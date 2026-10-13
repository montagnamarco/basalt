namespace Basalt.Data.Sessions;

/// <summary>
/// Removes an event handler when disposed, so a batch's message listener lasts
/// only as long as the batch.
/// </summary>
internal sealed class EventSubscription(Action remove) : IDisposable
{
    private Action? _remove = remove;

    public void Dispose()
    {
        // Exchanged, so a second Dispose cannot detach twice.
        var remove = Interlocked.Exchange(ref _remove, null);
        remove?.Invoke();
    }
}
