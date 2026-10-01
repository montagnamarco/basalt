using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing;

/// <summary>
/// The shape overloads that need nothing but Pen, Brush and plain points:
/// lines, rectangles, ellipses, arcs, pies, polygons and cardinal-spline
/// curves. Every one goes through AlignToPixels, so the pixel rule holds for
/// all of them exactly as it does for DrawRectangle and FillRectangle.
/// </summary>
public sealed partial class Graphics
{
    // The default GDI+ uses for a curve's tension when the caller does not
    // name one: half of the vector between a point's neighbours.
    private const float DefaultTension = 0.5f;

    public void DrawLines(Pen pen, PointF[] points) => DrawLines(pen, (ReadOnlySpan<PointF>)RequirePoints(points));

    public void DrawLines(Pen pen, Point[] points) => DrawLines(pen, (ReadOnlySpan<Point>)RequirePoints(points));

    public void DrawLines(Pen pen, ReadOnlySpan<Point> points) => DrawLines(pen, ToPointF(points));

    public void DrawLines(Pen pen, ReadOnlySpan<PointF> points)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = OpenPath(points);
        StrokeOpen(path, pen);
    }

    public void DrawRectangles(Pen pen, RectangleF[] rects) => DrawRectangles(pen, (ReadOnlySpan<RectangleF>)RequirePoints(rects));

    public void DrawRectangles(Pen pen, Rectangle[] rects) => DrawRectangles(pen, (ReadOnlySpan<Rectangle>)RequirePoints(rects));

    public void DrawRectangles(Pen pen, ReadOnlySpan<Rectangle> rects) => DrawRectangles(pen, ToRectangleF(rects));

    public void DrawRectangles(Pen pen, ReadOnlySpan<RectangleF> rects)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        foreach (var rect in rects) _canvas.DrawRect(rect.ToSkia(), paint);
    }

    public void FillRectangles(Brush brush, RectangleF[] rects) => FillRectangles(brush, (ReadOnlySpan<RectangleF>)RequirePoints(rects));

    public void FillRectangles(Brush brush, Rectangle[] rects) => FillRectangles(brush, (ReadOnlySpan<Rectangle>)RequirePoints(rects));

    public void FillRectangles(Brush brush, ReadOnlySpan<Rectangle> rects) => FillRectangles(brush, ToRectangleF(rects));

    public void FillRectangles(Brush brush, ReadOnlySpan<RectangleF> rects)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        foreach (var rect in rects) _canvas.DrawRect(rect.ToSkia(), paint);
    }

    public void DrawEllipse(Pen pen, Rectangle rect) => DrawEllipse(pen, (float)rect.X, rect.Y, rect.Width, rect.Height);

    public void DrawEllipse(Pen pen, RectangleF rect) => DrawEllipse(pen, rect.X, rect.Y, rect.Width, rect.Height);

    public void DrawEllipse(Pen pen, int x, int y, int width, int height) => DrawEllipse(pen, (float)x, y, width, height);

    public void DrawEllipse(Pen pen, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        _canvas.DrawOval(new RectangleF(x, y, width, height).ToSkia(), paint);
    }

    public void FillEllipse(Brush brush, Rectangle rect) => FillEllipse(brush, (float)rect.X, rect.Y, rect.Width, rect.Height);

    public void FillEllipse(Brush brush, RectangleF rect) => FillEllipse(brush, rect.X, rect.Y, rect.Width, rect.Height);

    public void FillEllipse(Brush brush, int x, int y, int width, int height) => FillEllipse(brush, (float)x, y, width, height);

    public void FillEllipse(Brush brush, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        _canvas.DrawOval(new RectangleF(x, y, width, height).ToSkia(), paint);
    }

    public void DrawArc(Pen pen, Rectangle rect, float startAngle, float sweepAngle) => DrawArc(pen, (float)rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void DrawArc(Pen pen, RectangleF rect, float startAngle, float sweepAngle) => DrawArc(pen, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void DrawArc(Pen pen, int x, int y, int width, int height, int startAngle, int sweepAngle) => DrawArc(pen, (float)x, y, width, height, startAngle, sweepAngle);

    public void DrawArc(Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = ArcPath(x, y, width, height, startAngle, sweepAngle, pie: false);
        StrokeOpen(path, pen);
    }

    public void DrawPie(Pen pen, Rectangle rect, float startAngle, float sweepAngle) => DrawPie(pen, (float)rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void DrawPie(Pen pen, RectangleF rect, float startAngle, float sweepAngle) => DrawPie(pen, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void DrawPie(Pen pen, int x, int y, int width, int height, int startAngle, int sweepAngle) => DrawPie(pen, (float)x, y, width, height, startAngle, sweepAngle);

    public void DrawPie(Pen pen, float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        using var pie = ArcPath(x, y, width, height, startAngle, sweepAngle, pie: true);
        _canvas.DrawPath(pie, paint);
    }

    public void FillPie(Brush brush, Rectangle rect, float startAngle, float sweepAngle) => FillPie(brush, (float)rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void FillPie(Brush brush, RectangleF rect, float startAngle, float sweepAngle) => FillPie(brush, rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void FillPie(Brush brush, int x, int y, int width, int height, int startAngle, int sweepAngle) => FillPie(brush, (float)x, y, width, height, startAngle, sweepAngle);

    public void FillPie(Brush brush, float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        using var pie = ArcPath(x, y, width, height, startAngle, sweepAngle, pie: true);
        _canvas.DrawPath(pie, paint);
    }

    public void DrawPolygon(Pen pen, PointF[] points) => DrawPolygon(pen, (ReadOnlySpan<PointF>)RequirePoints(points));

    public void DrawPolygon(Pen pen, Point[] points) => DrawPolygon(pen, (ReadOnlySpan<Point>)RequirePoints(points));

    public void DrawPolygon(Pen pen, ReadOnlySpan<Point> points) => DrawPolygon(pen, ToPointF(points));

    public void DrawPolygon(Pen pen, ReadOnlySpan<PointF> points)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = ClosedPath(points, FillMode.Alternate);
        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(path, paint);
    }

    public void FillPolygon(Brush brush, PointF[] points) => FillPolygon(brush, points, FillMode.Alternate);

    public void FillPolygon(Brush brush, PointF[] points, FillMode fillMode) => FillPolygon(brush, (ReadOnlySpan<PointF>)RequirePoints(points), fillMode);

    public void FillPolygon(Brush brush, Point[] points) => FillPolygon(brush, points, FillMode.Alternate);

    public void FillPolygon(Brush brush, Point[] points, FillMode fillMode) => FillPolygon(brush, (ReadOnlySpan<Point>)RequirePoints(points), fillMode);

    public void FillPolygon(Brush brush, ReadOnlySpan<Point> points) => FillPolygon(brush, points, FillMode.Alternate);

    public void FillPolygon(Brush brush, ReadOnlySpan<Point> points, FillMode fillMode) => FillPolygon(brush, ToPointF(points), fillMode);

    public void FillPolygon(Brush brush, ReadOnlySpan<PointF> points) => FillPolygon(brush, points, FillMode.Alternate);

    public void FillPolygon(Brush brush, ReadOnlySpan<PointF> points, FillMode fillMode)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var path = ClosedPath(points, fillMode);
        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(path, paint);
    }

    public void DrawBezier(Pen pen, Point pt1, Point pt2, Point pt3, Point pt4) =>
        DrawBezier(pen, (float)pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);

    public void DrawBezier(Pen pen, PointF pt1, PointF pt2, PointF pt3, PointF pt4) =>
        DrawBezier(pen, pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);

    public void DrawBezier(Pen pen, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = new SKPath();
        path.MoveTo(x1, y1);
        path.CubicTo(x2, y2, x3, y3, x4, y4);

        StrokeOpen(path, pen);
    }

    public void DrawBeziers(Pen pen, PointF[] points) => DrawBeziers(pen, (ReadOnlySpan<PointF>)RequirePoints(points));

    public void DrawBeziers(Pen pen, Point[] points) => DrawBeziers(pen, (ReadOnlySpan<Point>)RequirePoints(points));

    public void DrawBeziers(Pen pen, ReadOnlySpan<Point> points) => DrawBeziers(pen, ToPointF(points));

    public void DrawBeziers(Pen pen, ReadOnlySpan<PointF> points)
    {
        ArgumentNullException.ThrowIfNull(pen);
        if (points.Length < 4 || (points.Length - 1) % 3 != 0)
        {
            throw new ArgumentException("A Bezier chain needs one point plus a multiple of 3 more.", nameof(points));
        }

        using var path = new SKPath();
        path.MoveTo(points[0].ToSkia());
        for (var i = 1; i < points.Length; i += 3) path.CubicTo(points[i].ToSkia(), points[i + 1].ToSkia(), points[i + 2].ToSkia());

        StrokeOpen(path, pen);
    }

    public void DrawCurve(Pen pen, PointF[] points) => DrawCurve(pen, points, 0, RequirePoints(points).Length - 1, DefaultTension);

    public void DrawCurve(Pen pen, PointF[] points, int offset, int numberOfSegments) => DrawCurve(pen, points, offset, numberOfSegments, DefaultTension);

    public void DrawCurve(Pen pen, PointF[] points, int offset, int numberOfSegments, float tension) =>
        DrawCurveCore(pen, (ReadOnlySpan<PointF>)RequirePoints(points), offset, numberOfSegments, tension);

    public void DrawCurve(Pen pen, PointF[] points, float tension) => DrawCurve(pen, points, 0, RequirePoints(points).Length - 1, tension);

    public void DrawCurve(Pen pen, Point[] points) => DrawCurve(pen, points, DefaultTension);

    public void DrawCurve(Pen pen, Point[] points, int offset, int numberOfSegments, float tension) =>
        DrawCurveCore(pen, ToPointF(RequirePoints(points)), offset, numberOfSegments, tension);

    public void DrawCurve(Pen pen, Point[] points, float tension) => DrawCurveCore(pen, ToPointF(RequirePoints(points)), 0, points.Length - 1, tension);

    public void DrawCurve(Pen pen, ReadOnlySpan<Point> points) => DrawCurve(pen, points, DefaultTension);

    public void DrawCurve(Pen pen, ReadOnlySpan<Point> points, int offset, int numberOfSegments, float tension) =>
        DrawCurveCore(pen, ToPointF(points), offset, numberOfSegments, tension);

    public void DrawCurve(Pen pen, ReadOnlySpan<Point> points, float tension) => DrawCurveCore(pen, ToPointF(points), 0, points.Length - 1, tension);

    public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points) => DrawCurve(pen, points, 0, points.Length - 1, DefaultTension);

    public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, int offset, int numberOfSegments) => DrawCurve(pen, points, offset, numberOfSegments, DefaultTension);

    public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, int offset, int numberOfSegments, float tension) => DrawCurveCore(pen, points, offset, numberOfSegments, tension);

    public void DrawCurve(Pen pen, ReadOnlySpan<PointF> points, float tension) => DrawCurveCore(pen, points, 0, points.Length - 1, tension);

    private void DrawCurveCore(Pen pen, ReadOnlySpan<PointF> points, int offset, int numberOfSegments, float tension)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = OpenCurvePath(points, offset, numberOfSegments, tension);
        StrokeOpen(path, pen);
    }

    public void DrawClosedCurve(Pen pen, PointF[] points) => DrawClosedCurve(pen, (ReadOnlySpan<PointF>)RequirePoints(points));

    public void DrawClosedCurve(Pen pen, PointF[] points, float tension, FillMode fillMode) =>
        DrawClosedCurve(pen, (ReadOnlySpan<PointF>)RequirePoints(points), tension, fillMode);

    public void DrawClosedCurve(Pen pen, Point[] points) => DrawClosedCurve(pen, (ReadOnlySpan<Point>)RequirePoints(points));

    public void DrawClosedCurve(Pen pen, Point[] points, float tension, FillMode fillMode) =>
        DrawClosedCurve(pen, (ReadOnlySpan<Point>)RequirePoints(points), tension, fillMode);

    public void DrawClosedCurve(Pen pen, ReadOnlySpan<Point> points) => DrawClosedCurve(pen, ToPointF(points), DefaultTension, FillMode.Alternate);

    public void DrawClosedCurve(Pen pen, ReadOnlySpan<Point> points, float tension, FillMode fillMode) => DrawClosedCurve(pen, ToPointF(points), tension, fillMode);

    public void DrawClosedCurve(Pen pen, ReadOnlySpan<PointF> points) => DrawClosedCurve(pen, points, DefaultTension, FillMode.Alternate);

    public void DrawClosedCurve(Pen pen, ReadOnlySpan<PointF> points, float tension, FillMode fillMode)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var path = ClosedCurvePath(points, tension, fillMode);
        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(path, paint);
    }

    public void FillClosedCurve(Brush brush, PointF[] points) => FillClosedCurve(brush, (ReadOnlySpan<PointF>)RequirePoints(points));

    public void FillClosedCurve(Brush brush, PointF[] points, FillMode fillMode) => FillClosedCurve(brush, (ReadOnlySpan<PointF>)RequirePoints(points), fillMode);

    public void FillClosedCurve(Brush brush, PointF[] points, FillMode fillMode, float tension) =>
        FillClosedCurve(brush, (ReadOnlySpan<PointF>)RequirePoints(points), fillMode, tension);

    public void FillClosedCurve(Brush brush, Point[] points) => FillClosedCurve(brush, (ReadOnlySpan<Point>)RequirePoints(points));

    public void FillClosedCurve(Brush brush, Point[] points, FillMode fillMode) => FillClosedCurve(brush, (ReadOnlySpan<Point>)RequirePoints(points), fillMode);

    public void FillClosedCurve(Brush brush, Point[] points, FillMode fillMode, float tension) =>
        FillClosedCurve(brush, (ReadOnlySpan<Point>)RequirePoints(points), fillMode, tension);

    public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points) => FillClosedCurve(brush, ToPointF(points));

    public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points, FillMode fillMode) => FillClosedCurve(brush, ToPointF(points), fillMode);

    public void FillClosedCurve(Brush brush, ReadOnlySpan<Point> points, FillMode fillMode, float tension) => FillClosedCurve(brush, ToPointF(points), fillMode, tension);

    public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points) => FillClosedCurve(brush, points, FillMode.Alternate);

    public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points, FillMode fillMode) => FillClosedCurve(brush, points, fillMode, DefaultTension);

    public void FillClosedCurve(Brush brush, ReadOnlySpan<PointF> points, FillMode fillMode, float tension)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var path = ClosedCurvePath(points, tension, fillMode);
        using var paint = Fill(brush);
        using var aligned = AlignToPixels();
        _canvas.DrawPath(path, paint);
    }

    private static T[] RequirePoints<T>(T[] points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return points;
    }

    private static SKPath OpenPath(ReadOnlySpan<PointF> points)
    {
        var path = new SKPath();
        if (points.Length == 0) return path;

        path.MoveTo(points[0].ToSkia());
        for (var i = 1; i < points.Length; i++) path.LineTo(points[i].ToSkia());

        return path;
    }

    private static SKPath ClosedPath(ReadOnlySpan<PointF> points, FillMode fillMode)
    {
        var path = OpenPath(points);
        path.Close();
        path.FillType = fillMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd;

        return path;
    }

    /// <summary>
    /// The GDI+ cardinal spline: for a segment Pi to Pi+1, the cubic Bezier
    /// control points are Pi + (Pi+1 - Pi-1) * tension / 3 and
    /// Pi+1 - (Pi+2 - Pi) * tension / 3, falling back to Pi (respectively
    /// Pi+1) for the neighbour that does not exist at either end of an open
    /// curve, which is what "one-sided" means here.
    /// </summary>
    private static SKPath OpenCurvePath(ReadOnlySpan<PointF> points, int offset, int numberOfSegments, float tension)
    {
        var path = new SKPath();
        if (points.Length == 0 || numberOfSegments < 1) return path;

        path.MoveTo(points[offset].ToSkia());
        for (var i = 0; i < numberOfSegments; i++)
        {
            var i1 = offset + i;
            var i2 = i1 + 1;
            var p1 = points[i1];
            var p2 = points[i2];
            var p0 = i1 - 1 >= 0 ? points[i1 - 1] : p1;
            var p3 = i2 + 1 < points.Length ? points[i2 + 1] : p2;

            AddCurveSegment(path, p0, p1, p2, p3, tension);
        }

        return path;
    }

    /// <summary>The same spline, but the point list wraps around: every point has two real neighbours, and a final segment closes back to the start.</summary>
    private static SKPath ClosedCurvePath(ReadOnlySpan<PointF> points, float tension, FillMode fillMode)
    {
        var path = new SKPath();
        var n = points.Length;
        if (n == 0) return path;

        path.MoveTo(points[0].ToSkia());
        for (var i = 0; i < n; i++)
        {
            var p0 = points[(i - 1 + n) % n];
            var p1 = points[i];
            var p2 = points[(i + 1) % n];
            var p3 = points[(i + 2) % n];

            AddCurveSegment(path, p0, p1, p2, p3, tension);
        }

        path.Close();
        path.FillType = fillMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd;

        return path;
    }

    private static void AddCurveSegment(SKPath path, PointF p0, PointF p1, PointF p2, PointF p3, float tension)
    {
        var c1 = new PointF(p1.X + (p2.X - p0.X) * tension / 3f, p1.Y + (p2.Y - p0.Y) * tension / 3f);
        var c2 = new PointF(p2.X - (p3.X - p1.X) * tension / 3f, p2.Y - (p3.Y - p1.Y) * tension / 3f);

        path.CubicTo(c1.ToSkia(), c2.ToSkia(), p2.ToSkia());
    }

    private static PointF[] ToPointF(ReadOnlySpan<Point> points)
    {
        var result = new PointF[points.Length];
        for (var i = 0; i < points.Length; i++) result[i] = new PointF(points[i].X, points[i].Y);

        return result;
    }

    private static RectangleF[] ToRectangleF(ReadOnlySpan<Rectangle> rects)
    {
        var result = new RectangleF[rects.Length];
        for (var i = 0; i < rects.Length; i++) result[i] = new RectangleF(rects[i].X, rects[i].Y, rects[i].Width, rects[i].Height);

        return result;
    }

    /// <summary>
    /// An arc, or a pie closed through the centre, with GDI+'s geometry: the
    /// angles measured on the ellipse, as GraphicsPath.AddArc does, so that a
    /// drawn arc and the same arc added to a path coincide.
    /// </summary>
    private static SKPath ArcPath(float x, float y, float width, float height, float startAngle, float sweepAngle, bool pie)
    {
        var arc = PathGeometry.Arc(x, y, width, height, startAngle, sweepAngle);
        var path = new SKPath();

        if (arc.Count == 0) return path;

        if (pie)
        {
            path.MoveTo(x + width / 2, y + height / 2);
            path.LineTo(arc[0].ToSkia());
        }
        else
        {
            path.MoveTo(arc[0].ToSkia());
        }

        for (var i = 1; i + 2 < arc.Count; i += 3)
        {
            path.CubicTo(arc[i].ToSkia(), arc[i + 1].ToSkia(), arc[i + 2].ToSkia());
        }

        if (pie) path.Close();

        return path;
    }
}
