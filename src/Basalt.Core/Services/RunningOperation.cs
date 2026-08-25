namespace Basalt.Core.Services;

/// <summary>
/// A long-running piece of work the user should be able to see and stop.
///
/// A build or a solution load can take long enough that the only thing worse
/// than waiting is not being able to stop waiting. The token is the operation
/// itself; the description is what the status bar shows.
/// </summary>
public sealed class RunningOperation : IDisposable
{
    private readonly CancellationTokenSource _source = new();

    private bool _disposed;

    public RunningOperation(string description) => Description = description;

    /// <summary>What is happening, as the status bar says it.</summary>
    public string Description { get; }

    /// <summary>Cancelled when the user asks to stop.</summary>
    public CancellationToken Token => _source.Token;

    /// <summary>Whether stopping has been asked for.</summary>
    public bool IsCancelled => _source.IsCancellationRequested;

    /// <summary>Asks the operation to stop.</summary>
    public void Cancel()
    {
        if (_disposed) return;

        _source.Cancel();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _source.Dispose();
    }
}

/// <summary>
/// The operations in progress.
///
/// One at a time is what the status bar can show, but several can run, so the
/// most recent is the one offered for cancelling: it is the one the user just
/// started and is waiting on.
/// </summary>
public sealed class OperationTracker
{
    private readonly List<RunningOperation> _running = [];
    private readonly Lock _gate = new();

    /// <summary>Raised when something starts or finishes.</summary>
    public event EventHandler? Changed;

    /// <summary>The one to show, or null when nothing is running.</summary>
    public RunningOperation? Current
    {
        get
        {
            lock (_gate) return _running.Count > 0 ? _running[^1] : null;
        }
    }

    /// <summary>How many are running.</summary>
    public int Count
    {
        get
        {
            lock (_gate) return _running.Count;
        }
    }

    /// <summary>
    /// Starts one, which finishes when it is disposed.
    ///
    /// Disposed rather than stopped explicitly, so an operation that throws
    /// still leaves the status bar clean.
    /// </summary>
    public RunningOperation Start(string description)
    {
        var operation = new RunningOperation(description);

        lock (_gate) _running.Add(operation);

        Changed?.Invoke(this, EventArgs.Empty);

        return operation;
    }

    /// <summary>Marks one as finished.</summary>
    public void Finish(RunningOperation operation)
    {
        lock (_gate) _running.Remove(operation);

        operation.Dispose();

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Asks the one being shown to stop.</summary>
    public void CancelCurrent() => Current?.Cancel();
}
