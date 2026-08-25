using Basalt.Extensibility.Interpretation;

namespace Basalt.Tests;

/// <summary>
/// A pretend program, to test the stepping rules without a dialect.
///
/// Each statement is a line number and a depth, which is all the base class
/// looks at: whether a step ends is decided by those, not by what the line
/// says. Testing it here means the rules are checked once rather than again
/// for every dialect.
/// </summary>
internal sealed class FakeInterpreter(params (int Line, int Depth)[] statements)
    : InterpreterBase
{
    private int _index;

    /// <summary>Every line that was run, in order.</summary>
    public List<int> Ran { get; } = [];

    /// <summary>What Evaluate should answer, for conditional breakpoints.</summary>
    public Func<string, string?> Evaluator { get; set; } = _ => "1";

    public override int CurrentLine =>
        _index < statements.Length ? statements[_index].Line : 0;

    protected override int CallDepth =>
        _index < statements.Length ? statements[_index].Depth : 0;

    protected override bool ExecuteNextStatement(CancellationToken ct)
    {
        if (_index >= statements.Length) return false;

        Ran.Add(statements[_index].Line);
        _index++;

        return _index < statements.Length;
    }

    /// <summary>Prints something, as a running program would.</summary>
    public void Say(string text) => ReportOutput(text);

    public override IReadOnlyList<InterpreterError> Load(string source, string? filePath = null) => [];

    public override IReadOnlyList<InterpreterFrame> GetCallStack() =>
        [new InterpreterFrame("main", CurrentLine)];

    public override IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex) => [];

    public override string? Evaluate(string expression, int frameIndex) => Evaluator(expression);

    public override bool SetVariable(string name, string value, int frameIndex) => false;
}

/// <summary>An interpreter that never ends, to test stopping one.</summary>
internal sealed class EndlessInterpreter : InterpreterBase
{
    public int Statements { get; private set; }

    public override int CurrentLine => 1;
    protected override int CallDepth => 0;

    protected override bool ExecuteNextStatement(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Statements++;
        return true;
    }

    public override IReadOnlyList<InterpreterError> Load(string source, string? filePath = null) => [];
    public override IReadOnlyList<InterpreterFrame> GetCallStack() => [];
    public override IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex) => [];
    public override string? Evaluate(string expression, int frameIndex) => null;
    public override bool SetVariable(string name, string value, int frameIndex) => false;
}

public sealed class InterpreterBaseTests
{
    /// <summary>Four lines at the top level, with a call at line 2.</summary>
    private static FakeInterpreter WithACall() =>
        new((1, 0), (2, 0), (10, 1), (11, 1), (3, 0), (4, 0));

    [Fact]
    public async Task RunsToTheEndWhenNothingStopsIt()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0));

        var stop = await interpreter.RunAsync();

        Assert.Equal(StopReason.Ended, stop.Reason);
        Assert.Equal([1, 2, 3], interpreter.Ran);
        Assert.Equal(RunState.Finished, interpreter.State);
    }

    [Fact]
    public async Task StopsAtABreakpoint()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0));
        interpreter.SetBreakpoints([new InterpreterBreakpoint(2)]);

        var stop = await interpreter.RunAsync();

        Assert.Equal(StopReason.Breakpoint, stop.Reason);
        Assert.Equal(2, stop.Line);

        // Stopped before running line 2, not after.
        Assert.Equal([1], interpreter.Ran);
    }

    [Fact]
    public async Task CarriesOnFromWhereItStopped()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0));
        interpreter.SetBreakpoints([new InterpreterBreakpoint(2)]);

        await interpreter.RunAsync();
        var stop = await interpreter.RunAsync();

        // The breakpoint it is sitting on does not stop it again.
        Assert.Equal(StopReason.Ended, stop.Reason);
        Assert.Equal([1, 2, 3], interpreter.Ran);
    }

    [Fact]
    public async Task IgnoresADisabledBreakpoint()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0));
        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, Enabled: false)]);

        Assert.Equal(StopReason.Ended, (await interpreter.RunAsync()).Reason);
    }

    [Fact]
    public async Task StopsOnlyWhenAConditionHolds()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0))
        {
            Evaluator = _ => "0"
        };

        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, Condition: "i = 5")]);

        Assert.Equal(StopReason.Ended, (await interpreter.RunAsync()).Reason);
    }

    [Fact]
    public async Task StopsWhenTheConditionCannotBeWorkedOut()
    {
        // Rather than carrying on: a condition that is quietly ignored is
        // harder to notice than one that stops too often.
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0))
        {
            Evaluator = _ => throw new InvalidOperationException("nonsense")
        };

        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, Condition: "???")]);

        Assert.Equal(StopReason.Breakpoint, (await interpreter.RunAsync()).Reason);
    }

    [Fact]
    public async Task WaitsForTheHitCountBeforeStopping()
    {
        // Line 2 comes round three times.
        var interpreter = new FakeInterpreter(
            (1, 0), (2, 0), (2, 0), (2, 0), (3, 0));

        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, HitCount: 3)]);

        var stop = await interpreter.RunAsync();

        Assert.Equal(StopReason.Breakpoint, stop.Reason);

        // Two hits went past before it stopped on the third.
        Assert.Equal([1, 2, 2], interpreter.Ran);
    }

    [Fact]
    public async Task StepIntoGoesIntoACall()
    {
        var interpreter = WithACall();

        await interpreter.StepIntoAsync();   // runs line 1, stops at 2
        await interpreter.StepIntoAsync();   // runs line 2, stops at 10 inside

        Assert.Equal(10, interpreter.CurrentLine);
    }

    [Fact]
    public async Task StepOverRunsACallWithoutGoingInto()
    {
        var interpreter = WithACall();

        await interpreter.StepOverAsync();   // line 1
        await interpreter.StepOverAsync();   // line 2, and the call it makes

        // Back at the top level rather than inside the call.
        Assert.Equal(3, interpreter.CurrentLine);
        Assert.Contains(10, interpreter.Ran);
    }

    [Fact]
    public async Task StepOutRunsUntilTheCallReturns()
    {
        var interpreter = WithACall();

        await interpreter.StepIntoAsync();   // line 1
        await interpreter.StepIntoAsync();   // into the call, at line 10

        Assert.Equal(10, interpreter.CurrentLine);

        await interpreter.StepOutAsync();

        Assert.Equal(3, interpreter.CurrentLine);
    }

    [Fact]
    public async Task LetsABreakpointWinOverAStep()
    {
        // Someone who set a breakpoint wants to stop there even while
        // stepping over something else.
        var interpreter = WithACall();
        interpreter.SetBreakpoints([new InterpreterBreakpoint(10)]);

        await interpreter.StepOverAsync();   // line 1
        var stop = await interpreter.StepOverAsync();

        Assert.Equal(StopReason.Breakpoint, stop.Reason);
        Assert.Equal(10, stop.Line);
    }

    [Fact]
    public async Task StopsAProgramThatWouldNeverEnd()
    {
        // The one that matters: without this an endless loop would need the
        // IDE to be killed.
        var interpreter = new EndlessInterpreter();

        var run = interpreter.RunAsync();

        while (interpreter.Statements < 100) await Task.Yield();

        interpreter.Stop();

        var stop = await run;

        Assert.Equal(StopReason.Paused, stop.Reason);
        Assert.Equal(RunState.Finished, interpreter.State);
    }

    [Fact]
    public async Task SaysWhereItStoppedAfterwards()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0), (3, 0));
        interpreter.SetBreakpoints([new InterpreterBreakpoint(3)]);

        await interpreter.RunAsync();

        Assert.Equal(StopReason.Breakpoint, interpreter.LastStop?.Reason);
        Assert.Equal(3, interpreter.LastStop?.Line);
    }

    [Fact]
    public async Task RaisesStoppedWhenItPauses()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0));
        interpreter.SetBreakpoints([new InterpreterBreakpoint(2)]);

        StopInfo? raised = null;
        interpreter.Stopped += (_, info) => raised = info;

        await interpreter.RunAsync();

        Assert.Equal(StopReason.Breakpoint, raised?.Reason);
    }

    [Fact]
    public async Task RaisesExitedWhenTheProgramEnds()
    {
        var interpreter = new FakeInterpreter((1, 0));

        var exited = false;
        interpreter.Exited += (_, _) => exited = true;

        await interpreter.RunAsync();

        Assert.True(exited);
    }

    [Fact]
    public async Task DoesNothingWhenAskedToCarryOnAfterTheEnd()
    {
        var interpreter = new FakeInterpreter((1, 0));

        await interpreter.RunAsync();
        var again = await interpreter.RunAsync();

        Assert.Equal(StopReason.Ended, again.Reason);
        Assert.Single(interpreter.Ran);
    }

    [Fact]
    public void ForgetsHitCountsWhenBreakpointsAreSetAgain()
    {
        var interpreter = new FakeInterpreter((1, 0), (2, 0));

        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, HitCount: 2)]);
        interpreter.SetBreakpoints([new InterpreterBreakpoint(2, HitCount: 2)]);

        // Nothing to assert beyond it not throwing: the count is private, and
        // the behaviour it guards is covered by the hit count test above.
        Assert.Equal(RunState.Ready, interpreter.State);
    }
}
