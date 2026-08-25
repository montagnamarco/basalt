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
}
