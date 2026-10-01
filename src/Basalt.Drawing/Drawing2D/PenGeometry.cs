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
    /// DashCap and StartCap/EndCap are independent knobs in GDI+ (a dashed
    /// line can have round dash caps but flat line ends); SKPaint has one
    /// StrokeCap for both dash segments and line ends. While dashing, DashCap
    /// wins, since it is what most of the line's length shows; a solid line
    /// uses StartCap (EndCap is not reproduced separately either).
    /// </remarks>
    public static SKPaint StrokePaint(Pen pen)
    {
        if (pen.Alignment == PenAlignment.Inset) throw new NotImplementedException("Pen.Alignment.Inset is not implemented.");
        if (pen.CompoundArray.Length > 0) throw new NotImplementedException("Pen.CompoundArray is not implemented.");
        if (pen.StartCap == LineCap.Custom || pen.EndCap == LineCap.Custom) throw new NotImplementedException("Pen.CustomStartCap and Pen.CustomEndCap are not implemented.");

        static SKStrokeCap CapOfLine(LineCap cap) => cap switch
        {
            LineCap.Flat or LineCap.NoAnchor => SKStrokeCap.Butt,
            LineCap.Square or LineCap.SquareAnchor => SKStrokeCap.Square,
            LineCap.Round or LineCap.RoundAnchor => SKStrokeCap.Round,

            // Skia has no triangular cap; a round one is the closest it has,
            // and the anchor decorations (diamond, arrow) fall back to it too.
            LineCap.Triangle or LineCap.DiamondAnchor or LineCap.ArrowAnchor => SKStrokeCap.Round,
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
            StrokeCap = pen.DashStyle == DashStyle.Solid ? CapOfLine(pen.StartCap) : CapOfDash(pen.DashCap),
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

}
