using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// A display, in WinForms coordinates: logical pixels, so that a form
/// placed at the centre of the working area is centred on a high-density
/// display as well.
/// </summary>
public class Screen
{
    private Screen(string deviceName, Rectangle bounds, Rectangle workingArea, bool primary)
    {
        DeviceName = deviceName;
        Bounds = bounds;
        WorkingArea = workingArea;
        Primary = primary;
    }

    public string DeviceName { get; }

    public Rectangle Bounds { get; }

    public Rectangle WorkingArea { get; }

    public bool Primary { get; }

    public int BitsPerPixel => 32;

    public static Screen[] AllScreens => ReadScreens();

    public static Screen? PrimaryScreen => AllScreens.FirstOrDefault(screen => screen.Primary) ?? AllScreens.FirstOrDefault();

    public static Screen FromPoint(Point point) => AllScreens.FirstOrDefault(screen => screen.Bounds.Contains(point)) ?? Fallback();

    public static Screen FromRectangle(Rectangle rect) =>
        AllScreens.OrderByDescending(screen => Overlap(screen.Bounds, rect)).FirstOrDefault() ?? Fallback();

    public static Screen FromControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return FromRectangle(control.RectangleToScreen(control.ClientRectangle));
    }

    public static Screen FromHandle(IntPtr hwnd) => Control.FromHandle(hwnd) is { } control ? FromControl(control) : Fallback();

    public static Rectangle GetBounds(Point pt) => FromPoint(pt).Bounds;

    public static Rectangle GetBounds(Rectangle rect) => FromRectangle(rect).Bounds;

    public static Rectangle GetBounds(Control ctl) => FromControl(ctl).Bounds;

    public static Rectangle GetWorkingArea(Point pt) => FromPoint(pt).WorkingArea;

    public static Rectangle GetWorkingArea(Rectangle rect) => FromRectangle(rect).WorkingArea;

    public static Rectangle GetWorkingArea(Control ctl) => FromControl(ctl).WorkingArea;

    public override bool Equals(object? obj) => obj is Screen other && other.DeviceName == DeviceName;

    public override int GetHashCode() => DeviceName.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => $"{GetType().Name}[Bounds={Bounds} WorkingArea={WorkingArea} Primary={Primary} DeviceName={DeviceName}]";

    private static int Overlap(Rectangle a, Rectangle b)
    {
        var overlap = Rectangle.Intersect(a, b);

        return overlap.Width * overlap.Height;
    }

    /// <summary>Without a display (a headless run), a single 1024 by 768 screen, as Windows reports with no monitor.</summary>
    private static Screen Fallback() => new(@"\\.\DISPLAY1", new Rectangle(0, 0, 1024, 768), new Rectangle(0, 0, 1024, 768), primary: true);

    private static Screen[] ReadScreens()
    {
        AvaloniaPlatform.EnsureStarted();

        var platformScreens = HostScreens.Read();
        if (platformScreens.Count == 0) return [Fallback()];

        return [.. platformScreens.Select((screen, index) => new Screen(
            $@"\\.\DISPLAY{index + 1}",
            Logical(screen.Bounds, screen.Scaling),
            Logical(screen.WorkingArea, screen.Scaling),
            screen.IsPrimary))];
    }

    private static Rectangle Logical(Avalonia.PixelRect rect, double scaling) => new(
        (int)Math.Round(rect.X / scaling),
        (int)Math.Round(rect.Y / scaling),
        (int)Math.Round(rect.Width / scaling),
        (int)Math.Round(rect.Height / scaling));
}
