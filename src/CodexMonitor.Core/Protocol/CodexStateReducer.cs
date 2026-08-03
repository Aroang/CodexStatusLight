using System.Text.Json;
using CodexMonitor.Core.Models;

namespace CodexMonitor.Core.Protocol;

public sealed class CodexStateReducer
{
    private readonly Dictionary<string, MutableSession> _sessions = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public event EventHandler<MonitorSnapshot>? Changed;

    public MonitorSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return BuildSnapshot(DateTimeOffset.Now);
            }
        }
    }

    public bool WasActive(string threadId)
    {
        lock (_gate)
        {
            return _sessions.TryGetValue(threadId, out var session) &&
                   session.Status is MonitorStatus.Working or MonitorStatus.Waiting;
        }
    }

    public void ApplyThread(JsonElement thread, bool isInitialDiscovery = false)
    {
        if (!thread.TryGetProperty("id", out var idNode) || string.IsNullOrWhiteSpace(idNode.GetString()))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var threadId = idNode.GetString()!;

        lock (_gate)
        {
            var existed = _sessions.TryGetValue(threadId, out var session);
            session ??= new MutableSession(threadId);
            UpdateMetadata(session, thread);

            if (thread.TryGetProperty("status", out var status))
            {
                ApplyRuntimeStatus(session, status, isInitialDiscovery || !existed, now);
            }

            if (thread.TryGetProperty("turns", out var turns) && turns.ValueKind == JsonValueKind.Array)
            {
                ApplyLastTurn(session, turns, now);
            }

            session.UpdatedAt = now;
            _sessions[threadId] = session;
            RaiseChangedLocked(now);
        }
    }

    public void ApplyNotification(JsonElement message)
    {
        if (!message.TryGetProperty("method", out var methodNode))
        {
            return;
        }

        var method = methodNode.GetString();
        if (!message.TryGetProperty("params", out var parameters))
        {
            return;
        }

        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            switch (method)
            {
                case "thread/status/changed":
                    if (TryGetThreadId(parameters, out var statusThreadId) &&
                        parameters.TryGetProperty("status", out var runtimeStatus))
                    {
                        var statusSession = GetOrCreate(statusThreadId);
                        ApplyRuntimeStatus(statusSession, runtimeStatus, false, now);
                        statusSession.UpdatedAt = now;
                    }
                    break;

                case "turn/started":
                    if (TryGetThreadId(parameters, out var startedThreadId))
                    {
                        SetStatus(GetOrCreate(startedThreadId), MonitorStatus.Working, "Agent 正在工作", now);
                    }
                    break;

                case "turn/completed":
                    if (TryGetThreadId(parameters, out var completedThreadId))
                    {
                        var completedSession = GetOrCreate(completedThreadId);
                        var status = MonitorStatus.Completed;
                        var detail = "任务执行完成";
                        if (parameters.TryGetProperty("turn", out var turn))
                        {
                            (status, detail) = ReadTurnOutcome(turn);
                        }

                        SetStatus(completedSession, status, detail, now);
                    }
                    break;

                case "error":
                {
                    var willRetry = parameters.TryGetProperty("willRetry", out var retryNode) && retryNode.GetBoolean();
                    var detail = ReadErrorMessage(parameters) ?? (willRetry ? "发生错误，Codex 正在重试" : "Codex 执行失败");
                    var errorThreadId = TryGetThreadId(parameters, out var reportedThreadId)
                        ? reportedThreadId
                        : "__monitor__";
                    var errorSession = GetOrCreate(errorThreadId);
                    if (errorThreadId == "__monitor__")
                    {
                        errorSession.DisplayName = "Codex Monitor";
                    }
                    SetStatus(errorSession, willRetry ? MonitorStatus.Working : MonitorStatus.Error, detail, now);
                    break;
                }

                case "thread/closed":
                    if (TryGetThreadId(parameters, out var closedThreadId))
                    {
                        _sessions.Remove(closedThreadId);
                    }
                    break;
            }

            RaiseChangedLocked(now);
        }
    }

    public void MarkUnavailable(string detail)
    {
        var now = DateTimeOffset.Now;
        lock (_gate)
        {
            var session = GetOrCreate("__monitor__");
            session.DisplayName = "Codex Monitor";
            SetStatus(session, MonitorStatus.Error, detail, now);
            RaiseChangedLocked(now);
        }
    }

    public void Tick()
    {
        // Terminal results intentionally remain visible until a subsequent
        // App Server event changes the session state.
    }

    private MutableSession GetOrCreate(string threadId)
    {
        if (_sessions.TryGetValue(threadId, out var existing))
        {
            return existing;
        }

        var session = new MutableSession(threadId);
        _sessions[threadId] = session;
        return session;
    }

    private static bool TryGetThreadId(JsonElement parameters, out string threadId)
    {
        threadId = string.Empty;
        if (!parameters.TryGetProperty("threadId", out var node))
        {
            return false;
        }

        threadId = node.GetString() ?? string.Empty;
        return threadId.Length > 0;
    }

    private static void UpdateMetadata(MutableSession session, JsonElement thread)
    {
        if (thread.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
        {
            session.DisplayName = name.GetString() ?? session.DisplayName;
        }

        if (thread.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String)
        {
            session.Workspace = cwd.GetString() ?? session.Workspace;
        }

        if (string.IsNullOrWhiteSpace(session.DisplayName) && !string.IsNullOrWhiteSpace(session.Workspace))
        {
            session.DisplayName = Path.GetFileName(session.Workspace.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
    }

    private static void ApplyRuntimeStatus(MutableSession session, JsonElement status, bool initialDiscovery, DateTimeOffset now)
    {
        if (!status.TryGetProperty("type", out var typeNode))
        {
            return;
        }

        switch (typeNode.GetString())
        {
            case "active":
                var flags = status.TryGetProperty("activeFlags", out var activeFlags) && activeFlags.ValueKind == JsonValueKind.Array
                    ? activeFlags.EnumerateArray().Select(x => x.GetString()).Where(x => x is not null).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string?>();
                if (flags.Contains("waitingOnApproval"))
                {
                    SetStatus(session, MonitorStatus.Waiting, "等待批准操作", now);
                }
                else if (flags.Contains("waitingOnUserInput"))
                {
                    SetStatus(session, MonitorStatus.Waiting, "等待用户输入", now);
                }
                else
                {
                    SetStatus(session, MonitorStatus.Working, "Agent 正在工作", now);
                }
                break;
            case "systemError":
                SetStatus(session, MonitorStatus.Error, "Codex 系统错误", now);
                break;
            case "idle":
                if (initialDiscovery || session.Status == MonitorStatus.Idle)
                {
                    SetStatus(session, MonitorStatus.Idle, "等待新任务", now);
                }
                break;
            case "notLoaded":
                SetStatus(session, MonitorStatus.Idle, "会话未加载", now);
                break;
        }
    }

    private static void ApplyLastTurn(MutableSession session, JsonElement turns, DateTimeOffset now)
    {
        JsonElement? last = null;
        foreach (var turn in turns.EnumerateArray())
        {
            last = turn;
        }

        if (last is not { } lastTurn)
        {
            return;
        }

        var (status, detail) = ReadTurnOutcome(lastTurn);
        if (status != MonitorStatus.Working)
        {
            SetStatus(session, status, detail, now);
        }
    }

    private static (MonitorStatus Status, string Detail) ReadTurnOutcome(JsonElement turn)
    {
        var value = turn.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        return value switch
        {
            "failed" => (MonitorStatus.Error, ReadErrorMessage(turn) ?? "任务执行失败"),
            "interrupted" => (MonitorStatus.Interrupted, "任务已中断"),
            "inProgress" => (MonitorStatus.Working, "Agent 正在工作"),
            _ => (MonitorStatus.Completed, "任务执行完成")
        };
    }

    private static string? ReadErrorMessage(JsonElement element)
    {
        if (!element.TryGetProperty("error", out var error))
        {
            return null;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        foreach (var property in new[] { "message", "details" })
        {
            if (error.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static void SetStatus(MutableSession session, MonitorStatus status, string detail, DateTimeOffset now)
    {
        if (session.Status is not (MonitorStatus.Working or MonitorStatus.Waiting) &&
            status is MonitorStatus.Working or MonitorStatus.Waiting)
        {
            session.ActiveSince = now;
        }

        session.Status = status;
        session.Detail = detail;
        session.UpdatedAt = now;
        session.FinishedAt = status is MonitorStatus.Completed or MonitorStatus.Interrupted or MonitorStatus.Error ? now : null;
    }

    private MonitorSnapshot BuildSnapshot(DateTimeOffset now)
    {
        var visible = _sessions.Values
            .Where(x => x.ThreadId != "__monitor__" || x.Status != MonitorStatus.Idle)
            .Select(x => x.ToSnapshot())
            .OrderByDescending(x => Priority(x.Status))
            .ThenByDescending(x => x.UpdatedAt)
            .ToArray();

        if (visible.Length == 0)
        {
            return new MonitorSnapshot(MonitorStatus.Idle, "Codex 空闲", "等待启动任务", now, visible);
        }

        var primary = visible[0];
        var title = primary.Status switch
        {
            MonitorStatus.Waiting => "Codex 等待输入",
            MonitorStatus.Error => "Codex 发生错误",
            MonitorStatus.Working => "Codex 正在工作",
            MonitorStatus.Completed => "Codex 已完成",
            MonitorStatus.Interrupted => "Codex 已中断",
            MonitorStatus.Starting => "正在连接 Codex",
            _ => "Codex 空闲"
        };

        if (visible.Count(x => x.Status is MonitorStatus.Working or MonitorStatus.Waiting) > 1)
        {
            title += $" · {visible.Count(x => x.Status is MonitorStatus.Working or MonitorStatus.Waiting)} 个会话";
        }

        return new MonitorSnapshot(primary.Status, title, primary.Detail, now, visible);
    }

    private static int Priority(MonitorStatus status) => status switch
    {
        MonitorStatus.Waiting => 60,
        MonitorStatus.Error => 50,
        MonitorStatus.Interrupted => 45,
        MonitorStatus.Working => 40,
        MonitorStatus.Completed => 30,
        MonitorStatus.Starting => 20,
        _ => 10
    };

    private void RaiseChangedLocked(DateTimeOffset now) => Changed?.Invoke(this, BuildSnapshot(now));

    private sealed class MutableSession(string threadId)
    {
        public string ThreadId { get; } = threadId;
        public string DisplayName { get; set; } = threadId.Length > 12 ? threadId[..12] : threadId;
        public string Workspace { get; set; } = string.Empty;
        public MonitorStatus Status { get; set; } = MonitorStatus.Idle;
        public string Detail { get; set; } = "等待新任务";
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset? ActiveSince { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }

        public SessionSnapshot ToSnapshot() => new(
            ThreadId,
            DisplayName,
            Workspace,
            Status,
            Detail,
            UpdatedAt,
            ActiveSince,
            FinishedAt);
    }
}
