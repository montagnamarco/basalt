using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// The language server asking the compiler once per pause in typing, not
/// once per keystroke.
/// </summary>
public sealed class KeyedDebouncerTests
{
    [Fact]
    public async Task ABurstOfRequestsRunsTheWorkOnceWithTheLastOne()
    {
        // Ten keystrokes used to be ten compilations of the whole view.
        using var debouncer = new KeyedDebouncer(TimeSpan.FromMilliseconds(100));

        var runs = new List<int>();
        var tasks = new List<Task>();

        for (var keystroke = 1; keystroke <= 10; keystroke++)
        {
            var mine = keystroke;

            tasks.Add(debouncer.ScheduleAsync("view.vbhtml", _ =>
            {
                lock (runs) runs.Add(mine);
                return Task.CompletedTask;
            }));
        }

        await Task.WhenAll(tasks);

        Assert.Equal([10], runs);
    }

    [Fact]
    public async Task WorkStillRunningIsCancelledByANewerRequest()
    {
        // A question still being answered when the next keystroke arrives is
        // stopped rather than finished for nothing.
        using var debouncer = new KeyedDebouncer(TimeSpan.Zero);

        var started = new TaskCompletionSource();
        var wasCancelled = false;

        var first = debouncer.ScheduleAsync("view.vbhtml", async token =>
        {
            started.SetResult();

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), token);
            }
            catch (OperationCanceledException)
            {
                wasCancelled = true;
                throw;
            }
        });

        await started.Task;

        await debouncer.ScheduleAsync("view.vbhtml", _ => Task.CompletedTask);
        await first;

        Assert.True(wasCancelled);
    }

    [Fact]
    public async Task EachDocumentHasItsOwnPause()
    {
        using var debouncer = new KeyedDebouncer(TimeSpan.FromMilliseconds(50));

        var runs = new List<string>();

        await Task.WhenAll(
            debouncer.ScheduleAsync("a.vbhtml", _ => { lock (runs) runs.Add("a"); return Task.CompletedTask; }),
            debouncer.ScheduleAsync("b.vbhtml", _ => { lock (runs) runs.Add("b"); return Task.CompletedTask; }));

        Assert.Equal(["a", "b"], runs.Order());
    }
}
