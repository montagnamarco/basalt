namespace Basalt.Workspace;

/// <summary>
/// The one gate every dotnet build of the startup project passes through:
/// the toolbar's build, a run, a debug start, a publish, a test run and the
/// Immediate window's build. Two of them over one obj folder corrupt each
/// other, so one goes at a time and the others wait.
///
/// It also remembers who holds it, so that a request that has to wait can
/// say what it is waiting for.
/// </summary>
public sealed class BuildGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _lock = new();
    private string? _holder;

    /// <summary>Who has the gate (as a phrase to show the user), or null when free.</summary>
    public string? Holder
    {
        get { lock (_lock) return _holder; }
    }

    /// <summary>Whether somebody holds the gate.</summary>
    public bool IsHeld => _semaphore.CurrentCount == 0;

    /// <summary>For the tests: 1 when free, 0 when held.</summary>
    internal int CurrentCount => _semaphore.CurrentCount;

    /// <summary>
    /// Waits for the gate and returns the handle that gives it back.
    /// </summary>
    /// <param name="holder">What to call the new holder in other requests' waiting messages.</param>
    /// <param name="onWaiting">Called, with the current holder, only if the gate is taken and this has to wait.</param>
    public async Task<IDisposable> AcquireAsync(
        string holder, Action<string>? onWaiting = null, CancellationToken ct = default)
    {
        if (!_semaphore.Wait(0))
        {
            onWaiting?.Invoke(Holder ?? holder);
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        }

        lock (_lock) _holder = holder;

        return new Releaser(this);
    }

    private void Release()
    {
        lock (_lock) _holder = null;
        _semaphore.Release();
    }

    private sealed class Releaser(BuildGate gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
