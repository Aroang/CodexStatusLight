using System.ComponentModel;
using System.Runtime.CompilerServices;
using CodexMonitor.Core.Models;

namespace CodexMonitor.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private string _title = "正在连接 Codex";
    private string _detail = "启动本地 App Server";
    private string _workspace = "";
    private MonitorStatus _status = MonitorStatus.Starting;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<MonitorStatus>? AttentionRequested;

    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string Workspace { get => _workspace; private set => Set(ref _workspace, value); }
    public MonitorStatus Status => _status;
    public bool IsRedLightActive => _status is MonitorStatus.Waiting or MonitorStatus.Interrupted or MonitorStatus.Error;
    public bool IsYellowLightActive => _status == MonitorStatus.Working;
    public bool IsGreenLightActive => _status == MonitorStatus.Completed;

    public void Apply(MonitorSnapshot snapshot)
    {
        var previous = _status;
        _status = snapshot.Status;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(IsRedLightActive));
        OnPropertyChanged(nameof(IsYellowLightActive));
        OnPropertyChanged(nameof(IsGreenLightActive));
        Title = snapshot.Title;
        Detail = snapshot.Detail;

        var primary = snapshot.Sessions.FirstOrDefault();
        Workspace = primary?.Workspace ?? string.Empty;

        if (previous != snapshot.Status && snapshot.Status is MonitorStatus.Waiting or MonitorStatus.Interrupted or MonitorStatus.Error)
        {
            AttentionRequested?.Invoke(this, snapshot.Status);
        }
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
