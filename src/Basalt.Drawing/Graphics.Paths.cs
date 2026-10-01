using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing;

/// <summary>Paths, regions and the members that stand in for the Windows device context.</summary>
public sealed partial class Graphics
{
    // Nothing to release here: the canvas and the clip paths are Skia objects
    // with finalizers of their own, and touching them from this one could
    // find them already finalized.
    ~Graphics()
    {
    }

    public void DrawPath(Pen pen, GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(pen);
        ArgumentNullException.ThrowIfNull(path);

        using var shape = path.ToSkia();
        StrokeOpen(shape, pen);
    }

    public void FillPath(Brush brush, GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(path);

        using var shape = path.ToSkia();
        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(shape, paint);
    }

    public void FillRegion(Brush brush, Region region)
    {
        ArgumentNullException.ThrowIfNull(brush);
        ArgumentNullException.ThrowIfNull(region);

        using var shape = region.Area is null ? Region.RectanglePath(Region.InfiniteBounds) : new SKPath(region.Area);
        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(shape, paint);
    }

    public void DrawRoundedRectangle(Pen pen, Rectangle rect, Size radius) => DrawRoundedRectangle(pen, (RectangleF)rect, radius);

    public void DrawRoundedRectangle(Pen pen, RectangleF rect, SizeF radius)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = new GraphicsPath();
        path.AddRoundedRectangle(rect, radius);
        DrawPath(pen, path);
    }

    public void FillRoundedRectangle(Brush brush, Rectangle rect, Size radius) => FillRoundedRectangle(brush, (RectangleF)rect, radius);

    public void FillRoundedRectangle(Brush brush, RectangleF rect, SizeF radius)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var path = new GraphicsPath();
        path.AddRoundedRectangle(rect, radius);
        FillPath(brush, path);
    }

    /// <summary>
    /// Every colour can be shown as it is: the surfaces this library draws on
    /// have 32 bits per pixel, never a palette.
    /// </summary>
    public Color GetNearestColor(Color color) => color;

    public void ReleaseHdc(IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void ReleaseHdcInternal(IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static IntPtr GetHalftonePalette() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>The clip and the transform, as the obsolete GDI+ call returns them.</summary>
    public object GetContextInfo() => new object[] { Clip, Transform };

    /// <summary>Where this Graphics's origin sits on the surface it draws on.</summary>
    public void GetContextInfo(out PointF offset) => offset = new PointF(_baseMatrix.M31, _baseMatrix.M32);

    public void GetContextInfo(out PointF offset, out Region? clip)
    {
        GetContextInfo(out offset);
        clip = Clip;
    }
}
