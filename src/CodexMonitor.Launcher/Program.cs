using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CodexMonitor.Core.Runtime;

namespace CodexMonitor.Launcher;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Any(x => string.Equals(x, "--remote", StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine("codex-watch 会自动管理 --remote 参数，请不要手动传入该选项。");
            return 2;
        }

        try
        {
            if (OperatingSystem.IsLinux())
            {
                return await RunFromWslAsync(args);
            }

            var terminalWindow = TerminalWindowTarget.CaptureForeground();
            var runtime = await EnsureMonitorAsync(terminalWindow);
            return await RunCodexAsync(runtime.Endpoint, args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"codex-watch: {exception.Message}");
            return 1;
        }
    }

    private static async Task<int> RunFromWslAsync(IReadOnlyList<string> args)
    {
        EnsureWsl();
        var sessionsDirectory = Path.Combine(RuntimeDescriptorStore.DirectoryPath, "sessions");
        Directory.CreateDirectory(sessionsDirectory);
        var windowOffset = Directory.EnumerateFiles(sessionsDirectory, "*.lifetime").Count();
        var sessionId = Guid.NewGuid().ToString("N");
        var runtimePath = Path.Combine(sessionsDirectory, sessionId + ".json");
        var lifetimePath = Path.Combine(sessionsDirectory, sessionId + ".lifetime");
        File.WriteAllText(runtimePath, string.Empty);
        File.WriteAllText(lifetimePath, Environment.ProcessId.ToString());
        using var heartbeatCancellation = new CancellationTokenSource();
        var heartbeatTask = HeartbeatAsync(lifetimePath, heartbeatCancellation.Token);

        Process? appServer = null;
        try
        {
            var endpoint = $"ws://127.0.0.1:{ReserveFreePort()}";
            var serverInfo = CodexCommand.CreateStartInfo(
                new[] { "app-server", "--listen", endpoint },
                redirectOutput: true,
                createNoWindow: true,
                redirectInput: true);
            appServer = Process.Start(serverInfo)
                ?? throw new InvalidOperationException("无法在 WSL 中启动 codex app-server。");
            // The WebSocket app server must not share the terminal's input with the
            // interactive Codex TUI. Closing its redirected stdin gives it EOF and
            // leaves the TUI as the only process reading keyboard events.
            appServer.StandardInput.Close();
            _ = DrainAsync(appServer.StandardOutput);
            _ = DrainAsync(appServer.StandardError);

            var monitorInfo = new ProcessStartInfo
            {
                FileName = FindMonitorExecutable(),
                UseShellExecute = false
            };
            monitorInfo.ArgumentList.Add("--session-runtime");
            monitorInfo.ArgumentList.Add(ConvertToWindowsPath(runtimePath));
            monitorInfo.ArgumentList.Add("--connect");
            monitorInfo.ArgumentList.Add(endpoint);
            monitorInfo.ArgumentList.Add("--lifetime-file");
            monitorInfo.ArgumentList.Add(ConvertToWindowsPath(lifetimePath));
            monitorInfo.ArgumentList.Add("--window-offset");
            monitorInfo.ArgumentList.Add(windowOffset.ToString());
            Process.Start(monitorInfo)?.Dispose();

            await WaitForMonitorAsync(runtimePath, endpoint, appServer);
            return await RunCodexAsync(endpoint, args);
        }
        finally
        {
            heartbeatCancellation.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch (OperationCanceledException)
            {
            }
            TryDelete(lifetimePath);
            TryDelete(runtimePath);
            if (appServer is not null)
            {
                try
                {
                    if (!appServer.HasExited)
                    {
                        appServer.Kill(entireProcessTree: true);
                        await appServer.WaitForExitAsync();
                    }
                }
                catch (InvalidOperationException)
                {
                }
                appServer.Dispose();
            }
        }
    }

    private static async Task HeartbeatAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            }
            catch (IOException)
            {
                // The Windows side may briefly hold the file while inspecting it.
            }
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static void EnsureWsl()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME")) ||
            !File.Exists("/proc/sys/fs/binfmt_misc/WSLInterop"))
        {
            throw new PlatformNotSupportedException(
                "Linux 启动器需要在已启用 Windows 程序互操作的 WSL 中运行。");
        }
    }

    private static async Task WaitForMonitorAsync(string runtimePath, string endpoint, Process appServer)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (appServer.HasExited)
            {
                throw new InvalidOperationException($"codex app-server 提前退出（退出码 {appServer.ExitCode}）。");
            }

            await Task.Delay(200);
            var runtime = RuntimeDescriptorStore.Read(runtimePath);
            if (runtime is not null && string.Equals(runtime.Endpoint, endpoint, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "Windows 悬浮窗连接 WSL 超时。请确认 WSL 的 localhostForwarding 未被禁用。");
    }

    private static string ConvertToWindowsPath(string path)
    {
        var info = new ProcessStartInfo
        {
            FileName = "wslpath",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("-w");
        info.ArgumentList.Add(path);
        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动 wslpath。");
        var result = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(result))
        {
            throw new InvalidOperationException($"无法将 WSL 路径转换为 Windows 路径：{path}");
        }
        return result;
    }

    private static int ReserveFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is not null)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private static async Task<RuntimeDescriptor> EnsureMonitorAsync(TerminalWindowTarget? terminalWindow)
    {
        var sessionsDirectory = Path.Combine(RuntimeDescriptorStore.DirectoryPath, "sessions");
        Directory.CreateDirectory(sessionsDirectory);
        CleanupStaleSessions(sessionsDirectory);
        var windowOffset = Directory.EnumerateFiles(sessionsDirectory, "*.json").Count();
        var runtimePath = Path.Combine(sessionsDirectory, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(runtimePath, string.Empty);

        var monitorPath = FindMonitorExecutable();
        var startInfo = new ProcessStartInfo
        {
            FileName = monitorPath,
            UseShellExecute = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        startInfo.ArgumentList.Add("--session-runtime");
        startInfo.ArgumentList.Add(runtimePath);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("--window-offset");
        startInfo.ArgumentList.Add(windowOffset.ToString());
        if (terminalWindow is not null)
        {
            startInfo.ArgumentList.Add("--terminal-window");
            startInfo.ArgumentList.Add(terminalWindow.WindowHandle.ToString());
            startInfo.ArgumentList.Add("--terminal-process-id");
            startInfo.ArgumentList.Add(terminalWindow.ProcessId.ToString());
        }
        Process.Start(startInfo)?.Dispose();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(12);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(200);
            var runtime = RuntimeDescriptorStore.Read(runtimePath);
            if (runtime is not null && runtime.IsAlive())
            {
                return runtime;
            }
        }

        throw new InvalidOperationException("悬浮窗启动超时。请直接运行 CodexMonitor.exe 查看错误状态。");
    }

    private static void CleanupStaleSessions(string sessionsDirectory)
    {
        foreach (var file in Directory.EnumerateFiles(sessionsDirectory, "*.json"))
        {
            var runtime = RuntimeDescriptorStore.Read(file);
            if (runtime is null || !runtime.IsAlive())
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static string FindMonitorExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_MONITOR_APP");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "CodexMonitor.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        throw new FileNotFoundException(
            "找不到 CodexMonitor.exe。请将 CodexMonitor.exe 与 codex-watch.exe 放在同一目录，" +
            "或设置 CODEX_MONITOR_APP 环境变量。",
            candidate);
    }

    private static async Task<int> RunCodexAsync(string endpoint, IReadOnlyList<string> args)
    {
        var codexArguments = new List<string> { "--remote", endpoint };
        codexArguments.AddRange(args);
        var startInfo = CodexCommand.CreateStartInfo(codexArguments);
        startInfo.WorkingDirectory = Environment.CurrentDirectory;

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Codex CLI。请确认 codex 已加入 PATH。");
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
