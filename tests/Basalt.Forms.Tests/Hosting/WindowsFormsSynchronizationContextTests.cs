using System.Threading;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Hosting;

/// <summary>The synchronization context an await inside a handler captures, so its continuation comes back to the UI thread.</summary>
public class WindowsFormsSynchronizationContextTests
{
    [AvaloniaFact]
    public void Send_on_the_UI_thread_runs_the_callback_directly()
    {
        var context = new WindowsFormsSynchronizationContext();
        var ranOnUiThread = false;

        context.Send(_ => ranOnUiThread = Dispatcher.UIThread.CheckAccess(), null);

        Assert.True(ranOnUiThread);
    }

    [AvaloniaFact]
    public void Post_marshals_the_callback_onto_the_UI_threads_queue()
    {
        var context = new WindowsFormsSynchronizationContext();
        var ranOnUiThread = false;

        var worker = new Thread(() => context.Post(_ => ranOnUiThread = Dispatcher.UIThread.CheckAccess(), null));
        worker.Start();
        worker.Join();

        Assert.False(ranOnUiThread, "Post must not run the callback before the dispatcher has drained its queue.");

        Dispatcher.UIThread.RunJobs();

        Assert.True(ranOnUiThread);
    }

    [Fact]
    public void CreateCopy_returns_an_independent_context_of_the_same_kind() =>
        Assert.IsType<WindowsFormsSynchronizationContext>(new WindowsFormsSynchronizationContext().CreateCopy());

    [AvaloniaFact]
    public void An_await_after_installing_the_context_resumes_on_the_UI_thread()
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        try
        {
            var completed = false;
            var resumedOnUiThread = false;

            async void Run()
            {
                await Task.Run(() => Thread.Sleep(10));
                resumedOnUiThread = Dispatcher.UIThread.CheckAccess();
                completed = true;
            }

            Run();

            // The continuation lands on the dispatcher's queue once the background work finishes: pump it until it does.
            for (var i = 0; i < 200 && !completed; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (!completed) Thread.Sleep(5);
            }

            Assert.True(completed, "The continuation never ran.");
            Assert.True(resumedOnUiThread, "The continuation ran, but not on the UI thread.");
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public void AutoInstall_defaults_to_true()
    {
        Assert.True(WindowsFormsSynchronizationContext.AutoInstall);
    }
}
