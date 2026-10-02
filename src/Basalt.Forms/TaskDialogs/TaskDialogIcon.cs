using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// An icon shown by a task dialog, either one of the standard ones Windows
/// draws for its built-in dialogs or a custom bitmap/icon supplied by the
/// application. Drawn the way MessageBox draws its icons: no native shield
/// artwork, so every shield variant uses the plain shield glyph.
/// </summary>
public class TaskDialogIcon : IDisposable
{
    public static readonly TaskDialogIcon Error = new(SystemIcons.Error, standard: true);
    public static readonly TaskDialogIcon Information = new(SystemIcons.Information, standard: true);
    public static readonly TaskDialogIcon None = new((Icon?)null, standard: true);
    public static readonly TaskDialogIcon Shield = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon ShieldBlueBar = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon ShieldErrorRedBar = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon ShieldGrayBar = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon ShieldSuccessGreenBar = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon ShieldWarningYellowBar = new(SystemIcons.Shield, standard: true);
    public static readonly TaskDialogIcon Warning = new(SystemIcons.Warning, standard: true);

    private readonly Icon? _icon;
    private readonly Bitmap? _bitmap;
    private readonly IntPtr _handle;
    private bool _disposed;

    /// <summary>Used only for the standard icons above: an Icon? that may legitimately be null (None).</summary>
    private TaskDialogIcon(Icon? icon, bool standard) => _icon = icon;

    public TaskDialogIcon(Bitmap image) => _bitmap = image ?? throw new ArgumentNullException(nameof(image));

    public TaskDialogIcon(Icon icon) => _icon = icon ?? throw new ArgumentNullException(nameof(icon));

    public TaskDialogIcon(IntPtr iconHandle) => _handle = iconHandle;

    /// <summary>
    /// The native handle, for icons made from one. Standard and image-based
    /// icons have no native handle here (there is no GDI in Basalt.Drawing).
    /// </summary>
    public IntPtr IconHandle => _handle != IntPtr.Zero
        ? _handle
        : throw new PlatformNotSupportedException("This icon was not created from a native handle.");

    /// <summary>Draws the icon the way MessageBox draws its icon, at its natural size.</summary>
    internal void Draw(Graphics graphics, Rectangle bounds)
    {
        if (_icon is not null) graphics.DrawIcon(_icon, bounds);
        else if (_bitmap is not null) graphics.DrawImage(_bitmap, bounds);
    }

    internal bool HasImage => _icon is not null || _bitmap is not null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
