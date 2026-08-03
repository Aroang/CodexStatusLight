using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace CodexMonitor.Core.Protocol;

public sealed class AppServerClient : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private Task? _receiveTask;
    private long _nextId;

    public event EventHandler<JsonElement>? Notification;

    public WebSocketState State => _socket.State;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _receiveTask = ReceiveLoopAsync(_shutdown.Token);

        await RequestAsync("initialize", new
        {
            clientInfo = new { name = "codex_monitor", title = "Codex Monitor", version = "0.1.0" },
            capabilities = new { experimentalApi = true }
        }, cancellationToken).ConfigureAwait(false);

        await SendAsync(new { method = "initialized" }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<JsonElement> RequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Unable to allocate a JSON-RPC request id.");
        }

        try
        {
            await SendAsync(parameters is null ? new { method, id } : new { method, id, @params = parameters }, cancellationToken)
                .ConfigureAwait(false);
            return await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task SendAsync(object payload, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }
                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                using var document = JsonDocument.Parse(message.ToArray());
                var root = document.RootElement;
                if (root.TryGetProperty("id", out var idNode) && idNode.TryGetInt64(out var id) && _pending.TryGetValue(id, out var completion))
                {
                    if (root.TryGetProperty("error", out var error))
                    {
                        completion.TrySetException(new InvalidOperationException(error.ToString()));
                    }
                    else if (root.TryGetProperty("result", out var response))
                    {
                        completion.TrySetResult(response.Clone());
                    }
                    continue;
                }

                if (root.TryGetProperty("method", out _))
                {
                    Notification?.Invoke(this, root.Clone());
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            foreach (var completion in _pending.Values)
            {
                completion.TrySetException(exception);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Codex Monitor stopped", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WebSocketException)
            {
            }
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _socket.Dispose();
        _shutdown.Dispose();
        _sendLock.Dispose();
    }
}
