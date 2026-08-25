namespace Basalt.Extensibility.Interpretation;

/// <summary>What an interpreter is doing.</summary>
public enum RunState
{
    /// <summary>Loaded, not started.</summary>
    Ready,

    /// <summary>Running, and will keep going until something stops it.</summary>
    Running,

    /// <summary>Stopped somewhere, waiting to be told to go on.</summary>
    Paused,

    /// <summary>Finished, whether by ending or by being stopped.</summary>
    Finished,

    /// <summary>Stopped by an error it could not carry on from.</summary>
    Failed
}

/// <summary>Why the interpreter stopped.</summary>
public enum StopReason
{
    None,
    Breakpoint,
    Step,
    Paused,
    Error,
    Ended
}

/// <summary>One entry of the call stack.</summary>
/// <param name="Name">The procedure, or the name given to the top level.</param>
/// <param name="Line">The line being run, counted from one.</param>
public sealed record InterpreterFrame(string Name, int Line);

/// <summary>A variable as the debugger shows it.</summary>
/// <param name="HasChildren">Whether it holds things worth expanding, like an array.</param>
public sealed record InterpreterVariable(
    string Name, string Value, string Type, bool HasChildren = false);

/// <summary>Where the interpreter stopped, and why.</summary>
public sealed record StopInfo(
    StopReason Reason,
    int Line,
    string? Message = null);

/// <summary>
/// A program being run one step at a time.
///
/// The interface is deliberately about a dialect in general rather than about
/// QuickBASIC: the IDE is meant for Basic and its dialects, so the second
/// interpreter should need no change here. What is specific to a dialect —
/// how a program is parsed, what an expression means, which library functions
/// exist — belongs to the implementation.
///
/// Nothing here runs a program to completion on its own. The IDE decides when
/// to take the next step, which is what makes stepping through it possible
/// rather than watching it go past.
/// </summary>
public interface IInterpreter
{
    /// <summary>What it is doing now.</summary>
    RunState State { get; }

    /// <summary>Where it stopped, when it is paused.</summary>
    StopInfo? LastStop { get; }

    /// <summary>The line about to be run, counted from one.</summary>
    int CurrentLine { get; }

    /// <summary>
    /// Reads a program, ready to be run.
    ///
    /// What comes back are the problems that stop it running at all; an empty
    /// list means it is ready.
    /// </summary>
    IReadOnlyList<InterpreterError> Load(string source, string? filePath = null);

    /// <summary>Runs until a breakpoint, the end, or a stop.</summary>
    Task<StopInfo> RunAsync(CancellationToken ct = default);

    /// <summary>Runs the next line, going into a call rather than over it.</summary>
    Task<StopInfo> StepIntoAsync(CancellationToken ct = default);

    /// <summary>Runs the next line, treating a call as one step.</summary>
    Task<StopInfo> StepOverAsync(CancellationToken ct = default);

    /// <summary>Runs on until the current procedure returns.</summary>
    Task<StopInfo> StepOutAsync(CancellationToken ct = default);

    /// <summary>
    /// Stops a running program.
    ///
    /// Has to work while it is running, not only while paused: a program with
    /// an endless loop is exactly the one that needs stopping.
    /// </summary>
    void Stop();

    /// <summary>Sets every breakpoint, replacing the ones set before.</summary>
    void SetBreakpoints(IReadOnlyList<InterpreterBreakpoint> breakpoints);

    /// <summary>The call stack, innermost first.</summary>
    IReadOnlyList<InterpreterFrame> GetCallStack();

    /// <summary>The variables a frame can see.</summary>
    IReadOnlyList<InterpreterVariable> GetVariables(int frameIndex);

    /// <summary>
    /// Works out what an expression comes to, in the language being run.
    ///
    /// In the dialect's own syntax, not in C#: a user debugging QuickBASIC
    /// writes a QuickBASIC condition.
    /// </summary>
    string? Evaluate(string expression, int frameIndex);

    /// <summary>
    /// Changes a variable while the program is stopped.
    ///
    /// Returns false when there is no such variable, or the value does not
    /// fit it.
    /// </summary>
    bool SetVariable(string name, string value, int frameIndex);

    /// <summary>Something the program printed.</summary>
    event EventHandler<string>? Output;

    /// <summary>
    /// The program is asking for input.
    ///
    /// Asked of the IDE rather than read from a console: the program has no
    /// terminal of its own, and answering through the IDE is what lets it be
    /// debugged.
    /// </summary>
    event EventHandler<InputRequest>? InputRequested;

    /// <summary>The program stopped.</summary>
    event EventHandler<StopInfo>? Stopped;

    /// <summary>The program finished.</summary>
    event EventHandler<int>? Exited;
}

/// <summary>A place to stop, with the condition that decides whether to.</summary>
/// <param name="Condition">
/// An expression in the language being run; the interpreter decides what it
/// means. Null stops every time.
/// </param>
/// <param name="HitCount">Stops from the nth hit onwards, when given.</param>
public sealed record InterpreterBreakpoint(
    int Line,
    bool Enabled = true,
    string? Condition = null,
    int? HitCount = null);

/// <summary>Something wrong with the program, found while reading or running it.</summary>
public sealed record InterpreterError(string Message, int Line, int Column = 0)
{
    public override string ToString() => $"({Line},{Column}): {Message}";
}

/// <summary>
/// The program waiting for something to be typed.
///
/// The answer is set on the request rather than returned, so an interpreter
/// that asks from inside a running statement can carry on where it left off.
/// </summary>
public sealed class InputRequest(string prompt)
{
    public string Prompt { get; } = prompt;

    /// <summary>What was typed. Left null when nothing was.</summary>
    public string? Response { get; set; }
}
