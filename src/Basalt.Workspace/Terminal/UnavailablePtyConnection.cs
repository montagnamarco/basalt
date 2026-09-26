namespace Basalt.Workspace.Terminal;

/// <summary>
/// The stand-in for a terminal that could not start.
/// </summary>
/// <remarks>
/// ConPTY cannot attach a process in every session — a disconnected remote
/// session has no desktop for the console to join — and a missing libc or a
/// shell that will not run fails the same way elsewhere. The panel shows why
/// instead of taking the IDE down with an exception from its constructor.
/// </remarks>
public sealed class UnavailablePtyConnection(string reason) : IPtyConnection
{
    /// <summary>Why the terminal could not start.</summary>
    public string Reason { get; } = reason;

    public bool IsRunning => false;

    public event EventHandler<string>? OutputReceived { add { } remove { } }

    public event EventHandler<int>? Exited { add { } remove { } }

    public void Write(string text) { }

    public void Resize(int columns, int rows) { }

    public void Interrupt() { }

    public void Dispose() { }
}
