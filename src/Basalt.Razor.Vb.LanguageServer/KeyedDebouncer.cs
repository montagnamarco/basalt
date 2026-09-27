using System.Collections.Concurrent;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Runs work for a key only once the key has been quiet for a moment, and
/// cancels the work a newer request replaces.
/// </summary>
/// <remarks>
/// Asking Roslyn about a whole view on every keystroke meant one full
/// compilation per character typed, each finishing after the next had
/// started and all but the last thrown away. Now a burst of typing costs one
/// question, asked after the burst, and a question still running when the
/// next keystroke arrives is cancelled rather than finished for nothing.
/// </remarks>
public sealed class KeyedDebouncer(TimeSpan delay) : IDisposable
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.Ordinal);

    /// <summary>How long a key must be quiet before its work runs.</summary>
    public TimeSpan Delay { get; } = delay;

    /// <summary>
    /// Schedules work for a key, cancelling whatever was scheduled or running
    /// for it before.
    /// </summary>
    /// <returns>The work's task; it ends quietly when cancelled.</returns>
    public async Task ScheduleAsync(string key, Func<CancellationToken, Task> work)
    {
        var mine = new CancellationTokenSource();

        // Put in place before anything is awaited: with the swap after the
        // await, a burst of requests interleaved, each cancelled a request
        // that was already stale, and half the burst ran.
        CancellationTokenSource? previous = null;

        _pending.AddOrUpdate(key, mine, (_, current) =>
        {
            previous = current;
            return mine;
        });

        if (previous is not null)
        {
            // It may have finished and disposed itself a moment ago.
            try { await previous.CancelAsync().ConfigureAwait(false); } catch (ObjectDisposedException) { }
        }

        await RunAsync(key, mine, work).ConfigureAwait(false);
    }

    private async Task RunAsync(string key, CancellationTokenSource mine, Func<CancellationToken, Task> work)
    {
        try
        {
            await Task.Delay(Delay, mine.Token).ConfigureAwait(false);
            await work(mine.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (mine.IsCancellationRequested)
        {
            // Replaced by a newer request: nothing to report.
        }
        finally
        {
            // Only if still the latest: a newer one has put its own in place.
            _pending.TryRemove(new KeyValuePair<string, CancellationTokenSource>(key, mine));
            mine.Dispose();
        }
    }

    /// <summary>Cancels the work pending or running for a key, when a document closes.</summary>
    public void Cancel(string key)
    {
        if (_pending.TryRemove(key, out var pending)) pending.Cancel();
    }

    public void Dispose()
    {
        foreach (var pending in _pending.Values) pending.Cancel();

        _pending.Clear();
    }
}
