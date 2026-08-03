using System.Net.Http;
using System.Net.WebSockets;
using System.Text.Json;
using CodexMonitor.Core.Models;
using CodexMonitor.Core.Protocol;
using CodexMonitor.Core.Runtime;

namespace CodexMonitor.App.Services;

public sealed class MonitorCoordinator : IAsyncDisposable
{
    private readonly CodexStateReducer _reducer = new();
    private readonly AppServerHost _host = new();
    private readonly CancellationTokenSource _shutdown = new();
    private AppServerClient? _client;
    private Task? _pollTask;
    private readonly string? _runtimeDescriptorPath;

    public MonitorCoordinator(string? runtimeDescriptorPath = null)
    {
        _runtimeDescriptorPath = runtimeDescriptorPath;
        _reducer.Changed += (_, snapshot) => SnapshotChanged?.Invoke(this, snapshot);
    }

    public event EventHandler<MonitorSnapshot>? SnapshotChanged;

    public void MarkUnavailable(string detail) => _reducer.MarkUnavailable(detail);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _host.StartAsync(cancellationToken).ConfigureAwait(false);
        var endpoint = _host.Endpoint ?? throw new InvalidOperationException("App Server 未提供监听地址。");
        _client = await ConnectWithRetryAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _client.Notification += (_, notification) => _reducer.ApplyNotification(notification);

        RuntimeDescriptorStore.Write(
            new RuntimeDescriptor(Environment.ProcessId, endpoint.ToString().TrimEnd('/'), DateTimeOffset.Now),
            _runtimeDescriptorPath);
        _pollTask = PollThreadsAsync(_shutdown.Token);
    }

    private static async Task<AppServerClient> ConnectWithRetryAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = new AppServerClient();
            try
            {
                await client.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
                return client;
            }
            catch (Exception exception) when (exception is WebSocketException or HttpRequestException)
            {
                lastError = exception;
                await client.DisposeAsync().ConfigureAwait(false);
                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("无法连接本地 Codex App Server。", lastError);
    }

    private async Task PollThreadsAsync(CancellationToken cancellationToken)
    {
        var knownThreads = new HashSet<string>(StringComparer.Ordinal);
        var consecutiveFailures = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // App Server requires params even when neither cursor nor limit is supplied.
                // Omitting the member produces JSON-RPC error -32600.
                var response = await _client!.RequestAsync("thread/loaded/list", new { }, cancellationToken).ConfigureAwait(false);
                if (response.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var idNode in data.EnumerateArray())
                    {
                        var threadId = idNode.GetString();
                        if (string.IsNullOrWhiteSpace(threadId))
                        {
                            continue;
                        }

                        var wasActive = _reducer.WasActive(threadId);
                        var initialDiscovery = knownThreads.Add(threadId);
                        var thread = await ReadThreadAsync(threadId, false, cancellationToken).ConfigureAwait(false);
                        var runtimeType = ReadRuntimeType(thread);

                        if (wasActive && runtimeType == "idle")
                        {
                            thread = await ReadThreadAsync(threadId, true, cancellationToken).ConfigureAwait(false);
                        }

                        _reducer.ApplyThread(thread, initialDiscovery);
                    }
                }

                consecutiveFailures = 0;
                _reducer.Tick();
                await Task.Delay(400, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 3)
                {
                    _reducer.MarkUnavailable($"监控连接异常：{exception.Message}");
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<JsonElement> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken)
    {
        var result = await _client!.RequestAsync("thread/read", new { threadId, includeTurns }, cancellationToken).ConfigureAwait(false);
        if (!result.TryGetProperty("thread", out var thread))
        {
            throw new InvalidOperationException("thread/read 响应缺少 thread 字段。");
        }
        return thread.Clone();
    }

    private static string? ReadRuntimeType(JsonElement thread)
    {
        return thread.TryGetProperty("status", out var status) && status.TryGetProperty("type", out var type)
            ? type.GetString()
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        RuntimeDescriptorStore.DeleteIfOwnedBy(Environment.ProcessId, _runtimeDescriptorPath);

        if (_pollTask is not null)
        {
            try
            {
                await _pollTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }

        await _host.DisposeAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
