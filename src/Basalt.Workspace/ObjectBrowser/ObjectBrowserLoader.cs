namespace Basalt.Workspace.ObjectBrowser;

/// <summary>
/// Runs the browser's questions so that only the newest answer of each kind
/// is ever used.
///
/// The panel asks again whenever the selection moves: arrowing down the type
/// list asks for the members of every type it passes. An old, slow answer
/// finishing after a newer one must neither overwrite it nor keep the compiler
/// busy, so each request cancels the previous one of its channel and a request
/// that was superseded reports <c>null</c> instead of a result.
///
/// Awaiting continues on the caller's context, which is the interface thread
/// in the IDE: the work itself runs inside the service on a pool thread, the
/// result is applied where the controls live.
/// </summary>
public sealed class ObjectBrowserLoader : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _current = [];
    private bool _disposed;

    public ObjectBrowserLoader(ObjectBrowserService service) => Service = service;

    public ObjectBrowserService Service { get; }

    /// <summary>
    /// Runs <paramref name="work"/> as the current request of a channel and
    /// returns its result, or null when a newer request of the same channel
    /// (or <see cref="CancelAll"/>) replaced it first.
    /// </summary>
    public async Task<T?> LatestAsync<T>(
        string channel, Func<ObjectBrowserService, CancellationToken, Task<T>> work)
        where T : class
    {
        CancellationTokenSource mine;

        lock (_gate)
        {
            if (_disposed) return null;

            if (_current.TryGetValue(channel, out var previous)) previous.Cancel();

            mine = new CancellationTokenSource();
            _current[channel] = mine;
        }

        try
        {
            var result = await work(Service, mine.Token);

            lock (_gate)
            {
                // Cancellation is cooperative: a request already past its last
                // check still returns a value, which must not be applied.
                return mine.IsCancellationRequested || !ReferenceEquals(_current.GetValueOrDefault(channel), mine)
                    ? null
                    : result;
            }
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current.GetValueOrDefault(channel), mine)) _current.Remove(channel);
            }
        }
    }

    /// <summary>
    /// Drops every request in flight except those of the given channels. Choosing
    /// another library drops what was asked about the old one, but must not
    /// drop the list of libraries itself when a solution reload is under way.
    /// </summary>
    public void CancelAllExcept(params string[] keepChannels)
    {
        lock (_gate)
        {
            foreach (var (channel, source) in _current.ToList())
            {
                if (keepChannels.Contains(channel)) continue;

                source.Cancel();
                _current.Remove(channel);
            }
        }
    }

    /// <summary>Drops the request in flight of one channel, if any.</summary>
    public void Cancel(string channel)
    {
        lock (_gate)
        {
            if (!_current.Remove(channel, out var source)) return;

            source.Cancel();
        }
    }

    /// <summary>Drops every request in flight, as when the libraries are read again.</summary>
    public void CancelAll()
    {
        lock (_gate)
        {
            foreach (var source in _current.Values) source.Cancel();
            _current.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate) _disposed = true;
        CancelAll();
    }
}
