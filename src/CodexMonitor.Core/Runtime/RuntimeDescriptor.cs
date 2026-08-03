using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace CodexMonitor.Core.Runtime;

public sealed record RuntimeDescriptor(int MonitorProcessId, string Endpoint, DateTimeOffset StartedAt)
{
    public bool IsAlive()
    {
        try
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("ws" or "wss") ||
                !IPAddress.TryParse(endpoint.Host, out var address) ||
                !IPAddress.IsLoopback(address))
            {
                return false;
            }

            using var process = Process.GetProcessById(MonitorProcessId);
            var startDifference = (process.StartTime.ToUniversalTime() - StartedAt.UtcDateTime).Duration();
            return !process.HasExited &&
                   process.ProcessName.Equals("CodexMonitor", StringComparison.OrdinalIgnoreCase) &&
                   startDifference < TimeSpan.FromMinutes(1);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}

public static class RuntimeDescriptorStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DirectoryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexMonitor");

    public static string FilePath => Path.Combine(DirectoryPath, "runtime.json");

    public static RuntimeDescriptor? Read(string? filePath = null)
    {
        filePath ??= FilePath;
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<RuntimeDescriptor>(File.ReadAllText(filePath), Options);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Write(RuntimeDescriptor descriptor, string? filePath = null)
    {
        filePath ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temporaryPath = filePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(descriptor, Options));
        File.Move(temporaryPath, filePath, true);
    }

    public static void DeleteIfOwnedBy(int processId, string? filePath = null)
    {
        filePath ??= FilePath;
        var descriptor = Read(filePath);
        if (descriptor?.MonitorProcessId == processId && File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
