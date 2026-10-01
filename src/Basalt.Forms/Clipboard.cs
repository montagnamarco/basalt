using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>The system clipboard. Text for now; other formats come with drag and drop's data objects.</summary>
public static class Clipboard
{
    public static void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        PlatformClipboard.SetText(text);
    }

    public static string GetText() => PlatformClipboard.GetText() ?? string.Empty;

    public static bool ContainsText() => !string.IsNullOrEmpty(PlatformClipboard.GetText());

    public static void Clear() => PlatformClipboard.Clear();
}
