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

    public bool IsExiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, "Local\\CodexMonitor.App.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        var settingsStore = new AppSettingsStore();
        var viewModel = new MainViewModel();
        _coordinator = new MonitorCoordinator();
        _coordinator.SnapshotChanged += (_, snapshot) => Dispatcher.BeginInvoke(() => viewModel.Apply(snapshot));

        _window = new MainWindow(viewModel, settingsStore);
        MainWindow = _window;
        _window.Show();
        _ = StartCoordinatorAsync();
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
        base.OnExit(e);
    }
}
