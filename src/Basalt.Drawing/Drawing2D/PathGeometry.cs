using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// The geometry GDI+ writes into a path: how an arc, an ellipse or a curve
/// becomes points and point types, and how those become a Skia path.
/// </summary>
/// <remarks>
/// An application reads PathPoints and PathTypes back, counts them and walks
/// them with GraphicsPathIterator, so the points are GDI+'s and not Skia's:
/// an ellipse is four Bézier segments in thirteen points, an arc is split into
/// segments of at most 90 degrees. The construction follows libgdiplus, Mono's
/// MIT-licensed GDI+ (graphics-path.c), whose tests were written against
/// Windows; where it could not be checked against Windows it says so.
/// </remarks>
internal static class PathGeometry
{
    public const byte Start = (byte)PathPointType.Start;
    public const byte Line = (byte)PathPointType.Line;
    public const byte Bezier = (byte)PathPointType.Bezier;
    public const byte TypeMask = (byte)PathPointType.PathTypeMask;
    public const byte Marker = (byte)PathPointType.PathMarker;
    public const byte Close = (byte)PathPointType.CloseSubpath;

    // The distance of a quarter circle's control points from its ends, as a
    // fraction of the radius: 4/3·(√2 − 1).
    private const float QuarterControl = 0.5522847498f;

    /// <summary>The four Bézier segments of an ellipse, starting and ending at its rightmost point.</summary>
    /// <remarks>
    /// Clockwise on the screen (down first), the direction in which GDI+ measures
    /// angles, so that an ellipse and a full arc run the same way. libgdiplus
    /// goes the other way; the direction decides how two overlapping ellipses
    /// fill with FillMode.Winding and has not been checked against Windows.
    /// </remarks>
    public static PointF[] Ellipse(float x, float y, float width, float height)
    {
        var rx = width / 2;
        var ry = height / 2;
        var cx = x + rx;
        var cy = y + ry;
        var kx = QuarterControl * rx;
        var ky = QuarterControl * ry;

        return
        [
            new(cx + rx, cy),
            new(cx + rx, cy + ky), new(cx + kx, cy + ry), new(cx, cy + ry),
            new(cx - kx, cy + ry), new(cx - rx, cy + ky), new(cx - rx, cy),
            new(cx - rx, cy - ky), new(cx - kx, cy - ry), new(cx, cy - ry),
            new(cx + kx, cy - ry), new(cx + rx, cy - ky), new(cx + rx, cy),
        ];
    }

    /// <summary>
    /// An arc as its first point followed by Bézier segments of three points
    /// each, none covering more than 90 degrees.
    /// </summary>
    /// <remarks>
    /// The angles are measured on the ellipse, not on the circle it was
    /// squashed from: GDI+ puts the point at 45 degrees of a wide ellipse where
    /// a ray at 45 degrees from the centre meets it.
    /// </remarks>
    public static List<PointF> Arc(float x, float y, float width, float height, float startAngle, float sweepAngle)
    {
        var points = new List<PointF>();

        // A sweep of more than a full turn draws the full turn once.
        sweepAngle = Math.Clamp(sweepAngle, -360f, 360f);

        var endAngle = startAngle + sweepAngle;
        var increment = endAngle < startAngle ? -90f : 90f;
        var drawn = 0f;

        for (var segment = 0; segment < 4; segment++)
        {
            var current = startAngle + drawn;
            var additional = endAngle - current;
            var last = Math.Abs(additional) <= 90f;

            if (!last) additional = increment;
            // No sweep, no arc: GDI+ adds no point at all.
            if (last && Math.Abs(additional) < 0.0001f) break;

            AppendArcSegment(points, segment == 0, x, y, width, height, current, current + additional);
            drawn += additional;

            if (last) break;
        }

        return points;
    }

    private static void AppendArcSegment(List<PointF> points, bool first, float x, float y, float width, float height, float startAngle, float endAngle)
    {
        var rx = width / 2;
        var ry = height / 2;
        var cx = x + rx;
        var cy = y + ry;

        var alpha = EllipticalAngle(startAngle, rx, ry);
        var beta = EllipticalAngle(endAngle, rx, ry);

        // atan2 answers in (−π, π]: bring the end back next to the start.
        if (Math.Abs(beta - alpha) > Math.PI)
        {
            if (beta > alpha) beta -= 2 * Math.PI;
            else alpha -= 2 * Math.PI;
        }

        var delta = beta - alpha;
        var control = Math.Abs(delta) < 1e-9 ? 0 : 4.0 / 3 * (1 - Math.Cos(delta / 2)) / Math.Sin(delta / 2);

        var (sinAlpha, cosAlpha) = Math.SinCos(alpha);
        var (sinBeta, cosBeta) = Math.SinCos(beta);

        if (first) points.Add(new((float)(cx + rx * cosAlpha), (float)(cy + ry * sinAlpha)));

        points.Add(new((float)(cx + rx * (cosAlpha - control * sinAlpha)), (float)(cy + ry * (sinAlpha + control * cosAlpha))));
        points.Add(new((float)(cx + rx * (cosBeta + control * sinBeta)), (float)(cy + ry * (sinBeta - control * cosBeta))));
        points.Add(new((float)(cx + rx * cosBeta), (float)(cy + ry * sinBeta)));
    }

    private static double EllipticalAngle(float degrees, float rx, float ry)
    {
        var radians = degrees * Math.PI / 180;

        return Math.Atan2(rx * Math.Sin(radians), ry * Math.Cos(radians));
    }

    /// <summary>
    /// The tangents of a cardinal spline: tension/3 times the vector between
    /// the neighbours of each point, one-sided at the ends of an open curve.
    /// </summary>
    public static PointF[] CurveTangents(ReadOnlySpan<PointF> points, float tension, bool closed)
    {
        var tangents = new PointF[points.Length];
        var coefficient = tension / 3f;

        // Two points make a straight line whatever the tension.
        if (points.Length <= 2 && !closed) return tangents;

        for (var i = 0; i < points.Length; i++)
        {
            int next;
            int previous;

            if (closed)
            {
                next = (i + 1) % points.Length;
                previous = (i + points.Length - 1) % points.Length;
            }
            else
            {
                next = Math.Min(i + 1, points.Length - 1);
                previous = Math.Max(i - 1, 0);
            }

            tangents[i] = new(coefficient * (points[next].X - points[previous].X), coefficient * (points[next].Y - points[previous].Y));
        }

        return tangents;
    }

    /// <summary>The three points of each Bézier segment from point <paramref name="first"/> onwards.</summary>
    public static void AppendCurveSegments(List<PointF> output, ReadOnlySpan<PointF> points, PointF[] tangents, int first, int segments, bool closed)
    {
        for (var i = first; i < first + segments; i++)
        {
            var next = closed ? (i + 1) % points.Length : i + 1;

            output.Add(new(points[i].X + tangents[i].X, points[i].Y + tangents[i].Y));
            output.Add(new(points[next].X - tangents[next].X, points[next].Y - tangents[next].Y));
            output.Add(points[next]);
        }
    }

    /// <summary>A GDI+ path as a Skia path, figure by figure.</summary>
    public static SKPath ToSkia(IReadOnlyList<PointF> points, IReadOnlyList<byte> types, FillMode fillMode)
    {
        var path = new SKPath { FillType = fillMode == FillMode.Winding ? SKPathFillType.Winding : SKPathFillType.EvenOdd };

        for (var i = 0; i < points.Count; i++)
        {
            var type = types[i];

            switch (type & TypeMask)
            {
                case Start:
                    path.MoveTo(points[i].X, points[i].Y);
                    break;

                case Bezier when i + 2 < points.Count:
                    path.CubicTo(points[i].X, points[i].Y, points[i + 1].X, points[i + 1].Y, points[i + 2].X, points[i + 2].Y);
                    i += 2;
                    type = types[i];
                    break;

                default:
                    path.LineTo(points[i].X, points[i].Y);
                    break;
            }

            if ((type & Close) != 0) path.Close();
        }

        return path;
    }

    /// <summary>
    /// A Skia path as GDI+ points and types. Skia's quadratic and conic
    /// segments, which GDI+ does not have, become cubic ones.
    /// </summary>
    public static void FromSkia(SKPath path, List<PointF> points, List<byte> types)
    {
        using var iterator = path.CreateRawIterator();
        var segment = new SKPoint[4];
        SKPathVerb verb;

        while ((verb = iterator.Next(segment)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    points.Add(new(segment[0].X, segment[0].Y));
                    types.Add(Start);
                    break;

                case SKPathVerb.Line:
                    points.Add(new(segment[1].X, segment[1].Y));
                    types.Add(Line);
                    break;

                case SKPathVerb.Quad:
                    AddQuadAsCubic(points, types, segment[0], segment[1], segment[2]);
                    break;

                case SKPathVerb.Conic:
                    foreach (var quad in ConicAsQuads(segment[0], segment[1], segment[2], iterator.ConicWeight()))
                    {
                        AddQuadAsCubic(points, types, quad.Start, quad.Control, quad.End);
                    }

                    break;

                case SKPathVerb.Cubic:
                    for (var k = 1; k <= 3; k++)
                    {
                        points.Add(new(segment[k].X, segment[k].Y));
                        types.Add(Bezier);
                    }

                    break;

                case SKPathVerb.Close:
                    if (types.Count > 0) types[^1] |= Close;
                    break;
            }
        }
    }

    private static void AddQuadAsCubic(List<PointF> points, List<byte> types, SKPoint start, SKPoint control, SKPoint end)
    {
        // A quadratic segment is the cubic whose controls sit two thirds of
        // the way from each end towards the quadratic's single control.
        points.Add(new(start.X + 2f / 3 * (control.X - start.X), start.Y + 2f / 3 * (control.Y - start.Y)));
        points.Add(new(end.X + 2f / 3 * (control.X - end.X), end.Y + 2f / 3 * (control.Y - end.Y)));
        points.Add(new(end.X, end.Y));
        types.Add(Bezier);
        types.Add(Bezier);
        types.Add(Bezier);
    }

    private static IEnumerable<(SKPoint Start, SKPoint Control, SKPoint End)> ConicAsQuads(SKPoint start, SKPoint control, SKPoint end, float weight)
    {
        var quads = SKPath.ConvertConicToQuads(start, control, end, weight, 2);

        for (var i = 0; i + 2 < quads.Length; i += 2)
        {
            yield return (quads[i], quads[i + 1], quads[i + 2]);
        }
    }

    /// <summary>
    /// Replaces every Bézier segment with lines no farther than
    /// <paramref name="flatness"/> from the curve. Markers and closings stay on
    /// the points that carried them.
    /// </summary>
    public static void Flatten(List<PointF> points, List<byte> types, float flatness)
    {
        var flatPoints = new List<PointF>(points.Count);
        var flatTypes = new List<byte>(types.Count);

        for (var i = 0; i < points.Count; i++)
        {
            if ((types[i] & TypeMask) == Bezier && i + 2 < points.Count && flatPoints.Count > 0)
            {
                var from = flatPoints[^1];
                var lines = new List<PointF>();
                Subdivide(from, points[i], points[i + 1], points[i + 2], Math.Max(flatness, 0.0001f), lines, depth: 0);

                // The flags of the segment's last point describe the end of the segment.
                var flags = (byte)(types[i + 2] & ~TypeMask);
                for (var k = 0; k < lines.Count; k++)
                {
                    flatPoints.Add(lines[k]);
                    flatTypes.Add(k == lines.Count - 1 ? (byte)(Line | flags) : Line);
                }

                i += 2;
                continue;
            }

            flatPoints.Add(points[i]);
            flatTypes.Add(types[i]);
        }

        points.Clear();
        points.AddRange(flatPoints);
        types.Clear();
        types.AddRange(flatTypes);
    }

    private static void Subdivide(PointF p0, PointF p1, PointF p2, PointF p3, float flatness, List<PointF> output, int depth)
    {
        // Flat enough when both controls lie within the tolerance of the chord.
        if (depth >= 16 || (DistanceToLine(p1, p0, p3) <= flatness && DistanceToLine(p2, p0, p3) <= flatness))
        {
            output.Add(p3);
            return;
        }

        var p01 = Mid(p0, p1);
        var p12 = Mid(p1, p2);
        var p23 = Mid(p2, p3);
        var p012 = Mid(p01, p12);
        var p123 = Mid(p12, p23);
        var middle = Mid(p012, p123);

        Subdivide(p0, p01, p012, middle, flatness, output, depth + 1);
        Subdivide(middle, p123, p23, p3, flatness, output, depth + 1);
    }

    private static PointF Mid(PointF a, PointF b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    private static float DistanceToLine(PointF point, PointF a, PointF b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);

        if (length < 1e-6f) return MathF.Sqrt((point.X - a.X) * (point.X - a.X) + (point.Y - a.Y) * (point.Y - a.Y));

        return MathF.Abs(dy * point.X - dx * point.Y + b.X * a.Y - b.Y * a.X) / length;
    }
}
