using System.Drawing;

namespace System.Windows.Forms;

public static partial class ControlPaint
{
    public static IntPtr CreateHBitmap16Bit(Bitmap bitmap, Color background) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static IntPtr CreateHBitmapColorMask(Bitmap bitmap, IntPtr monochromeMask) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static IntPtr CreateHBitmapTransparencyMask(Bitmap bitmap) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>
    /// Win32 draws this straight onto the screen's device context in
    /// R2_NOTXORPEN mode, so that drawing the same frame again restores the
    /// pixels underneath without anyone having repainted them. There is no
    /// screen device context here - SkiaSharp only ever draws onto a surface
    /// the caller owns - so there is nothing to XOR against and nothing to do.
    /// </summary>
    public static void DrawReversibleFrame(Rectangle rectangle, Color backColor, FrameStyle style)
    {
    }

    /// <summary>Same reason as <see cref="DrawReversibleFrame"/>: a screen-XOR primitive with no screen to draw on.</summary>
    public static void DrawReversibleLine(Point start, Point end, Color backColor)
    {
    }

    /// <summary>Same reason as <see cref="DrawReversibleFrame"/>: a screen-XOR primitive with no screen to draw on.</summary>
    public static void FillReversibleRectangle(Rectangle rectangle, Color backColor)
    {
    }
}
