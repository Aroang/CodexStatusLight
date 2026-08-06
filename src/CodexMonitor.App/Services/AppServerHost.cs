using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CodexMonitor.Core.Runtime;

namespace CodexMonitor.App.Services;

public sealed class AppServerHost : IAsyncDisposable
{
    private Process? _process;
    private ChildProcessJob? _job;

    public Uri? Endpoint { get; private set; }
    public event EventHandler<string>? Diagnostic;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var port = ReserveFreePort();
        Endpoint = new Uri($"ws://127.0.0.1:{port}");

        var startInfo = CodexCommand.CreateStartInfo(
            new[] { "app-server", "--listen", Endpoint.ToString().TrimEnd('/') },
            redirectOutput: true,
            createNoWindow: true,
            redirectInput: true);

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, eventArgs) => ForwardDiagnostic(eventArgs.Data);
        _process.ErrorDataReceived += (_, eventArgs) => ForwardDiagnostic(eventArgs.Data);
        if (!_process.Start())
        {
            throw new InvalidOperationException("无法启动 codex app-server。请确认 Codex CLI 已加入 PATH。");
        }
        _process.StandardInput.Close();

        _job = ChildProcessJob.CreateKillOnClose();
        _job.Add(_process);

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private void ForwardDiagnostic(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            Diagnostic?.Invoke(this, value);
        }
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            _process.Dispose();
            _process = null;
            _job?.Dispose();
            _job = null;
        }
    }
}
