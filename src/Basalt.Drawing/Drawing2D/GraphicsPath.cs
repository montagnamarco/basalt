using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// A sequence of figures made of lines and Bézier curves, kept as GDI+ keeps
/// it: one list of points and one list of point types.
/// </summary>
/// <remarks>
/// The type of a point says how the path reaches it: Start opens a figure,
/// Line and Bezier draw to it, and the flags CloseSubpath and PathMarker ride
/// on the last point of a figure or of a marked section. Applications read and
/// write these arrays directly, so they are the model and a Skia path is only
/// made from them when something has to be drawn or measured.
/// </remarks>
public sealed class GraphicsPath : MarshalByRefObject, ICloneable, IDisposable
{
    private readonly List<PointF> _points = [];
    private readonly List<byte> _types = [];

    // The next point opens a figure instead of continuing the current one:
    // true at the start, after StartFigure and after any figure that closes.
    private bool _newFigure = true;

    public GraphicsPath()
        : this(FillMode.Alternate)
    {
    }

    public GraphicsPath(FillMode fillMode) => FillMode = fillMode;

    public GraphicsPath(PointF[] pts, byte[] types)
        : this(pts, types, FillMode.Alternate)
    {
    }

    public GraphicsPath(PointF[] pts, byte[] types, FillMode fillMode)
        : this(Required(pts).AsSpan(), Required(types).AsSpan(), fillMode)
    {
    }

    public GraphicsPath(Point[] pts, byte[] types)
        : this(pts, types, FillMode.Alternate)
    {
    }

    public GraphicsPath(Point[] pts, byte[] types, FillMode fillMode)
        : this(ToPointF(Required(pts)), types, fillMode)
    {
    }

    public GraphicsPath(ReadOnlySpan<Point> pts, ReadOnlySpan<byte> types, FillMode fillMode = FillMode.Alternate)
        : this(ToPointF(pts), types, fillMode)
    {
    }

    public GraphicsPath(ReadOnlySpan<PointF> pts, ReadOnlySpan<byte> types, FillMode fillMode = FillMode.Alternate)
    {
        if (pts.Length != types.Length) throw new ArgumentException("Parameter is not valid.");

        FillMode = fillMode;
        _points.AddRange(pts);
        _types.AddRange(types);
        _newFigure = _types.Count == 0 || (_types[^1] & PathGeometry.Close) != 0;
    }

    ~GraphicsPath() => Dispose(false);

    public FillMode FillMode { get; set; }

    public int PointCount => _points.Count;

    public PointF[] PathPoints => [.. _points];

    public byte[] PathTypes => [.. _types];

    public PathData PathData => new() { Points = PathPoints, Types = PathTypes };

    public int GetPathPoints(Span<PointF> destination)
    {
        var count = Math.Min(destination.Length, _points.Count);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_points)[..count].CopyTo(destination);

        return count;
    }

    public int GetPathTypes(Span<byte> destination)
    {
        var count = Math.Min(destination.Length, _types.Count);
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_types)[..count].CopyTo(destination);

        return count;
    }

    public PointF GetLastPoint() =>
        _points.Count > 0 ? _points[^1] : throw new ArgumentException("Parameter is not valid.");

    public object Clone()
    {
        var copy = new GraphicsPath(FillMode);
        copy._points.AddRange(_points);
        copy._types.AddRange(_types);
        copy._newFigure = _newFigure;

        return copy;
    }

    public void Reset()
    {
        _points.Clear();
        _types.Clear();
        _newFigure = true;
        FillMode = FillMode.Alternate;
    }

    public void StartFigure() => _newFigure = true;

    public void CloseFigure()
    {
        if (_types.Count > 0) _types[^1] |= PathGeometry.Close;

        _newFigure = true;
    }

    public void CloseAllFigures()
    {
        for (var i = 0; i < _types.Count; i++)
        {
            var endsFigure = i == _types.Count - 1 || (_types[i + 1] & PathGeometry.TypeMask) == PathGeometry.Start;
            if (endsFigure) _types[i] |= PathGeometry.Close;
        }

        _newFigure = true;
    }

    public void SetMarkers()
    {
        if (_types.Count > 0) _types[^1] |= PathGeometry.Marker;
    }

    public void ClearMarkers()
    {
        for (var i = 0; i < _types.Count; i++)
        {
            _types[i] = (byte)(_types[i] & ~PathGeometry.Marker);
        }
    }

    // --- Lines -----------------------------------------------------------

    public void AddLine(Point pt1, Point pt2) => AddLine((float)pt1.X, pt1.Y, pt2.X, pt2.Y);

    public void AddLine(PointF pt1, PointF pt2) => AddLine(pt1.X, pt1.Y, pt2.X, pt2.Y);

    public void AddLine(int x1, int y1, int x2, int y2) => AddLine((float)x1, y1, x2, y2);

    public void AddLine(float x1, float y1, float x2, float y2)
    {
        Continue(new PointF(x1, y1));
        Append(new PointF(x2, y2), PathGeometry.Line);
    }

    public void AddLines(PointF[] points) => AddLines(Required(points).AsSpan());

    public void AddLines(Point[] points) => AddLines(ToPointF(Required(points)));

    public void AddLines(ReadOnlySpan<Point> points) => AddLines(ToPointF(points));

    public void AddLines(ReadOnlySpan<PointF> points)
    {
        if (points.IsEmpty) throw new ArgumentException("Parameter is not valid.");

        Continue(points[0]);

        for (var i = 1; i < points.Length; i++)
        {
            Append(points[i], PathGeometry.Line);
        }
    }

    // --- Curves ----------------------------------------------------------

    public void AddBezier(Point pt1, Point pt2, Point pt3, Point pt4) =>
        AddBezier((float)pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);

    public void AddBezier(PointF pt1, PointF pt2, PointF pt3, PointF pt4) =>
        AddBezier(pt1.X, pt1.Y, pt2.X, pt2.Y, pt3.X, pt3.Y, pt4.X, pt4.Y);

    public void AddBezier(int x1, int y1, int x2, int y2, int x3, int y3, int x4, int y4) =>
        AddBezier((float)x1, y1, x2, y2, x3, y3, x4, y4);

    public void AddBezier(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
    {
        Continue(new PointF(x1, y1));
        Append(new PointF(x2, y2), PathGeometry.Bezier);
        Append(new PointF(x3, y3), PathGeometry.Bezier);
        Append(new PointF(x4, y4), PathGeometry.Bezier);
    }

    public void AddBeziers(PointF[] points) => AddBeziers(Required(points).AsSpan());

    public void AddBeziers(Point[] points) => AddBeziers(ToPointF(Required(points)));

    public void AddBeziers(ReadOnlySpan<Point> points) => AddBeziers(ToPointF(points));

    public void AddBeziers(ReadOnlySpan<PointF> points)
    {
        // A start point and three more for every segment.
        if (points.Length < 4 || (points.Length - 1) % 3 != 0) throw new ArgumentException("Parameter is not valid.");

        Continue(points[0]);

        for (var i = 1; i < points.Length; i++)
        {
            Append(points[i], PathGeometry.Bezier);
        }
    }

    public void AddCurve(PointF[] points) => AddCurve(points, 0.5f);

    public void AddCurve(PointF[] points, float tension) => AddCurve(Required(points).AsSpan(), tension);

    public void AddCurve(PointF[] points, int offset, int numberOfSegments, float tension) =>
        AddCurveSegments(Required(points), offset, numberOfSegments, tension);

    public void AddCurve(Point[] points) => AddCurve(points, 0.5f);

    public void AddCurve(Point[] points, float tension) => AddCurve(ToPointF(Required(points)), tension);

    public void AddCurve(Point[] points, int offset, int numberOfSegments, float tension) =>
        AddCurveSegments(ToPointF(Required(points)), offset, numberOfSegments, tension);

    public void AddCurve(ReadOnlySpan<Point> points) => AddCurve(ToPointF(points), 0.5f);

    public void AddCurve(ReadOnlySpan<Point> points, float tension) => AddCurve(ToPointF(points), tension);

    public void AddCurve(ReadOnlySpan<PointF> points) => AddCurve(points, 0.5f);

    public void AddCurve(ReadOnlySpan<PointF> points, float tension) =>
        AddCurveSegments(points, 0, points.Length - 1, tension);

    private void AddCurveSegments(ReadOnlySpan<PointF> points, int offset, int numberOfSegments, float tension)
    {
        if (points.Length < 2 || numberOfSegments < 1 || offset < 0 || offset + numberOfSegments >= points.Length)
        {
            throw new ArgumentException("Parameter is not valid.");
        }

        var tangents = PathGeometry.CurveTangents(points, tension, closed: false);
        var segments = new List<PointF>();
        PathGeometry.AppendCurveSegments(segments, points, tangents, offset, numberOfSegments, closed: false);

        Continue(points[offset]);

        foreach (var point in segments)
        {
            Append(point, PathGeometry.Bezier);
        }
    }

    public void AddClosedCurve(PointF[] points) => AddClosedCurve(points, 0.5f);

    public void AddClosedCurve(PointF[] points, float tension) => AddClosedCurve(Required(points).AsSpan(), tension);

    public void AddClosedCurve(Point[] points) => AddClosedCurve(points, 0.5f);

    public void AddClosedCurve(Point[] points, float tension) => AddClosedCurve(ToPointF(Required(points)), tension);

    public void AddClosedCurve(ReadOnlySpan<Point> points) => AddClosedCurve(ToPointF(points), 0.5f);

    public void AddClosedCurve(ReadOnlySpan<Point> points, float tension) => AddClosedCurve(ToPointF(points), tension);

    public void AddClosedCurve(ReadOnlySpan<PointF> points) => AddClosedCurve(points, 0.5f);

    public void AddClosedCurve(ReadOnlySpan<PointF> points, float tension)
    {
        if (points.Length < 3) throw new ArgumentException("Parameter is not valid.");

        var tangents = PathGeometry.CurveTangents(points, tension, closed: true);
        var segments = new List<PointF>();
        PathGeometry.AppendCurveSegments(segments, points, tangents, 0, points.Length, closed: true);

        AddFigure(points[0], segments, PathGeometry.Bezier);
    }

    // --- Closed shapes ---------------------------------------------------

    public void AddRectangle(Rectangle rect) => AddRectangle((RectangleF)rect);

    public void AddRectangle(RectangleF rect)
    {
        // A rectangle with no area adds nothing, not a degenerate figure.
        if (rect.Width == 0 || rect.Height == 0) return;

        AddFigure(new PointF(rect.Left, rect.Top), [new(rect.Right, rect.Top), new(rect.Right, rect.Bottom), new(rect.Left, rect.Bottom)], PathGeometry.Line);
    }

    public void AddRectangles(RectangleF[] rects) => AddRectangles(Required(rects).AsSpan());

    public void AddRectangles(Rectangle[] rects) => AddRectangles(Required(rects).AsSpan());

    public void AddRectangles(ReadOnlySpan<Rectangle> rects)
    {
        foreach (var rect in rects)
        {
            AddRectangle(rect);
        }
    }

    public void AddRectangles(ReadOnlySpan<RectangleF> rects)
    {
        foreach (var rect in rects)
        {
            AddRectangle(rect);
        }
    }

    public void AddRoundedRectangle(Rectangle rect, Size radius) => AddRoundedRectangle((RectangleF)rect, radius);

    /// <summary>A rectangle whose corners are quarters of an ellipse of the given size.</summary>
    public void AddRoundedRectangle(RectangleF rect, SizeF radius)
    {
        if (rect.Width == 0 || rect.Height == 0) return;

        // The corner ellipse cannot be larger than the rectangle.
        var width = Math.Min(Math.Abs(radius.Width), rect.Width);
        var height = Math.Min(Math.Abs(radius.Height), rect.Height);

        if (width == 0 || height == 0)
        {
            AddRectangle(rect);
            return;
        }

        StartFigure();
        AddArc(rect.Right - width, rect.Top, width, height, 270, 90);
        AddArc(rect.Right - width, rect.Bottom - height, width, height, 0, 90);
        AddArc(rect.Left, rect.Bottom - height, width, height, 90, 90);
        AddArc(rect.Left, rect.Top, width, height, 180, 90);
        CloseFigure();
    }

    public void AddEllipse(Rectangle rect) => AddEllipse((float)rect.X, rect.Y, rect.Width, rect.Height);

    public void AddEllipse(RectangleF rect) => AddEllipse(rect.X, rect.Y, rect.Width, rect.Height);

    public void AddEllipse(int x, int y, int width, int height) => AddEllipse((float)x, y, width, height);

    public void AddEllipse(float x, float y, float width, float height)
    {
        var points = PathGeometry.Ellipse(x, y, width, height);

        AddFigure(points[0], points[1..], PathGeometry.Bezier);
    }

    public void AddArc(Rectangle rect, float startAngle, float sweepAngle) =>
        AddArc((float)rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void AddArc(RectangleF rect, float startAngle, float sweepAngle) =>
        AddArc(rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void AddArc(int x, int y, int width, int height, float startAngle, float sweepAngle) =>
        AddArc((float)x, y, width, height, startAngle, sweepAngle);

    public void AddArc(float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        var arc = PathGeometry.Arc(x, y, width, height, startAngle, sweepAngle);
        if (arc.Count == 0) return;

        Continue(arc[0]);

        for (var i = 1; i < arc.Count; i++)
        {
            Append(arc[i], PathGeometry.Bezier);
        }
    }

    public void AddPie(Rectangle rect, float startAngle, float sweepAngle) =>
        AddPie((float)rect.X, rect.Y, rect.Width, rect.Height, startAngle, sweepAngle);

    public void AddPie(int x, int y, int width, int height, float startAngle, float sweepAngle) =>
        AddPie((float)x, y, width, height, startAngle, sweepAngle);

    /// <summary>A figure from the centre to the arc and back.</summary>
    public void AddPie(float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        StartFigure();
        Append(new PointF(x + width / 2, y + height / 2), PathGeometry.Line);

        var arc = PathGeometry.Arc(x, y, width, height, startAngle, sweepAngle);
        if (arc.Count > 0)
        {
            Append(arc[0], PathGeometry.Line);

            for (var i = 1; i < arc.Count; i++)
            {
                Append(arc[i], PathGeometry.Bezier);
            }
        }

        CloseFigure();
    }

    public void AddPolygon(PointF[] points) => AddPolygon(Required(points).AsSpan());

    public void AddPolygon(Point[] points) => AddPolygon(ToPointF(Required(points)));

    public void AddPolygon(ReadOnlySpan<Point> points) => AddPolygon(ToPointF(points));

    public void AddPolygon(ReadOnlySpan<PointF> points)
    {
        if (points.Length < 3) throw new ArgumentException("Parameter is not valid.");

        AddFigure(points[0], points[1..].ToArray(), PathGeometry.Line);
    }

    /// <summary>Adds the figures of another path; with connect, its first figure continues the open one.</summary>
    public void AddPath(GraphicsPath addingPath, bool connect)
    {
        ArgumentNullException.ThrowIfNull(addingPath);

        if (addingPath._points.Count == 0) return;

        var first = _points.Count;
        var joining = connect && !_newFigure && _points.Count > 0;

        _points.AddRange(addingPath._points);
        _types.AddRange(addingPath._types);

        if (joining && (_types[first] & PathGeometry.TypeMask) == PathGeometry.Start)
        {
            _types[first] = (byte)((_types[first] & ~PathGeometry.TypeMask) | PathGeometry.Line);
        }

        _newFigure = (_types[^1] & PathGeometry.Close) != 0;
    }

    // --- Changing the path -----------------------------------------------

    public void Transform(Matrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        var points = PathPoints;
        matrix.TransformPoints(points);

        for (var i = 0; i < points.Length; i++)
        {
            _points[i] = points[i];
        }
    }

    public void Flatten() => Flatten(null, 0.25f);

    public void Flatten(Matrix? matrix) => Flatten(matrix, 0.25f);

    public void Flatten(Matrix? matrix, float flatness)
    {
        if (matrix is not null) Transform(matrix);

        PathGeometry.Flatten(_points, _types, flatness);
    }

    /// <summary>
    /// Replaces the path with the outline of what the pen would paint along it.
    /// </summary>
    public void Widen(Pen pen) => Widen(pen, null, 0.25f);

    public void Widen(Pen pen, Matrix? matrix) => Widen(pen, matrix, 0.25f);

    public void Widen(Pen pen, Matrix? matrix, float flatness)
    {
        ArgumentNullException.ThrowIfNull(pen);

        if (matrix is not null) Transform(matrix);

        using var outline = Outline(pen);

        _points.Clear();
        _types.Clear();
        PathGeometry.FromSkia(outline, _points, _types);
        PathGeometry.Flatten(_points, _types, flatness);
        _newFigure = true;
    }

    public void Warp(PointF[] destPoints, RectangleF srcRect) => Warp(destPoints, srcRect, null, WarpMode.Perspective, 0.25f);

    public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix) => Warp(destPoints, srcRect, matrix, WarpMode.Perspective, 0.25f);

    public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix, WarpMode warpMode) =>
        Warp(destPoints, srcRect, matrix, warpMode, 0.25f);

    public void Warp(PointF[] destPoints, RectangleF srcRect, Matrix? matrix, WarpMode warpMode, float flatness) =>
        Warp(Required(destPoints).AsSpan(), srcRect, matrix, warpMode, flatness);

    /// <summary>
    /// Maps the source rectangle onto the quadrilateral of the destination
    /// points (upper-left, upper-right, lower-left and, if given, lower-right),
    /// after flattening the path so that its curves bend with the mapping.
    /// </summary>
    public void Warp(ReadOnlySpan<PointF> destPoints, RectangleF srcRect, Matrix? matrix = null, WarpMode warpMode = WarpMode.Perspective, float flatness = 0.25f)
    {
        if (destPoints.Length is not (3 or 4) || srcRect.Width == 0 || srcRect.Height == 0)
        {
            throw new ArgumentException("Parameter is not valid.");
        }

        if (matrix is not null) Transform(matrix);

        PathGeometry.Flatten(_points, _types, flatness);

        var topLeft = destPoints[0];
        var topRight = destPoints[1];
        var bottomLeft = destPoints[2];

        // Three points describe a parallelogram: the fourth corner follows.
        var bottomRight = destPoints.Length == 4
            ? destPoints[3]
            : new PointF(topRight.X + bottomLeft.X - topLeft.X, topRight.Y + bottomLeft.Y - topLeft.Y);

        var perspective = SKMatrix.Identity;
        if (warpMode == WarpMode.Perspective)
        {
            perspective = QuadrilateralMapping(srcRect, topLeft, topRight, bottomLeft, bottomRight);
        }

        for (var i = 0; i < _points.Count; i++)
        {
            var u = (_points[i].X - srcRect.X) / srcRect.Width;
            var v = (_points[i].Y - srcRect.Y) / srcRect.Height;

            if (warpMode == WarpMode.Perspective)
            {
                var mapped = perspective.MapPoint(_points[i].X, _points[i].Y);
                _points[i] = new PointF(mapped.X, mapped.Y);
            }
            else
            {
                var top = Lerp(topLeft, topRight, u);
                var bottom = Lerp(bottomLeft, bottomRight, u);
                _points[i] = Lerp(top, bottom, v);
            }
        }
    }

    /// <summary>
    /// Reverses the order of the points, and of the figures. Each point's type
    /// describes the segment that reaches it, so the types move one place
    /// along with their segments and every figure still begins with Start.
    /// </summary>
    public void Reverse()
    {
        if (_points.Count == 0) return;

        var figures = Figures();
        var points = new List<PointF>(_points.Count);
        var types = new List<byte>(_types.Count);

        for (var f = figures.Count - 1; f >= 0; f--)
        {
            var (first, last) = figures[f];
            var closed = (_types[last] & PathGeometry.Close) != 0;

            for (var i = last; i >= first; i--)
            {
                points.Add(_points[i]);

                // The segment now arriving at point i is the one that used to
                // arrive at point i + 1.
                var type = i == last ? PathGeometry.Start : (byte)(_types[i + 1] & PathGeometry.TypeMask);
                var marker = (byte)(_types[i] & PathGeometry.Marker);
                types.Add((byte)(type | marker));
            }

            if (closed) types[^1] |= PathGeometry.Close;
        }

        _points.Clear();
        _points.AddRange(points);
        _types.Clear();
        _types.AddRange(types);
    }

    // --- Questions -------------------------------------------------------

    public RectangleF GetBounds() => GetBounds(null, null);

    public RectangleF GetBounds(Matrix? matrix) => GetBounds(matrix, null);

    /// <summary>
    /// The smallest rectangle around the path, after the matrix, and around
    /// what the pen would paint when one is given.
    /// </summary>
    /// <remarks>
    /// Tight around the curves rather than around their control points; GDI+
    /// documents the result with a pen as a loose fit, and how loose has not
    /// been compared with Windows.
    /// </remarks>
    public RectangleF GetBounds(Matrix? matrix, Pen? pen)
    {
        if (_points.Count == 0) return RectangleF.Empty;

        using var path = ToSkia();
        if (matrix is not null) path.Transform(matrix.Value.ToSkia());

        SKRect bounds;
        if (pen is null)
        {
            bounds = path.TightBounds;
        }
        else
        {
            using var outline = Outline(pen, path);
            bounds = outline.TightBounds;
        }

        return RectangleF.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
    }

    public bool IsVisible(Point point) => IsVisible((float)point.X, point.Y);

    public bool IsVisible(Point pt, Graphics? graphics) => IsVisible((float)pt.X, pt.Y);

    public bool IsVisible(PointF point) => IsVisible(point.X, point.Y);

    public bool IsVisible(PointF pt, Graphics? graphics) => IsVisible(pt.X, pt.Y);

    public bool IsVisible(int x, int y) => IsVisible((float)x, y);

    public bool IsVisible(int x, int y, Graphics? graphics) => IsVisible((float)x, y);

    public bool IsVisible(float x, float y, Graphics? graphics) => IsVisible(x, y);

    /// <summary>Whether the point is inside the path, under its fill mode.</summary>
    /// <remarks>
    /// The Graphics of the other overloads is only a hint to GDI+ about the
    /// device resolution; the path's coordinates are the answer either way.
    /// </remarks>
    public bool IsVisible(float x, float y)
    {
        using var path = ToSkia();

        return path.Contains(x, y);
    }

    public bool IsOutlineVisible(Point point, Pen pen) => IsOutlineVisible((float)point.X, point.Y, pen);

    public bool IsOutlineVisible(Point pt, Pen pen, Graphics? graphics) => IsOutlineVisible((float)pt.X, pt.Y, pen);

    public bool IsOutlineVisible(PointF point, Pen pen) => IsOutlineVisible(point.X, point.Y, pen);

    public bool IsOutlineVisible(PointF pt, Pen pen, Graphics? graphics) => IsOutlineVisible(pt.X, pt.Y, pen);

    public bool IsOutlineVisible(int x, int y, Pen pen) => IsOutlineVisible((float)x, y, pen);

    public bool IsOutlineVisible(int x, int y, Pen pen, Graphics? graphics) => IsOutlineVisible((float)x, y, pen);

    public bool IsOutlineVisible(float x, float y, Pen pen, Graphics? graphics) => IsOutlineVisible(x, y, pen);

    /// <summary>Whether the pen, drawing this path, would paint the point.</summary>
    public bool IsOutlineVisible(float x, float y, Pen pen)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var outline = Outline(pen);

        return outline.Contains(x, y);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }

    /// <summary>The path as Skia draws it. The caller disposes it.</summary>
    internal SKPath ToSkia() => PathGeometry.ToSkia(_points, _types, FillMode);

    // --- Building blocks -------------------------------------------------

    /// <summary>
    /// The first point of a line, curve or arc: it opens a figure, or joins
    /// the open one with a straight line, unless it is where the figure
    /// already stands.
    /// </summary>
    private void Continue(PointF point)
    {
        if (!_newFigure && _points.Count > 0 && _points[^1] == point) return;

        Append(point, PathGeometry.Line);
    }

    private void Append(PointF point, byte type)
    {
        if (_newFigure)
        {
            type = PathGeometry.Start;
            _newFigure = false;
        }

        _points.Add(point);
        _types.Add(type);
    }

    /// <summary>A figure of its own, closed: rectangles, ellipses, polygons.</summary>
    private void AddFigure(PointF first, IReadOnlyList<PointF> rest, byte type)
    {
        StartFigure();
        Append(first, PathGeometry.Line);

        foreach (var point in rest)
        {
            Append(point, type);
        }

        CloseFigure();
    }

    /// <summary>The first and last index of every figure.</summary>
    internal List<(int First, int Last)> Figures()
    {
        var figures = new List<(int, int)>();
        var first = 0;

        for (var i = 1; i <= _types.Count; i++)
        {
            if (i == _types.Count || (_types[i] & PathGeometry.TypeMask) == PathGeometry.Start)
            {
                figures.Add((first, i - 1));
                first = i;
            }
        }

        return figures;
    }

    internal IReadOnlyList<byte> Types => _types;

    internal IReadOnlyList<PointF> Points => _points;

    private SKPath Outline(Pen pen)
    {
        using var path = ToSkia();

        return Outline(pen, path);
    }

    /// <summary>The area a pen covers along a path, as a path to fill.</summary>
    private static SKPath Outline(Pen pen, SKPath path) => PenGeometry.Outline(path, pen);

    private static SKMatrix QuadrilateralMapping(RectangleF source, PointF topLeft, PointF topRight, PointF bottomLeft, PointF bottomRight)
    {
        // Skia maps four points onto four points with a perspective matrix.
        var from = new[]
        {
            new SKPoint(source.Left, source.Top), new SKPoint(source.Right, source.Top),
            new SKPoint(source.Right, source.Bottom), new SKPoint(source.Left, source.Bottom),
        };
        var to = new[]
        {
            new SKPoint(topLeft.X, topLeft.Y), new SKPoint(topRight.X, topRight.Y),
            new SKPoint(bottomRight.X, bottomRight.Y), new SKPoint(bottomLeft.X, bottomLeft.Y),
        };

        return PerspectiveFromQuads(from, to);
    }

    private static SKMatrix PerspectiveFromQuads(SKPoint[] from, SKPoint[] to)
    {
        var toUnit = UnitSquareTo(from);
        var fromUnit = UnitSquareTo(to);

        if (!toUnit.TryInvert(out var inverse)) throw new ArgumentException("Parameter is not valid.");

        return SKMatrix.Concat(fromUnit, inverse);
    }

    /// <summary>The projective map that sends the unit square to four points (Heckbert's construction).</summary>
    private static SKMatrix UnitSquareTo(SKPoint[] q)
    {
        float x0 = q[0].X, y0 = q[0].Y, x1 = q[1].X, y1 = q[1].Y, x2 = q[2].X, y2 = q[2].Y, x3 = q[3].X, y3 = q[3].Y;
        var sx = x0 - x1 + x2 - x3;
        var sy = y0 - y1 + y2 - y3;

        if (Math.Abs(sx) < 1e-6f && Math.Abs(sy) < 1e-6f)
        {
            return new SKMatrix(x1 - x0, x2 - x1, x0, y1 - y0, y2 - y1, y0, 0, 0, 1);
        }

        var dx1 = x1 - x2;
        var dx2 = x3 - x2;
        var dy1 = y1 - y2;
        var dy2 = y3 - y2;
        var det = dx1 * dy2 - dx2 * dy1;
        var g = (sx * dy2 - dx2 * sy) / det;
        var h = (dx1 * sy - sx * dy1) / det;

        return new SKMatrix(
            x1 - x0 + g * x1, x3 - x0 + h * x3, x0,
            y1 - y0 + g * y1, y3 - y0 + h * y3, y0,
            g, h, 1);
    }

    private static PointF Lerp(PointF a, PointF b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static T[] Required<T>(T[] array, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(array))] string? name = null) =>
        array ?? throw new ArgumentNullException(name);

    private static PointF[] ToPointF(ReadOnlySpan<Point> points)
    {
        var converted = new PointF[points.Length];

        for (var i = 0; i < points.Length; i++)
        {
            converted[i] = points[i];
        }

        return converted;
    }
}
