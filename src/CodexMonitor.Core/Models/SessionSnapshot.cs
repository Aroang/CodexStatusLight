namespace CodexMonitor.Core.Models;

public sealed record SessionSnapshot(
    string ThreadId,
    string DisplayName,
    string Workspace,
    MonitorStatus Status,
    string Detail,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? ActiveSince = null,
    DateTimeOffset? FinishedAt = null);

public sealed record MonitorSnapshot(
    MonitorStatus Status,
    string Title,
    string Detail,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<SessionSnapshot> Sessions)
{
    public static MonitorSnapshot Starting { get; } = new(
        MonitorStatus.Starting,
        "正在连接 Codex",
        "启动本地 App Server",
        DateTimeOffset.Now,
        Array.Empty<SessionSnapshot>());
}
