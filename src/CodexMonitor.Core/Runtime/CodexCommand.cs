using System.Diagnostics;

namespace CodexMonitor.Core.Runtime;

public static class CodexCommand
{
    public static ProcessStartInfo CreateStartInfo(
        IEnumerable<string> arguments,
        bool redirectOutput = false,
        bool createNoWindow = false,
        bool redirectInput = false)
    {
        var command = Resolve();
        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = createNoWindow,
            RedirectStandardInput = redirectInput,
            RedirectStandardOutput = redirectOutput,
            RedirectStandardError = redirectOutput
        };

        if (OperatingSystem.IsWindows() && command.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = "powershell.exe";
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(command);
        }
        else if (OperatingSystem.IsWindows() &&
                 (command.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || command.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "发现 Codex CMD shim，但没有可安全调用的 codex.ps1 或 codex.exe。" +
                "请设置 CODEX_CLI_PATH 指向 codex.ps1 或原生 codex.exe。");
        }
        else
        {
            startInfo.FileName = command;
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    public static string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var names = OperatingSystem.IsWindows()
            ? new[] { "codex.exe", "codex.ps1", "codex.cmd", "codex.bat" }
            : new[] { "codex" };

        foreach (var directory in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                try
                {
                    var candidate = Path.Combine(directory.Trim('"'), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
                {
                }
            }
        }

        throw new FileNotFoundException(
            "找不到 Codex CLI。请确认 codex 已加入 PATH，或设置 CODEX_CLI_PATH 指向 Codex 可执行文件。");
    }
}
