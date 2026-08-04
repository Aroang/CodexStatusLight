using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using CodexMonitor.App.Services;
using CodexMonitor.App.ViewModels;
using CodexMonitor.Core.Models;
using CodexMonitor.Core.Runtime;

namespace CodexMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsStore _settingsStore;
    private readonly TerminalWindowTarget? _terminalWindow;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private bool _muted;
    private System.Windows.Point? _mouseDownPosition;
    private bool _dragStarted;

    public MainWindow(
        MainViewModel viewModel,
        AppSettingsStore settingsStore,
        TerminalWindowTarget? terminalWindow,
        int windowOffset = 0)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        _terminalWindow = terminalWindow;
        DataContext = viewModel;

        var settings = settingsStore.Load();
        Topmost = settings.Topmost;
        _muted = settings.Muted;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Loaded += (_, _) => MoveToInitialPosition(settings, windowOffset);

        _trayIcon = CreateTrayIcon();
        _viewModel.AttentionRequested += OnAttentionRequested;
    }

    private System.Windows.Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("显示悬浮窗", null, (_, _) => Dispatcher.Invoke(ShowAndActivate));
        menu.Items.Add("切换置顶", null, (_, _) => Dispatcher.Invoke(() => Topmost = !Topmost));
        menu.Items.Add("切换静音", null, (_, _) => _muted = !_muted);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(() => ((App)System.Windows.Application.Current).ExitApplication()));

        var icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Codex Monitor",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAndActivate);
        icon.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(ActivateTerminal);
        return icon;
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnAttentionRequested(object? sender, MonitorStatus status)
    {
        if (_muted)
        {
            return;
        }

        _trayIcon.BalloonTipTitle = status == MonitorStatus.Waiting ? "Codex 等待你的输入" : "Codex 执行出错";
        _trayIcon.BalloonTipText = _viewModel.Detail;
        _trayIcon.BalloonTipIcon = status == MonitorStatus.Waiting
            ? System.Windows.Forms.ToolTipIcon.Warning
            : System.Windows.Forms.ToolTipIcon.Error;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void MoveToInitialPosition(AppSettings settings, int windowOffset)
    {
        var workArea = SystemParameters.WorkArea;
        var baseLeft = settings.Left ?? workArea.Right - ActualWidth - 24;
        var baseTop = settings.Top ?? workArea.Top + 24;
        var rowsPerColumn = Math.Max(1, (int)((workArea.Height - 48) / (ActualHeight + 12)));
        var row = windowOffset % rowsPerColumn;
        var column = windowOffset / rowsPerColumn;

        Left = Math.Max(workArea.Left, baseLeft - column * (ActualWidth + 12));
        Top = Math.Min(workArea.Bottom - ActualHeight, baseTop + row * (ActualHeight + 12));
    }

    private void DragArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            _mouseDownPosition = e.GetPosition(this);
            _dragStarted = false;
            Mouse.Capture((IInputElement)sender);
            e.Handled = true;
        }
    }

    private void DragArea_OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_mouseDownPosition is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(this);
        if (Math.Abs(current.X - start.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dragStarted = true;
        _mouseDownPosition = null;
        Mouse.Capture(null);
        DragMove();
    }

    private void DragArea_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        Mouse.Capture(null);
        var shouldActivate = !_dragStarted && _mouseDownPosition is not null;
        _mouseDownPosition = null;
        _dragStarted = false;
        e.Handled = true;

        if (shouldActivate)
        {
            ActivateTerminal();
        }
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void ActivateTerminal() => _terminalWindow?.TryActivate();

    protected override void OnClosing(CancelEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        if (!app.IsExiting)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _settingsStore.Save(new AppSettings(Left, Top, Topmost, _muted, false));
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.OnClosing(e);
    }
}
