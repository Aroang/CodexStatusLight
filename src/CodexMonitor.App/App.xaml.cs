using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using CodexMonitor.App.Services;
using CodexMonitor.App.ViewModels;

namespace CodexMonitor.App;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MonitorCoordinator? _coordinator;
    private MainWindow? _window;
    private Process? _parentProcess;

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = StartupOptions.Parse(e.Args);
        if (options.RuntimeFile is null)
        {
            _singleInstance = new Mutex(true, "Local\\CodexMonitor.App.SingleInstance", out var createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }
        }

        var settingsStore = new AppSettingsStore();
        var viewModel = new MainViewModel();
        _coordinator = new MonitorCoordinator(options.RuntimeFile, options.ExternalEndpoint);
        _coordinator.SnapshotChanged += (_, snapshot) => Dispatcher.BeginInvoke(() => viewModel.Apply(snapshot));

        _window = new MainWindow(viewModel, settingsStore, options.WindowOffset);
        MainWindow = _window;
        _window.Show();
        _ = StartCoordinatorAsync();
        if (options.ParentProcessId is { } parentProcessId)
        {
            _ = ExitWhenParentStopsAsync(parentProcessId);
        }
        else if (options.LifetimeFile is { } lifetimeFile)
        {
            _ = ExitWhenLifetimeFileDisappearsAsync(lifetimeFile);
        }
    }

    private async Task ExitWhenLifetimeFileDisappearsAsync(string lifetimeFile)
    {
        while (IsLifetimeActive(lifetimeFile))
        {
            await Task.Delay(500);
        }

        await Dispatcher.InvokeAsync(ExitApplication);
    }

    private static bool IsLifetimeActive(string lifetimeFile)
    {
        try
        {
            return File.Exists(lifetimeFile) &&
                   DateTime.UtcNow - File.GetLastWriteTimeUtc(lifetimeFile) < TimeSpan.FromSeconds(15);
        }
        catch (IOException)
        {
            return true;
        }
    }

    private async Task ExitWhenParentStopsAsync(int parentProcessId)
    {
        try
        {
            _parentProcess = Process.GetProcessById(parentProcessId);
            await _parentProcess.WaitForExitAsync();
        }
        catch (ArgumentException)
        {
            // The launcher exited before the monitor finished starting.
        }

        await Dispatcher.InvokeAsync(ExitApplication);
    }

    private async Task StartCoordinatorAsync()
    {
        try
        {
            await _coordinator!.StartAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            _coordinator!.MarkUnavailable($"无法启动：{exception.Message}");
        }
    }

    public void ShowMonitor()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }
        _window.Activate();
        _window.Topmost = true;
    }

    public void ExitApplication()
    {
        IsExiting = true;
        _window?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_coordinator is not null)
        {
            _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        _parentProcess?.Dispose();
        base.OnExit(e);
    }

    private sealed record StartupOptions(
        string? RuntimeFile,
        int? ParentProcessId,
        int WindowOffset,
        Uri? ExternalEndpoint,
        string? LifetimeFile)
    {
        public static StartupOptions Parse(IReadOnlyList<string> args)
        {
            string? runtimeFile = null;
            int? parentProcessId = null;
            var windowOffset = 0;
            Uri? externalEndpoint = null;
            string? lifetimeFile = null;

            for (var index = 0; index < args.Count; index++)
            {
                if (args[index] == "--session-runtime" && index + 1 < args.Count)
                {
                    runtimeFile = Path.GetFullPath(args[++index]);
                }
                else if (args[index] == "--parent-pid" && index + 1 < args.Count && int.TryParse(args[++index], out var pid))
                {
                    parentProcessId = pid;
                }
                else if (args[index] == "--window-offset" && index + 1 < args.Count && int.TryParse(args[++index], out var offset))
                {
                    windowOffset = Math.Max(0, offset);
                }
                else if (args[index] == "--connect" && index + 1 < args.Count &&
                         Uri.TryCreate(args[++index], UriKind.Absolute, out var endpoint) &&
                         endpoint.Scheme is "ws" or "wss" && endpoint.IsLoopback)
                {
                    externalEndpoint = endpoint;
                }
                else if (args[index] == "--lifetime-file" && index + 1 < args.Count)
                {
                    lifetimeFile = Path.GetFullPath(args[++index]);
                }
            }

            return new StartupOptions(runtimeFile, parentProcessId, windowOffset, externalEndpoint, lifetimeFile);
        }
    }
}
