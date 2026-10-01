using Avalonia.Input.Platform;
using Avalonia.Threading;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>
/// The system clipboard, synchronously, as WinForms code expects it:
/// Avalonia's clipboard is asynchronous, so the call waits while the
/// dispatcher keeps running. Without a window to reach the clipboard
/// through, the text stays in the process.
/// </summary>
internal static class PlatformClipboard
{
    private static string? s_inProcessText;

    public static void SetText(string text)
    {
        s_inProcessText = text;

        if (Clipboard() is { } clipboard) Wait(clipboard.SetTextAsync(text));
    }

    public static string? GetText()
    {
        if (Clipboard() is { } clipboard)
        {
            var text = Wait(clipboard.TryGetTextAsync());
            if (text is not null) return text;
        }

        return s_inProcessText;
    }

    public static void Clear()
    {
        s_inProcessText = null;

        if (Clipboard() is { } clipboard) Wait(clipboard.ClearAsync());
    }

    private static IClipboard? Clipboard()
    {
        var host = Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(window => window is not null);

        return host?.Clipboard;
    }

    private static void Wait(Task task)
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

    private static T Wait<T>(Task<T> task)
    {
        Wait((Task)task);

        return task.Result;
    }
}
