using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>Converts the plain value types (Point, Rectangle, ...) Microsoft ships to the Skia types this assembly draws with.</summary>
internal static class GeometryInterop
{
    public static SKRect ToSkia(this RectangleF rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static SKRect ToSkia(this Rectangle rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static SKPoint ToSkia(this PointF point) => new(point.X, point.Y);

    public static SKPoint ToSkia(this Point point) => new(point.X, point.Y);
}
