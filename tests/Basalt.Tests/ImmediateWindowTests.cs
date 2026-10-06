using System.Text.Json.Nodes;
using Basalt.Core.Services;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Debugging;
using Basalt.Workspace.Immediate;

namespace Basalt.Tests;

/// <summary>
/// The Immediate window's line parsing, history, routing, and the requests it
/// sends to the debug adapter. Evaluation in a real child process is in
/// <see cref="ImmediateEvaluationTests"/>.
/// </summary>
public sealed class ImmediateWindowTests
{
    [Theory]
    [InlineData("?1+2", ImmediateKind.Print, "1+2")]
    [InlineData("  ? x ", ImmediateKind.Print, "x")]
    [InlineData("Print x", ImmediateKind.Print, "x")]
    [InlineData("print(x)", ImmediateKind.Print, "(x)")]
    [InlineData("x = 3", ImmediateKind.Statement, "x = 3")]
    // A name that merely starts with the word is an assignment.
    [InlineData("PrintCount = 3", ImmediateKind.Statement, "PrintCount = 3")]
    [InlineData("", ImmediateKind.Empty, "")]
    [InlineData("   ", ImmediateKind.Empty, "")]
    [InlineData("?", ImmediateKind.Empty, "")]
    public void ALineIsAPrintOrAStatement(string line, ImmediateKind kind, string text)
    {
        var input = ImmediateInput.Parse(line);

        Assert.Equal(kind, input.Kind);
        Assert.Equal(text, input.Text);
    }

    [Fact]
    public void HistoryWalksBackAndForwardAndEndsOnTheEmptyLine()
    {
        var history = new ImmediateHistory();
        history.Add("?1");
        history.Add("?2");
        history.Add("?2"); // a repeat must not make Up stand still
        history.Add("  ");
        history.Add("?3");

        Assert.Equal(["?1", "?2", "?3"], history.Lines);

        Assert.Equal("?3", history.Previous());
        Assert.Equal("?2", history.Previous());
        Assert.Equal("?1", history.Previous());
        Assert.Null(history.Previous());

        Assert.Equal("?2", history.Next());
        Assert.Equal("?3", history.Next());
        Assert.Equal("", history.Next());
        Assert.Null(history.Next());

        // Submitting starts the walk again from the newest line.
        history.Previous();
        history.Add("?4");
        Assert.Equal("?4", history.Previous());
    }

    [Fact]
    public async Task ARunningProgramIsRefusedPolitelyAndNothingIsEvaluated()
    {
        var debugger = new FakeImmediateDebugger { IsDebugging = true, IsPaused = false };
        var designTime = new RecordingEvaluator();
        var session = new ImmediateSession(debugger, designTime);

        var outcome = await session.SubmitAsync("?x");

        Assert.Equal(ImmediateOutcomeKind.Refused, outcome.Kind);
        Assert.Contains("running", outcome.Text);
        Assert.Empty(debugger.Asked);
        Assert.Equal(0, designTime.Calls);
    }

    [Fact]
    public async Task APausedProgramIsAskedInItsFrameAndTheDesignTimeEvaluatorIsNot()
    {
        var debugger = new FakeImmediateDebugger { IsDebugging = true, IsPaused = true };
        var designTime = new RecordingEvaluator();
        var session = new ImmediateSession(debugger, designTime);

        var value = await session.SubmitAsync("?x");

        Assert.Equal(("x", true), Assert.Single(debugger.Asked));
        Assert.Equal("answer", value.Text);
        Assert.Equal(0, designTime.Calls);
    }

    [Fact]
    public async Task AStatementWhileDebuggingIsRefusedWithoutAskingTheDebugger()
    {
        var debugger = new FakeImmediateDebugger { IsDebugging = true, IsPaused = true };
        var session = new ImmediateSession(debugger, new RecordingEvaluator());

        var outcome = await session.SubmitAsync("x = 3");

        Assert.Equal(ImmediateOutcomeKind.Refused, outcome.Kind);
        Assert.Contains("Statements can't run while debugging", outcome.Text);
        Assert.Empty(debugger.Asked);
    }

    [Fact]
    public async Task ADebuggerThatCannotEvaluateSaysSo()
    {
        var debugger = new FakeImmediateDebugger { IsDebugging = true, IsPaused = true, Answer = new ReplEvaluation(false, "") };
        var session = new ImmediateSession(debugger, new RecordingEvaluator());

        var outcome = await session.SubmitAsync("?nope");

        Assert.Equal(ImmediateOutcomeKind.RuntimeError, outcome.Kind);
        Assert.NotEmpty(outcome.Text);
    }

    [Fact]
    public async Task WithNoSessionTheLineGoesToTheDesignTimeEvaluator()
    {
        var designTime = new RecordingEvaluator();
        var session = new ImmediateSession(new FakeImmediateDebugger(), designTime);

        await session.SubmitAsync("?1+2");

        Assert.Equal(1, designTime.Calls);
        Assert.Equal(new ImmediateInput(ImmediateKind.Print, "1+2"), designTime.Last);
    }

    [Fact]
    public async Task EvaluationRunsOffTheCallingThreadWithoutBlockingIt()
    {
        var gate = new ManualResetEventSlim();
        var designTime = new RecordingEvaluator { Gate = gate };
        var session = new ImmediateSession(null, designTime);
        var callerThread = Environment.CurrentManagedThreadId;

        var pending = session.SubmitAsync("?1");

        // Returned while the evaluator is still held: the caller is free.
        Assert.False(pending.IsCompleted);

        // Wait, still on the calling thread, until the evaluator has started:
        // the calling thread is busy here, so the evaluator cannot be running
        // on it. Awaiting first would hand this thread back to the pool, which
        // could then run the evaluator on it and make the ids match by chance.
        Assert.True(designTime.Started.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(callerThread, designTime.ThreadId);

        gate.Set();
        await pending;
    }

    [Fact]
    public async Task APausedEvaluationAlsoRunsOffTheCallingThread()
    {
        // Same race as EvaluationRunsOffTheCallingThreadWithoutBlockingIt
        // above, and the same fix: awaiting the pending task right away lets
        // the thread pool hand this very thread the queued work once the
        // await yields it back, since nothing here keeps it busy in the
        // meantime — making the two ids equal by reusing a thread, not by
        // evaluating on the calling one. Blocking on Started first, rather
        // than awaiting, keeps this thread occupied until the evaluation has
        // provably started elsewhere.
        var gate = new ManualResetEventSlim();
        var debugger = new FakeImmediateDebugger { IsDebugging = true, IsPaused = true, Gate = gate };
        var session = new ImmediateSession(debugger, new RecordingEvaluator());
        var callerThread = Environment.CurrentManagedThreadId;

        var pending = session.SubmitAsync("?x");

        Assert.True(debugger.Started.Wait(TimeSpan.FromSeconds(10)));
        Assert.NotEqual(callerThread, debugger.ThreadId);

        gate.Set();
        await pending;
    }

    // The request the IDE puts on the wire is the contract with netcoredbg.

    [Fact]
    public async Task TheEvaluateRequestCarriesTheReplContextAndTheFrameBeingLookedAt()
    {
        var (service, dap) = await LaunchedAndPausedAsync();

        var answer = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 1);

        Assert.True(answer.Succeeded);
        Assert.Equal("7", answer.Text);

        var request = dap.ArgumentsOf("evaluate");
        Assert.NotNull(request);
        Assert.Equal("repl", request["context"]!.GetValue<string>());
        Assert.Equal(101, request["frameId"]!.GetValue<int>());
        Assert.Equal("x", request["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task AValueAskedForInABasicFileIsTranslatedToTheEvaluatorsSyntax()
    {
        var (service, dap) = await LaunchedAndPausedAsync();

        await service.EvaluateReplAsync("i = 4", isExpression: true, frameIndex: 0);
        Assert.Equal("i == 4", dap.ArgumentsOf("evaluate")!["expression"]!.GetValue<string>());
        Assert.Equal(100, dap.ArgumentsOf("evaluate")!["frameId"]!.GetValue<int>());

        await service.EvaluateReplAsync("s = \"C:\\temp \"\"q\"\"\"", isExpression: true, frameIndex: 0);
        Assert.Equal("s == \"C:\\\\temp \\\"q\\\"\"", dap.ArgumentsOf("evaluate")!["expression"]!.GetValue<string>());

        // Frame 1 is a C# file: nothing to translate there.
        await service.EvaluateReplAsync("i = 4", isExpression: true, frameIndex: 1);
        Assert.Equal("i = 4", dap.ArgumentsOf("evaluate")!["expression"]!.GetValue<string>());
    }

    [Fact]
    public async Task AStatementIsRefusedByTheServiceAndNeverSent()
    {
        var (service, dap) = await LaunchedAndPausedAsync();

        var answer = await service.EvaluateReplAsync("x = 3", isExpression: false, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("Statements can't run while debugging", answer.Text);
        Assert.Null(dap.ArgumentsOf("evaluate"));
    }

    [Fact]
    public async Task TheInterpretersDefaultAlsoRefusesStatements()
    {
        var interpreter = new Basalt.Workspace.Debugging.InterpreterDebugService(
            new Basalt.QuickBasic.Interpretation.QuickBasicInterpreter());

        var answer = await ((IDebugSessionService)interpreter).EvaluateReplAsync("x = 3", isExpression: false, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("Statements can't run while debugging", answer.Text);
    }

    [Fact]
    public async Task ASessionStoppedMidEvaluationIsReportedAsEnded()
    {
        var (service, dap) = await LaunchedAndPausedAsync();

        // What a client disposed by Stop throws when a request is in flight.
        dap.EvaluateAnswer = _ => throw new ObjectDisposedException("DapClient");

        var answer = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("session has ended", answer.Text);
    }

    [Fact]
    public async Task AMalformedAnswerSurfacesAsTheBugItIsInsteadOfPassingForAnEndedSession()
    {
        var (service, dap) = await LaunchedAndPausedAsync();
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(true, new JsonObject { ["result"] = new JsonObject() });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0));
    }

    [Fact]
    public async Task AnInvalidOperationAfterStopIsAnEndedSession()
    {
        var (service, dap) = await LaunchedAndPausedAsync();

        // The client dies under the request the way a disposed Process does.
        dap.EvaluateAnswer = _ =>
        {
            service.Dispose();
            throw new InvalidOperationException("No process is associated with this object.");
        };

        var answer = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("session has ended", answer.Text);
    }

    [Fact]
    public async Task AProgramThatMovedOnWhileTheStackWasFetchedIsNotEvaluatedIn()
    {
        var (service, dap) = await LaunchedAndPausedAsync();
        dap.OnRequest = command =>
        {
            if (command == "stackTrace") dap.Raise("continued", new JsonObject());
        };

        var answer = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("moved on", answer.Text);
        Assert.Null(dap.ArgumentsOf("evaluate"));
    }

    [Fact]
    public async Task AStopTheUserWasNeverShownIsNotEvaluatedIn()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        await service.LaunchAsync("/app/App.dll");

        // The adapter has said "stopped" but the stack that decides whether
        // this stop is shown (a breakpoint waiting for its count may resume
        // it silently) has not come back yet.
        var held = new TaskCompletionSource();
        dap.HoldNextStackTrace = held;
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1 });
        Assert.True(service.IsPaused);

        var early = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0);

        Assert.False(early.Succeeded);
        Assert.Null(dap.ArgumentsOf("evaluate"));

        held.SetResult();
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True((await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0)).Succeeded);
    }

    [Fact]
    public async Task OutputWithNoCategoryIsConsoleOutputAsTheProtocolSays()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        var printed = new List<string>();
        service.DebugOutputReceived += (_, text) => printed.Add(text);

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("output", new JsonObject { ["output"] = "uncategorised" });
        dap.Raise("output", new JsonObject { ["category"] = "stderr", ["output"] = "error text" });

        Assert.Equal(["uncategorised"], printed);
    }

    [Fact]
    public async Task AFailedEvaluationReturnsTheAdaptersMessage()
    {
        var (service, dap) = await LaunchedAndPausedAsync();
        dap.EvaluateAnswer = _ => FakeDapConnection.Answer(false, new JsonObject(), "The name 'y' does not exist in the current context");

        var answer = await service.EvaluateReplAsync("y", isExpression: true, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Contains("does not exist", answer.Text);
    }

    [Fact]
    public async Task NothingIsSentWhileTheProgramIsRunning()
    {
        var (service, dap) = await LaunchedAndPausedAsync();
        dap.Raise("continued", new JsonObject());

        var answer = await service.EvaluateReplAsync("x", isExpression: true, frameIndex: 0);

        Assert.False(answer.Succeeded);
        Assert.Null(dap.ArgumentsOf("evaluate"));
    }

    [Fact]
    public async Task DebugOutputIsRoutedToTheImmediateEventAndStillToTheOutputOne()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        var immediate = new List<string>();
        var everything = new List<string>();
        service.DebugOutputReceived += (_, text) => immediate.Add(text);
        service.OutputReceived += (_, text) => everything.Add(text);

        await service.LaunchAsync("/app/App.dll");

        dap.Raise("output", new JsonObject { ["category"] = "console", ["output"] = "Debug.Print says hi\n" });
        dap.Raise("output", new JsonObject { ["category"] = "stdout", ["output"] = "Console.WriteLine\n" });

        Assert.Equal(["Debug.Print says hi\n"], immediate);
        Assert.Equal(["Debug.Print says hi\n", "Console.WriteLine\n"], everything);
    }

    [Fact]
    public async Task TheSessionEvaluatesInTheSelectedFrameAndForwardsDebugOutput()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        using var debug = new DebugSession();
        var printed = new List<string>();
        debug.DebugOutputReceived += (_, text) => printed.Add(text);

        var paused = new TaskCompletionSource();
        debug.Paused += (_, _) => paused.TrySetResult();

        await debug.StartWithAsync(service, "/app/App.dll", null);
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1 });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        debug.SelectedFrame = 1;
        var answer = await debug.EvaluateReplAsync("y", isExpression: true);

        Assert.True(answer.Succeeded);
        Assert.Equal(101, dap.ArgumentsOf("evaluate")!["frameId"]!.GetValue<int>());

        dap.Raise("output", new JsonObject { ["category"] = "console", ["output"] = "traced" });
        Assert.Equal(["traced"], printed);
    }

    private static async Task<(NetCoreDebugService Service, FakeDapConnection Dap)> LaunchedAndPausedAsync()
    {
        var dap = new FakeDapConnection();
        var service = new NetCoreDebugService(() => dap);
        var paused = new TaskCompletionSource();
        service.Paused += (_, _) => paused.TrySetResult();

        await service.LaunchAsync("/app/App.dll");
        dap.Raise("stopped", new JsonObject { ["threadId"] = 1 });
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(10));

        return (service, dap);
    }

    private sealed class FakeImmediateDebugger : IImmediateDebugger
    {
        public bool IsDebugging { get; set; }
        public bool IsPaused { get; set; }
        public ReplEvaluation Answer { get; set; } = new(true, "answer");
        public List<(string Text, bool IsExpression)> Asked { get; } = [];
        public int ThreadId { get; private set; }

        /// <summary>
        /// Held by a test that needs to see the evaluation start before
        /// letting it finish, the same way <see cref="RecordingEvaluator"/>'s
        /// own gate does. Unused (and so never waited on) by every other
        /// test, which never sets it.
        /// </summary>
        public ManualResetEventSlim? Gate { get; init; }

        /// <summary>Set once this evaluator has started, win or lose the race with the caller.</summary>
        public ManualResetEventSlim Started { get; } = new();

        public Task<ReplEvaluation> EvaluateAsync(string text, bool isExpression, CancellationToken ct = default)
        {
            ThreadId = Environment.CurrentManagedThreadId;
            Asked.Add((text, isExpression));
            Started.Set();
            Gate?.Wait(TimeSpan.FromSeconds(10), ct);

            return Task.FromResult(Answer);
        }
    }

    private sealed class RecordingEvaluator : IImmediateEvaluator
    {
        public int Calls { get; private set; }
        public ImmediateInput? Last { get; private set; }
        public int ThreadId { get; private set; }
        public ManualResetEventSlim? Gate { get; init; }
        public ManualResetEventSlim Started { get; } = new();

        public Task<ImmediateOutcome> EvaluateAsync(ImmediateInput input, CancellationToken ct = default)
        {
            Calls++;
            Last = input;
            ThreadId = Environment.CurrentManagedThreadId;
            Started.Set();
            Gate?.Wait(TimeSpan.FromSeconds(10), ct);

            return Task.FromResult(new ImmediateOutcome(ImmediateOutcomeKind.Output, "ok"));
        }
    }
}
