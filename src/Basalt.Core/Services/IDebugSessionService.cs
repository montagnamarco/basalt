using Basalt.Core.Localization;

namespace Basalt.Core.Services;

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
