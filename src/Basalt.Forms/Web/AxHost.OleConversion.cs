using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The static helpers a generated Ax wrapper calls to cross between .NET
/// and OLE Automation types. OLE_COLOR and the OLE Automation date are both
/// just numbers - a packed RGB value, a day count - so those conversions
/// work with no COM involved. IFont, IFontDisp, IPicture and IPictureDisp
/// are COM interfaces with no .NET equivalent here, so the helpers that
/// would wrap or unwrap one refuse instead of pretending to produce it.
/// </summary>
public abstract partial class AxHost
{
    private const string NoComInterop =
        "IFont, IFontDisp, IPicture and IPictureDisp are COM interfaces; Basalt has no COM interop, so there is no object of that shape to produce or read.";

    protected static Font GetFontFromIFont(object font) => throw new PlatformNotSupportedException(NoComInterop);

    protected static Font GetFontFromIFontDisp(object font) => throw new PlatformNotSupportedException(NoComInterop);

    protected static object GetIFontDispFromFont(Font font) => throw new PlatformNotSupportedException(NoComInterop);

    protected static object GetIFontFromFont(Font font) => throw new PlatformNotSupportedException(NoComInterop);

    protected static object GetIPictureDispFromPicture(Image image) => throw new PlatformNotSupportedException(NoComInterop);

    protected static object GetIPictureFromCursor(Cursor cursor) => throw new PlatformNotSupportedException(NoComInterop);

    protected static object GetIPictureFromPicture(Image image) => throw new PlatformNotSupportedException(NoComInterop);

    protected static Image GetPictureFromIPicture(object picture) => throw new PlatformNotSupportedException(NoComInterop);

    protected static Image GetPictureFromIPictureDisp(object picture) => throw new PlatformNotSupportedException(NoComInterop);

    /// <summary>OLE_COLOR packs a colour the same way COLORREF does: 0x00BBGGRR, with no alpha channel.</summary>
    protected static uint GetOleColorFromColor(Color color) => (uint)color.R | ((uint)color.G << 8) | ((uint)color.B << 16);

    protected static Color GetColorFromOleColor(uint color) =>
        Color.FromArgb((int)(color & 0xFF), (int)((color >> 8) & 0xFF), (int)((color >> 16) & 0xFF));

    /// <summary>An OLE Automation date is a day count from 1899-12-30; .NET already does this conversion portably.</summary>
    protected static double GetOADateFromTime(DateTime time) => time.ToOADate();

    protected static DateTime GetTimeFromOADate(double date) => DateTime.FromOADate(date);
}
