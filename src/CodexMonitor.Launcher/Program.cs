using System.Diagnostics;
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
            var runtime = await EnsureMonitorAsync();
            return await RunCodexAsync(runtime.Endpoint, args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"codex-watch: {exception.Message}");
            return 1;
        }
    }

    private static async Task<RuntimeDescriptor> EnsureMonitorAsync()
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
