using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>Waiting for an asynchronous platform call from the UI thread, as the pickers and the clipboard do.</summary>
public class SynchronousWaitTests
{
    [AvaloniaFact]
    public void A_wait_on_the_UI_thread_completes_and_keeps_processing_messages()
    {
        var posted = false;
        Dispatcher.UIThread.Post(() => posted = true);

        var result = SynchronousWait.For(DelayedValue());

        Assert.Equal(42, result);
        Assert.True(posted, "The UI thread stopped processing messages while waiting.");
    }

    [AvaloniaFact]
    public void A_wait_for_a_task_that_continues_on_the_UI_thread_does_not_deadlock()
    {
        var result = SynchronousWait.For(ContinuesOnUiThread());

        Assert.Equal("done", result);
    }

    private static async Task<int> DelayedValue()
    {
        await Task.Delay(50).ConfigureAwait(false);

        return 42;
    }

    /// <summary>Like Avalonia's pickers: the continuation needs the UI thread, which a blocking wait would hold.</summary>
    private static async Task<string> ContinuesOnUiThread()
    {
        await Task.Delay(20);
        await Dispatcher.UIThread.InvokeAsync(() => { });

        return "done";
    }
}
