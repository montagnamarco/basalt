using System.Drawing;

namespace System.Windows.Forms.Internal.Tray;

/// <summary>
/// One tray icon, as NotifyIcon needs it: an icon, a tooltip text, and the
/// mouse activity the shell reports. Behind an interface so NotifyIcon tests
/// substitute a fake and never show a real tray icon; the real host wraps
/// Avalonia's TrayIcon, which only reports a plain click, so production code
/// only ever raises <see cref="Click"/> through it (see AvaloniaTrayIconHost).
/// </summary>
internal interface ITrayIconHost : IDisposable
{
    void SetIcon(Icon? icon);

    void SetToolTipText(string text);

    event EventHandler<TrayIconMouseEventArgs>? MouseDown;

    event EventHandler<TrayIconMouseEventArgs>? MouseUp;

    event EventHandler<TrayIconMouseEventArgs>? MouseMove;

    event EventHandler<TrayIconMouseEventArgs>? Click;

    event EventHandler<TrayIconMouseEventArgs>? DoubleClick;
}

/// <summary>A mouse event reported by the tray: the button involved and the pointer's screen position.</summary>
internal sealed class TrayIconMouseEventArgs(MouseButtons button, Point location) : EventArgs
{
    public MouseButtons Button { get; } = button;

    public Point Location { get; } = location;
}

/// <summary>Creates the host for one tray icon; tests swap the factory to avoid a real tray icon.</summary>
internal static class TrayIconHosts
{
    public static Func<ITrayIconHost> Factory { get; set; } = static () => new AvaloniaTrayIconHost();
}
