using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// What a pen covers: width, joins, caps and dashes as a Skia stroke. Shared by
/// drawing and by the questions a path asks without drawing (Widen,
/// IsOutlineVisible, GetBounds with a pen), so the two cannot disagree.
/// </summary>
internal static class PenGeometry
{
    /// <summary>A stroke paint with the pen's geometry; colour, smoothing and blending are the caller's.</summary>
    /// <remarks>
    /// Alignment.Inset strokes inside the shape's own outline, which needs
    /// the path being stroked to clip against; this method only ever sees the
    /// pen, so it cannot be made correct here and says so instead of drawing
    /// Center and pretending it asked for Inset. Left, Right and Outset are,
    /// like real GDI+, the same as Center for a stroke.
    ///
    /// Pen.Transform scales the stroke, not the path it draws (see Pen.Transform):
    /// the one number an SKPaint can carry for that is its stroke width, so
    /// only the transform's isotropic scale (the geometric mean of its two
    /// axis scales) is applied; a skewed or rotated pen transform, which would
    /// need the actual path to render as GDI+'s elliptical nib does, is not
    /// reproduced.
    ///
    /// The dashes take DashCap. The two ends of an open line take StartCap and
    /// EndCap: Skia's own cap when both are the same plain one, otherwise
    /// shapes that Outline adds at each end (see there).
    /// </remarks>
    public static SKPaint StrokePaint(Pen pen)
    {
        if (pen.Alignment == PenAlignment.Inset) throw new NotImplementedException("Pen.Alignment.Inset is not implemented.");
        if (pen.CompoundArray.Length > 0) throw new NotImplementedException("Pen.CompoundArray is not implemented.");
        static SKStrokeCap CapOfLine(LineCap cap) => cap switch
        {
            LineCap.Square => SKStrokeCap.Square,
            LineCap.Round => SKStrokeCap.Round,
            _ => SKStrokeCap.Butt,
        };

        static SKStrokeCap CapOfDash(DashCap cap) => cap switch
        {
            DashCap.Round => SKStrokeCap.Round,

            // Same absence of a triangular cap as above.
            DashCap.Triangle => SKStrokeCap.Round,
            _ => SKStrokeCap.Butt,
        };

        static SKStrokeJoin JoinOf(LineJoin join) => join switch
        {
            LineJoin.Bevel => SKStrokeJoin.Bevel,
            LineJoin.Round => SKStrokeJoin.Round,

            // MiterClipped only clips a miter once it exceeds MiterLimit,
            // which SKStrokeJoin.Miter with StrokeMiter set already does.
            _ => SKStrokeJoin.Miter,
        };

        // The geometric mean of the transform's two axis scales: see the Pen.Transform remark above.
        var m = pen.Transform.MatrixElements;
        var transformScale = MathF.Sqrt(MathF.Abs(m.M11 * m.M22 - m.M12 * m.M21));

        // A pen thinner than a pixel still draws a line one pixel wide.
        var strokeWidth = Math.Max(pen.Width, 1f) * transformScale;

        return new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth,
            StrokeJoin = JoinOf(pen.LineJoin),
            StrokeMiter = pen.MiterLimit,
            // Line ends that are not one of Skia's three plain caps, or that
            // differ between start and end, are added as geometry by Outline.
            StrokeCap = pen.DashStyle != DashStyle.Solid ? CapOfDash(pen.DashCap)
                : NeedsCapGeometry(pen) ? SKStrokeCap.Butt
                : CapOfLine(pen.StartCap),
            PathEffect = DashEffectOf(pen, strokeWidth),
        };
    }


    /// <summary>
    /// The dash-and-gap pattern Pen.DashStyle asks for, scaled from pen
    /// widths to device lengths, or null for a solid line. GDI+ accepts an
    /// odd-length custom dash pattern, still toggling on and off past the end
    /// of the array; Skia's CreateDash needs an even-length one, so an odd
    /// pattern is repeated once, which reaches the same toggle sequence after
    /// two passes through it.
    /// </summary>
    private static SKPathEffect? DashEffectOf(Pen pen, float strokeWidth)
    {
        if (pen.DashStyle == DashStyle.Solid) return null;

        var pattern = pen.DashPattern;
        if (pattern.Length % 2 != 0) pattern = [.. pattern, .. pattern];

        var intervals = new float[pattern.Length];
        for (var i = 0; i < pattern.Length; i++) intervals[i] = pattern[i] * strokeWidth;

        return SKPathEffect.CreateDash(intervals, pen.DashOffset * strokeWidth);
    }


    /// <summary>
    /// Whether the ends of an open line must be added as shapes: when they
    /// differ, or are not one of Skia's flat, square and round caps.
    /// </summary>
    public static bool NeedsCapGeometry(Pen pen) =>
        pen.StartCap != pen.EndCap || pen.StartCap is not (LineCap.Flat or LineCap.NoAnchor or LineCap.Square or LineCap.Round);

    /// <summary>
    /// The area the pen covers along a path, as one path to fill: the stroke,
    /// with each open figure's two ends capped as the pen says.
    /// </summary>
    /// <remarks>
    /// Filled once, so a pen that is not opaque does not grow darker where a
    /// cap overlaps the line. A custom cap's base inset shortens the line
    /// before it is stroked, so a filled arrow head is not crossed by it.
    ///
    /// Sizes not compared with Windows: Triangle reaches half a pen width past
    /// the end; the anchor caps are centred on the end and two pen widths
    /// across, ArrowAnchor being an arrow head two widths wide and long.
    /// </remarks>
    public static SKPath Outline(SKPath path, Pen pen)
    {
        using var paint = StrokePaint(pen);
        var outline = new SKPath();

        if (!NeedsCapGeometry(pen))
        {
            paint.GetFillPath(path, outline);
            return outline;
        }

        var width = paint.StrokeWidth;

        // A line of no length has no direction and SKPathMeasure skips it,
        // but its two caps are still drawn, back to back, as a dot or a mark.
        foreach (var point in PointFigures(path))
        {
            using var dummy = new SKPathMeasure();
            AddCap(outline, pen.StartCap, pen.CustomStartCapOrNull, point, new SKPoint(-1, 0), width, dummy, 0, atStart: true);
            AddCap(outline, pen.EndCap, pen.CustomEndCapOrNull, point, new SKPoint(1, 0), width, dummy, 0, atStart: false);
        }

        using var measure = new SKPathMeasure(path, forceClosed: false);

        do
        {
            var length = measure.Length;
            if (length <= 0) continue;

            using var contour = new SKPath();
            measure.GetSegment(0, length, contour, startWithMoveTo: true);

            if (measure.IsClosed)
            {
                contour.Close();
                Union(outline, Stroked(paint, contour));
                continue;
            }

            measure.GetPositionAndTangent(0, out var start, out var startTangent);
            measure.GetPositionAndTangent(length, out var end, out var endTangent);

            var startInset = Math.Min(InsetOf(pen.StartCap, pen.CustomStartCapOrNull) * width, length / 2);
            var endInset = Math.Min(InsetOf(pen.EndCap, pen.CustomEndCapOrNull) * width, length / 2);

            using var trimmed = new SKPath();
            measure.GetSegment(startInset, length - endInset, trimmed, startWithMoveTo: true);
            Union(outline, Stroked(paint, trimmed));

            // Outwards from the line at each end.
            var backwards = new SKPoint(-startTangent.X, -startTangent.Y);

            AddCap(outline, pen.StartCap, pen.CustomStartCapOrNull, start, backwards, width, measure, startInset, atStart: true);
            AddCap(outline, pen.EndCap, pen.CustomEndCapOrNull, end, endTangent, width, measure, length - endInset, atStart: false);
        }
        while (measure.NextContour());

        return outline;
    }

    /// <summary>The open figures whose points all coincide.</summary>
    private static List<SKPoint> PointFigures(SKPath path)
    {
        var found = new List<SKPoint>();
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        SKPoint? figure = null;
        var degenerate = false;
        var drew = false;
        SKPathVerb verb;

        void Finish()
        {
            if (figure is { } at && degenerate && drew) found.Add(at);
        }

        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            switch (verb)
            {
                case SKPathVerb.Move:
                    Finish();
                    figure = points[0];
                    degenerate = true;
                    drew = false;
                    break;

                case SKPathVerb.Close:
                    degenerate = false;
                    break;

                default:
                    drew = true;
                    var count = verb switch { SKPathVerb.Line => 2, SKPathVerb.Quad or SKPathVerb.Conic => 3, _ => 4 };
                    for (var k = 1; k < count; k++)
                    {
                        if (points[k] != figure) degenerate = false;
                    }

                    break;
            }
        }

        Finish();

        // A lone MoveTo draws nothing: only figures with a segment back to their own point count.
        return found;
    }

    private static float InsetOf(LineCap cap, CustomLineCap? custom) => cap switch
    {
        LineCap.Custom when custom is AdjustableArrowCap arrow => arrow.EffectiveInset * arrow.WidthScale,
        LineCap.Custom when custom is not null => custom.BaseInset * custom.WidthScale,
        LineCap.ArrowAnchor => 2f,
        _ => 0f,
    };

    private static void AddCap(SKPath outline, LineCap cap, CustomLineCap? custom, SKPoint at, SKPoint outwards, float width, SKPathMeasure measure, float trimmedAt, bool atStart)
    {
        if (cap == LineCap.Custom && custom is null) return;

        // The cap's +y axis along the outward direction, one unit per pen width.
        var angle = MathF.Atan2(outwards.Y, outwards.X) - MathF.PI / 2;
        var scale = width * (cap == LineCap.Custom ? custom!.WidthScale : 1f);
        var place = SKMatrix.CreateScale(scale, scale)
            .PostConcat(SKMatrix.CreateRotation(angle))
            .PostConcat(SKMatrix.CreateTranslation(at.X, at.Y));

        if (cap == LineCap.Custom)
        {
            // The base cap goes on the shortened line's own end.
            if (custom!.BaseCap is not (LineCap.Flat or LineCap.Custom) && measure.GetPositionAndTangent(trimmedAt, out var baseAt, out var tangent))
            {
                var baseOutwards = atStart ? new SKPoint(-tangent.X, -tangent.Y) : tangent;
                AddCap(outline, custom.BaseCap, null, baseAt, baseOutwards, width, measure, trimmedAt, atStart);
            }

            using var area = custom.Area(width);
            area.Transform(place);
            Union(outline, new SKPath(area));
            return;
        }

        using var shape = PlainCap(cap);
        if (shape is null) return;

        shape.Transform(place);
        Union(outline, new SKPath(shape));
    }

    /// <summary>The built-in caps, in cap space: the end at the origin, +y outwards, one unit per pen width.</summary>
    private static SKPath? PlainCap(LineCap cap)
    {
        var shape = new SKPath();

        switch (cap)
        {
            case LineCap.Square:
                shape.AddRect(new SKRect(-0.5f, 0, 0.5f, 0.5f));
                break;

            case LineCap.Round:
                shape.AddCircle(0, 0, 0.5f);
                break;

            case LineCap.Triangle:
                shape.MoveTo(-0.5f, 0);
                shape.LineTo(0, 0.5f);
                shape.LineTo(0.5f, 0);
                shape.Close();
                break;

            case LineCap.RoundAnchor:
                shape.AddCircle(0, 0, 1f);
                break;

            case LineCap.SquareAnchor:
                shape.AddRect(new SKRect(-1f, -1f, 1f, 1f));
                break;

            case LineCap.DiamondAnchor:
                shape.MoveTo(0, 1f);
                shape.LineTo(1f, 0);
                shape.LineTo(0, -1f);
                shape.LineTo(-1f, 0);
                shape.Close();
                break;

            case LineCap.ArrowAnchor:
                shape.MoveTo(0, 0);
                shape.LineTo(1f, -2f);
                shape.LineTo(-1f, -2f);
                shape.Close();
                break;

            default:
                shape.Dispose();
                return null;
        }

        return shape;
    }

    private static SKPath Stroked(SKPaint paint, SKPath path)
    {
        var stroked = new SKPath();
        paint.GetFillPath(path, stroked);

        return stroked;
    }

    /// <summary>Adds a shape to the outline, which takes ownership of it.</summary>
    private static void Union(SKPath outline, SKPath shape)
    {
        using (shape)
        {
            if (outline.IsEmpty)
            {
                outline.AddPath(shape);
                return;
            }

            using var united = outline.Op(shape, SKPathOp.Union)
                ?? throw new InvalidOperationException("The outline of the pen could not be combined with a cap.");

            outline.Reset();
            outline.AddPath(united);
        }
    }
}
