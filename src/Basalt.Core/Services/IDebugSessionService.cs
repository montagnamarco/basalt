using Basalt.Core.Localization;

namespace Basalt.Core.Services;

/// <summary>
/// The exceptions a session stops on, besides the ones that end the program unhandled.
/// </summary>
/// <param name="WhenThrown">Stop at the throw of any exception, caught or not.</param>
/// <param name="WhenUserUnhandled">Stop at an exception the program's own code does not handle.</param>
public readonly record struct ExceptionBreaks(bool WhenThrown, bool WhenUserUnhandled);

/// <summary>
/// How a program is started for debugging beyond where it is: the arguments and the environment its launch
/// profile gives it.
/// </summary>
/// <param name="WorkingDirectory">Where the program starts, or null for the debugger's choice (the assembly's folder).</param>
/// <param name="Arguments">The program's own arguments, already split.</param>
/// <param name="Environment">Variables added to the environment the program inherits.</param>
public sealed record LaunchOptions(
    string? WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment)
{
    /// <summary>Starting with nothing but a working directory.</summary>
    public static LaunchOptions In(string? workingDirectory) =>
        new(workingDirectory, [], new Dictionary<string, string>());
}

/// <summary>
/// What a debugging session needs of whatever is doing the debugging.
///
/// IDebugService says what debugging is; this adds the few things a session
/// needs on top — whether it is still running, what the program printed, and
/// when something went wrong — so that the session can hold either the .NET
/// debugger or an interpreter without knowing which.
/// </summary>
public interface IDebugSessionService : IDebugService, IDisposable
{
    /// <summary>Whether a program is still being debugged.</summary>
    bool IsRunning { get; }

    /// <summary>Whether the program is stopped somewhere.</summary>
    bool IsPaused { get; }

    /// <summary>
    /// The exception the program is stopped on, as its type and message, or null when the stop is not one.
    /// Read when <see cref="IDebugService.Paused"/> is raised.
    /// </summary>
    string? StoppedException => null;

    /// <summary>
    /// Whether the stop on an exception is one for an exception that was thrown, as opposed to one that nothing
    /// handled. Meaningful only while <see cref="StoppedException"/> is not null.
    /// </summary>
    bool StoppedOnThrow => false;

    /// <summary>
    /// Chooses which exceptions stop the program. Kept by the service and sent to the debugger when it is
    /// configured, and again at once when changed during a session. A debugger that cannot do this ignores it.
    /// </summary>
    Task SetExceptionBreaksAsync(ExceptionBreaks breaks, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>
    /// Whether ending the session leaves the program running: true for one the debugger attached to and was
    /// told to let go of, false for one it launched, which is ended with the session.
    /// </summary>
    bool DetachesOnStop => false;

    /// <summary>
    /// Launches a program with the arguments and environment of its launch profile. A debugger that cannot pass
    /// them (the interpreter runs a source file) starts it as <see cref="IDebugService.LaunchAsync"/> does.
    /// </summary>
    Task LaunchAsync(string assemblyPath, LaunchOptions options, CancellationToken ct = default) =>
        LaunchAsync(assemblyPath, options.WorkingDirectory, ct);

    /// <summary>What the program printed.</summary>
    event EventHandler<string>? OutputReceived;

    /// <summary>
    /// The program is up and running.
    ///
    /// A program that never ends by itself — a window, a web host — reports
    /// nothing else once it starts, so without this the interface cannot
    /// tell it apart from one that is still being launched.
    /// </summary>
    event EventHandler? Started;

    /// <summary>Something went wrong that the user should be told about.</summary>
    event EventHandler<Exception>? Faulted;

    /// <summary>
    /// The children of a variable that has them.
    ///
    /// Empty where the debugger cannot expand one, which is the honest answer
    /// for an interpreter whose values are all plain.
    /// </summary>
    Task<IReadOnlyList<VariableValue>> ExpandAsync(
        int reference, CancellationToken ct = default);

    /// <summary>
    /// Evaluates a line typed in the Immediate window in the given frame.
    ///
    /// <paramref name="isExpression"/> says the line was asked for its value
    /// ("?x") rather than run for its effect ("x = 3").
    ///
    /// The default asks for the value the way a watch does, which is all an
    /// interpreter can offer; the .NET debugger overrides it to send the
    /// "repl" context, where assignments and calls are allowed.
    /// </summary>
    async Task<ReplEvaluation> EvaluateReplAsync(
        string text, bool isExpression, int frameIndex, CancellationToken ct = default)
    {
        // An interpreter evaluates values; running a statement as one would
        // read "x = 3" as a comparison and answer False without assigning.
        if (!isExpression)
            return new ReplEvaluation(false, Localizer.Get(StringKeys.ImmediateNoStatements));

        var value = await EvaluateAsync(text, frameIndex, ct).ConfigureAwait(false);

        return value is null
            ? new ReplEvaluation(false, "")
            : new ReplEvaluation(true, value);
    }
}
