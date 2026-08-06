using System.Text.Json;
using CodexMonitor.Core.Models;
using CodexMonitor.Core.Protocol;
using CodexMonitor.Core.Runtime;

var tests = new (string Name, Action Run)[]
{
    ("initial idle thread stays idle", InitialIdle),
    ("active thread becomes working", Working),
    ("approval flag becomes waiting", WaitingForApproval),
    ("user input flag becomes waiting", WaitingForUserInput),
    ("completed turn becomes completed", Completed),
    ("completed state remains until next turn", CompletedPersists),
    ("interrupted turn becomes interrupted", Interrupted),
    ("failed turn becomes error", Failed),
    ("retryable error stays working", RetryableError),
    ("unscoped terminal error becomes error", UnscopedTerminalError),
    ("waiting outranks all other sessions", AggregatePriority),
    ("invalid terminal target is safely rejected", InvalidTerminalTarget),
    ("background app server can detach from terminal input", RedirectedServerInput)
};

var failures = 0;
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void InitialIdle()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyThread(Parse("""{"id":"t1","cwd":"D:\\repo","status":{"type":"idle"}}"""), true);
    Expect(reducer.Snapshot.Status, MonitorStatus.Idle);
}

static void Working()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyThread(Parse("""{"id":"t1","status":{"type":"active","activeFlags":[]}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Working);
}

static void WaitingForApproval()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"thread/status/changed","params":{"threadId":"t1","status":{"type":"active","activeFlags":["waitingOnApproval"]}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Waiting);
    Contains(reducer.Snapshot.Detail, "批准");
}

static void WaitingForUserInput()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"thread/status/changed","params":{"threadId":"t1","status":{"type":"active","activeFlags":["waitingOnUserInput"]}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Waiting);
    Contains(reducer.Snapshot.Detail, "输入");
}

static void Completed()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"turn/started","params":{"threadId":"t1","turn":{"status":"inProgress"}}}"""));
    reducer.ApplyNotification(Parse("""{"method":"turn/completed","params":{"threadId":"t1","turn":{"status":"completed"}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Completed);
}

static void CompletedPersists()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"turn/completed","params":{"threadId":"t1","turn":{"status":"completed"}}}"""));
    reducer.Tick();
    Expect(reducer.Snapshot.Status, MonitorStatus.Completed);
}

static void Interrupted()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"turn/completed","params":{"threadId":"t1","turn":{"status":"interrupted"}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Interrupted);
}

static void Failed()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyThread(Parse("""{"id":"t1","status":{"type":"idle"},"turns":[{"status":"failed","error":{"message":"boom"}}]}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Error);
    Contains(reducer.Snapshot.Detail, "boom");
}

static void RetryableError()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"error","params":{"threadId":"t1","turnId":"turn1","willRetry":true,"error":{"message":"temporary"}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Working);
}

static void UnscopedTerminalError()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyNotification(Parse("""{"method":"error","params":{"willRetry":false,"error":{"message":"service unavailable"}}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Error);
}

static void AggregatePriority()
{
    var reducer = new CodexStateReducer();
    reducer.ApplyThread(Parse("""{"id":"working","status":{"type":"active","activeFlags":[]}}"""));
    reducer.ApplyNotification(Parse("""{"method":"error","params":{"threadId":"failed","turnId":"turn1","willRetry":false,"error":{"message":"failed"}}}"""));
    reducer.ApplyThread(Parse("""{"id":"waiting","status":{"type":"active","activeFlags":["waitingOnApproval"]}}"""));
    Expect(reducer.Snapshot.Status, MonitorStatus.Waiting);
}

static void InvalidTerminalTarget()
{
    var target = new TerminalWindowTarget(0, 1);
    Expect(target.TryActivate(), false);
}

static void RedirectedServerInput()
{
    var original = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
    try
    {
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", Environment.ProcessPath);
        var startInfo = CodexCommand.CreateStartInfo(
            new[] { "app-server", "--listen", "ws://127.0.0.1:12345" },
            redirectOutput: true,
            createNoWindow: true,
            redirectInput: true);

        Expect(startInfo.RedirectStandardInput, true);
        Expect(startInfo.RedirectStandardOutput, true);
        Expect(startInfo.RedirectStandardError, true);
    }
    finally
    {
        Environment.SetEnvironmentVariable("CODEX_CLI_PATH", original);
    }
}

static JsonElement Parse(string json)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement.Clone();
}

static void Expect<T>(T actual, T expected) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(actual, expected))
    {
        throw new InvalidOperationException($"expected {expected}, got {actual}");
    }
}

static void Contains(string actual, string expected)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"expected '{actual}' to contain '{expected}'");
    }
}
