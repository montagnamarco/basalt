namespace Basalt.Extensibility.Interpretation;

/// <summary>
/// What every dialect's interpreter does the same way.
///
/// Stepping, breakpoints and stopping are not about any one dialect: whether
/// a step ends here depends on the call depth and the line, not on what the
/// line says. A dialect that derives from this writes how a statement runs
/// and leaves the rest alone.
///
/// The shape is inverted from the usual one: rather than the interpreter
/// running the program and calling back, the IDE asks for one step at a time
/// and this decides whether that step is where it should stop.
/// </summary>
public abstract class InterpreterBase : IInterpreter
{
    private readonly Dictionary<int, InterpreterBreakpoint> _breakpoints = [];

    /// <summary>How many times each breakpoint line has been reached.</summary>
    private readonly Dictionary<int, int> _hits = [];

    private CancellationTokenSource? _running;

    /// <summary>
    /// What the current step is waiting for.
    ///
    /// Null while running freely.
    /// </summary>
    private StepRequest? _step;

    public RunState State { get; protected set; } = RunState.Ready;

    public StopInfo? LastStop { get; protected set; }

    public abstract int CurrentLine { get; }

    public event EventHandler<string>? Output;
    public event EventHandler<InputRequest>? InputRequested;
    public event EventHandler<StopInfo>? Stopped;
    public event EventHandler<int>? Exited;

    public abstract IReadOnlyList<InterpreterError> Load(string source, string? filePath = null);
    public abstract IReadOnlyList<InterpreterFrame> GetCallStack();
    public abstract IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex);
    public abstract string? Evaluate(string expression, int frameIndex);
    public abstract bool SetVariable(string name, string value, int frameIndex);

    /// <summary>
    /// Runs the next statement.
    ///
    /// The one thing a dialect has to write. Returns false when the program
    /// has nothing left to run.
    /// </summary>
    protected abstract bool ExecuteNextStatement(CancellationToken ct);

    /// <summary>How deep the call stack is, for deciding what a step means.</summary>
    protected abstract int CallDepth { get; }

    /// <summary>
    /// Forgets that the program ever stopped.
    ///
    /// Called when a program is loaded, so a breakpoint on the first line of
    /// the next run fires as it should.
    /// </summary>
    protected void ResetStops()
    {
        _hasStopped = false;
        _hits.Clear();
    }

    public void SetBreakpoints(IReadOnlyList<InterpreterBreakpoint> breakpoints)
    {
        _breakpoints.Clear();

        foreach (var breakpoint in breakpoints)
            _breakpoints[breakpoint.Line] = breakpoint;

        // Hit counts belong to the breakpoints that were replaced, so a
        // breakpoint set again starts counting afresh.
        _hits.Clear();
    }

    public Task<StopInfo> RunAsync(CancellationToken ct = default) =>
        StepUntilAsync(null, ct);

    public Task<StopInfo> StepIntoAsync(CancellationToken ct = default) =>
        StepUntilAsync(new StepRequest(StepKind.Into, CallDepth, CurrentLine), ct);

    public Task<StopInfo> StepOverAsync(CancellationToken ct = default) =>
        StepUntilAsync(new StepRequest(StepKind.Over, CallDepth, CurrentLine), ct);

    public Task<StopInfo> StepOutAsync(CancellationToken ct = default) =>
        StepUntilAsync(new StepRequest(StepKind.Out, CallDepth, CurrentLine), ct);

    /// <summary>
    /// Whether the program has stopped somewhere already.
    ///
    /// Tells resuming apart from starting: only a resume has a line it is
    /// sitting on and should step past.
    /// </summary>
    private bool _hasStopped;

    /// <summary>
    /// Whether Stop was asked for.
    ///
    /// Kept apart from State because the run loop sets State when it returns,
    /// which would otherwise overwrite the stop with Paused.
    /// </summary>
    private bool _stopRequested;

    public void Stop()
    {
        // Cancelling rather than setting a flag alone: the loop below checks
        // the token, so a program in an endless loop stops at its next
        // statement rather than when it happens to finish.
        _stopRequested = true;

        _running?.Cancel();

        if (State is RunState.Running or RunState.Paused)
            State = RunState.Finished;
    }

    /// <summary>
    /// Runs statements until the step is satisfied or something stops it.
    ///
    /// On a background thread: a program that runs for a second would freeze
    /// the interface if it ran on the one drawing it.
    /// </summary>
    private async Task<StopInfo> StepUntilAsync(StepRequest? step, CancellationToken ct)
    {
        if (State is RunState.Finished or RunState.Failed)
            return new StopInfo(StopReason.Ended, CurrentLine);

        _stopRequested = false;
        _step = step;

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _running = linked;

        State = RunState.Running;

        var stop = await Task.Run(() => RunLoop(linked.Token), CancellationToken.None)
            .ConfigureAwait(false);

        _running = null;
        LastStop = stop;

        // A stop that was asked for stays finished: the program was ended, not
        // paused partway, and something waiting to resume it would hang.
        State = _stopRequested
            ? RunState.Finished
            : stop.Reason switch
            {
                StopReason.Ended => RunState.Finished,
                StopReason.Error => RunState.Failed,
                _ => RunState.Paused
            };

        if (State == RunState.Paused) Stopped?.Invoke(this, stop);

        if (stop.Reason == StopReason.Ended) Exited?.Invoke(this, 0);

        return stop;
    }

    /// <summary>The loop itself, one statement at a time.</summary>
    private StopInfo RunLoop(CancellationToken ct)
    {
        // A line already stopped at does not stop the program again, or
        // carrying on would never get past it. On the first run nothing has
        // stopped yet, so a breakpoint on the first line does fire.
        var first = _hasStopped;

        _hasStopped = true;

        while (true)
        {
            if (ct.IsCancellationRequested)
                return new StopInfo(StopReason.Paused, CurrentLine, "Stopped.");

            if (!first && ShouldStopAt(CurrentLine, out var reason))
                return new StopInfo(reason, CurrentLine);

            first = false;

            bool more;

            try
            {
                more = ExecuteNextStatement(ct);
            }
            catch (OperationCanceledException)
            {
                return new StopInfo(StopReason.Paused, CurrentLine, "Stopped.");
            }
            catch (InterpreterRuntimeException ex)
            {
                return new StopInfo(StopReason.Error, ex.Line, ex.Message);
            }

            if (!more) return new StopInfo(StopReason.Ended, CurrentLine);
        }
    }

    /// <summary>
    /// Whether the program should stop before running this line.
    ///
    /// A breakpoint wins over a step: someone who set one wants to stop there
    /// even if they asked to step over something else.
    /// </summary>
    private bool ShouldStopAt(int line, out StopReason reason)
    {
        if (BreakpointStopsHere(line))
        {
            reason = StopReason.Breakpoint;
            return true;
        }

        if (_step is { } step && StepEndsHere(step, line))
        {
            reason = StopReason.Step;
            return true;
        }

        reason = StopReason.None;
        return false;
    }

    /// <summary>Whether a breakpoint on this line stops the program.</summary>
    private bool BreakpointStopsHere(int line)
    {
        if (!_breakpoints.TryGetValue(line, out var breakpoint)) return false;
        if (!breakpoint.Enabled) return false;

        // The condition is asked of the dialect, in its own syntax.
        if (breakpoint.Condition is { Length: > 0 } condition && !ConditionHolds(condition))
            return false;

        // Counted here rather than by the dialect: a hit count means the same
        // thing whatever the language, and only a hit that passed the
        // condition counts as one.
        _hits[line] = _hits.GetValueOrDefault(line) + 1;

        return breakpoint.HitCount is not { } needed || _hits[line] >= needed;
    }

    /// <summary>
    /// Whether a breakpoint condition holds right now.
    ///
    /// A condition that cannot be worked out stops the program rather than
    /// being ignored: silently not stopping is the harder failure to notice.
    /// </summary>
    protected virtual bool ConditionHolds(string condition)
    {
        try
        {
            var result = Evaluate(condition, 0);

            return result is null || IsTrue(result);
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Whether an evaluated condition counts as true.</summary>
    protected virtual bool IsTrue(string value) =>
        !string.Equals(value, "0", StringComparison.Ordinal)
        && !string.Equals(value, "False", StringComparison.OrdinalIgnoreCase)
        && value.Length > 0;

    /// <summary>
    /// Whether a step has arrived where it was going.
    ///
    /// Into stops at the next line whatever the depth. Over stops at the next
    /// line at the same depth or shallower, so a call runs through. Out stops
    /// only once the stack is shallower than it was.
    /// </summary>
    private bool StepEndsHere(StepRequest step, int line) => step.Kind switch
    {
        StepKind.Into => line != step.FromLine || CallDepth != step.FromDepth,
        StepKind.Over => CallDepth <= step.FromDepth
                      && (line != step.FromLine || CallDepth < step.FromDepth),
        StepKind.Out => CallDepth < step.FromDepth,
        _ => false
    };

    /// <summary>Passes on something the program printed.</summary>
    protected void ReportOutput(string text) => Output?.Invoke(this, text);

    /// <summary>
    /// Asks the IDE for a line of input.
    ///
    /// An empty string when nobody answers, so a program waiting for input
    /// under a test does not hang.
    /// </summary>
    protected string RequestInput(string prompt)
    {
        var request = new InputRequest(prompt);

        InputRequested?.Invoke(this, request);

        return request.Response ?? "";
    }

    private enum StepKind { Into, Over, Out }

    private sealed record StepRequest(StepKind Kind, int FromDepth, int FromLine);
}

/// <summary>Something that went wrong while running, with the line it happened on.</summary>
public sealed class InterpreterRuntimeException(string message, int line)
    : Exception(message)
{
    public int Line { get; } = line;
}
