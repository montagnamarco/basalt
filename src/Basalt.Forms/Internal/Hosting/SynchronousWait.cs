using Avalonia.Threading;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// Waits for one of Avalonia's asynchronous platform calls (pickers,
/// clipboard) the way WinForms code expects: synchronously, while the UI
/// thread keeps processing messages in a nested frame, so the platform can
/// complete the call and the windows keep painting.
/// </summary>
internal static class SynchronousWait
{
    public static void For(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();

            // Ended from the UI thread, which also wakes the loop if it is waiting for messages.
            task.ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.UIThread.PushFrame(frame);
        }

        task.GetAwaiter().GetResult();
    }

    public static T For<T>(Task<T> task)
    {
        For((Task)task);

        return task.Result;
    }
}
