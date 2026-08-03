using System.ComponentModel;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using CodexMonitor.App.Services;
using CodexMonitor.App.ViewModels;
using CodexMonitor.Core.Models;

namespace CodexMonitor.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly AppSettingsStore _settingsStore;
    private readonly System.Windows.Forms.NotifyIcon _trayIcon;
    private bool _muted;

    public MainWindow(MainViewModel viewModel, AppSettingsStore settingsStore)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settingsStore = settingsStore;
        DataContext = viewModel;

        var settings = settingsStore.Load();
        Topmost = settings.Topmost;
        _muted = settings.Muted;
        if (settings.Left is { } left && settings.Top is { } top)
        {
            Left = left;
            Top = top;
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Loaded += (_, _) => MoveToDefaultPosition();
        }

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
            Icon = SystemIcons.Application,
            Text = "Codex Monitor",
            Visible = true,
            ContextMenuStrip = menu
        };
        icon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowAndActivate);
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

    private void MoveToDefaultPosition()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 24;
        Top = workArea.Top + 24;
    }

    private void DragArea_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

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
