namespace Basalt.Workspace.Testing;

/// <summary>
/// One discovery of a solution's tests, with the token that ends it.
/// </summary>
public readonly record struct DiscoveryRun(long Generation, CancellationToken Token);

/// <summary>
/// Keeps track of the discovery whose result is still wanted.
/// </summary>
/// <remarks>
/// Each build of a test project asks for a discovery, and so does Refresh. A
/// discovery runs "dotnet test --list-tests" for every test project, so an
/// older one still running is wasted work and its result is stale. Starting a
/// newer one cancels the older; closing the solution cancels the one in
/// progress and makes it stale, so its result is never shown in the next
/// solution's window.
/// </remarks>
public sealed class LatestDiscovery
{
    private readonly object _lock = new();
    private CancellationTokenSource _source = new();
    private long _generation;

    /// <summary>Starts a discovery, cancelling the one that was in progress.</summary>
    public DiscoveryRun Begin()
    {
        lock (_lock)
        {
            _source.Cancel();
            _source = new CancellationTokenSource();

            return new DiscoveryRun(++_generation, _source.Token);
        }
    }

    /// <summary>Ends the discovery in progress and makes every earlier one stale.</summary>
    public void Cancel()
    {
        lock (_lock)
        {
            _source.Cancel();
            _source = new CancellationTokenSource();
            _generation++;
        }
    }

    /// <summary>Whether no newer discovery has started or been cancelled since this one.</summary>
    public bool IsCurrent(DiscoveryRun run)
    {
        lock (_lock) return run.Generation == _generation;
    }
}
