using Avalonia.Platform;

namespace System.Windows.Forms.Internal.Hosting;

/// <summary>Reads the displays through a window, which is where Avalonia exposes them.</summary>
internal static class HostScreens
{
    private static Avalonia.Controls.Window? s_probe;

    public static IReadOnlyList<Avalonia.Platform.Screen> Read()
    {
        // Any open form's window will do; otherwise a window that is never shown.
        var window = Application.OpenForms.Snapshot().Select(form => form.Host).FirstOrDefault(host => host is not null)
            ?? (Avalonia.Controls.Window)(s_probe ??= new Avalonia.Controls.Window());

        return window.Screens.All;
    }
}
