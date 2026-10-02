using System.Drawing.Imaging;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Controls;

namespace System.Windows.Forms.Internal.Tray;

/// <summary>
/// The real tray icon, through Avalonia's <see cref="TrayIcon"/>. Avalonia
/// reports only one interaction, <see cref="TrayIcon.Clicked"/>, with no
/// button or pointer position; this host turns it into a left <see
/// cref="ITrayIconHost.Click"/> at the current cursor position. Avalonia does
/// not report a double click, mouse down/up/move, or a right click
/// separately, so this host never raises those: NotifyIcon's corresponding
/// public events exist (as WinForms declares them) but are never raised
/// through a real tray icon on this platform.
/// </summary>
internal sealed class AvaloniaTrayIconHost : ITrayIconHost
{
    private readonly TrayIcon _trayIcon = new() { IsVisible = true };
    private bool _registered;

    // Avalonia's TrayIcon never reports these: declared only to satisfy ITrayIconHost.
#pragma warning disable CS0067
    public event EventHandler<TrayIconMouseEventArgs>? MouseDown;

    public event EventHandler<TrayIconMouseEventArgs>? MouseUp;

    public event EventHandler<TrayIconMouseEventArgs>? MouseMove;

    public event EventHandler<TrayIconMouseEventArgs>? DoubleClick;
#pragma warning restore CS0067

    public event EventHandler<TrayIconMouseEventArgs>? Click;

    public AvaloniaTrayIconHost()
    {
        AvaloniaPlatform.EnsureStarted();

        _trayIcon.Clicked += OnClicked;
        Register();
    }

    public void SetIcon(Drawing.Icon? icon)
    {
        if (icon is null)
        {
            _trayIcon.Icon = null;
            return;
        }

        using var bitmap = icon.ToBitmap();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        _trayIcon.Icon = new WindowIcon(stream);
    }

    public void SetToolTipText(string text) => _trayIcon.ToolTipText = text;

    public void Dispose()
    {
        _trayIcon.Clicked -= OnClicked;
        Unregister();
        _trayIcon.Dispose();
    }

    private void OnClicked(object? sender, EventArgs e) =>
        Click?.Invoke(this, new TrayIconMouseEventArgs(MouseButtons.Left, Cursor.Position));

    private void Register()
    {
        if (Avalonia.Application.Current is not { } application) return;

        var icons = TrayIcon.GetIcons(application) ?? [];
        icons.Add(_trayIcon);
        TrayIcon.SetIcons(application, icons);
        _registered = true;
    }

    private void Unregister()
    {
        if (!_registered || Avalonia.Application.Current is not { } application) return;

        TrayIcon.GetIcons(application)?.Remove(_trayIcon);
        _registered = false;
    }
}
