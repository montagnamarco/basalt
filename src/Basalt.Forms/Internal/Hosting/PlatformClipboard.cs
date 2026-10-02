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

        if (Clipboard() is { } clipboard) SynchronousWait.For(clipboard.SetTextAsync(text));
    }

    public static string? GetText()
    {
        if (Clipboard() is { } clipboard)
        {
            var text = SynchronousWait.For(clipboard.TryGetTextAsync());
            if (text is not null) return text;
        }

        return s_inProcessText;
    }

    public static void Clear()
    {
        s_inProcessText = null;

        if (Clipboard() is { } clipboard) SynchronousWait.For(clipboard.ClearAsync());
    }

    private static IClipboard? Clipboard()
    {
        var host = Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(window => window is not null);
        if (host is null) return null;

        try
        {
            return host.Clipboard;
        }
        catch (InvalidOperationException)
        {
            // A platform without a clipboard service (the headless one, a minimal Linux session)
            // throws instead of answering null: the text then stays in the process, as without a window.
            return null;
        }
    }
}
