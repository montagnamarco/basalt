using System.Numerics;
using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// A brush whose colour runs from a centre point out to the edge of a shape.
/// </summary>
/// <remarks>
/// GDI+ colours each point by where it lies on the ray from the centre to the
/// edge: the centre colour at the centre, the surround colour of the edge at
/// the edge, Blend or InterpolationColors in between. Drawn here as a fan of
/// triangles from the centre to each edge segment, with colours at their
/// corners. Without a Blend, the colour along every ray inside a triangle
/// changes linearly, as GDI+'s does; a Blend or InterpolationColors is
/// followed with 32 rings of triangles, an approximation of its curve.
///
/// The fan covers the whole shape even when it is concave or the centre lies
/// outside it: the ray from the centre through any inside point leaves the
/// shape through some edge beyond that point, and the point lies in that
/// edge's triangle. Where triangles overlap, the last one drawn shows; which
/// edge GDI+ takes there has not been compared with Windows.
/// The fan is recorded once into a picture, which the paint reads as a shader;
/// outside the shape the picture is transparent, or tiled by WrapMode.
///
/// Not compared with Windows: the default colours (centre black, surround
/// white) and how the centre point is placed by default (the centroid of the
/// shape's area).
/// </remarks>
public sealed class PathGradientBrush : Brush
{
    // Rings between the edge and the focus when a Blend or InterpolationColors
    // bends the colour: enough for the bend to read as a curve.
    private const int CurvedRings = 32;

    private readonly List<PointF[]> _figures;
    private Color[] _surroundColors = [Color.White];
    private Blend? _blend;
    private ColorBlend? _interpolationColors;
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public PathGradientBrush(PointF[] points)
        : this(points, WrapMode.Clamp)
    {
    }

    public PathGradientBrush(PointF[] points, WrapMode wrapMode)
        : this(wrapMode, (ReadOnlySpan<PointF>)(points ?? throw new ArgumentNullException(nameof(points))))
    {
    }

    public PathGradientBrush(Point[] points)
        : this(points, WrapMode.Clamp)
    {
    }

    public PathGradientBrush(Point[] points, WrapMode wrapMode)
        : this(wrapMode, (ReadOnlySpan<PointF>)Array.ConvertAll(points ?? throw new ArgumentNullException(nameof(points)), point => (PointF)point))
    {
    }

    public PathGradientBrush(ReadOnlySpan<PointF> points)
        : this(WrapMode.Clamp, points)
    {
    }

    public PathGradientBrush(ReadOnlySpan<Point> points)
        : this(WrapMode.Clamp, points)
    {
    }

    public PathGradientBrush(WrapMode wrapMode, ReadOnlySpan<Point> points)
        : this(wrapMode, (ReadOnlySpan<PointF>)ToPointF(points))
    {
    }

    public PathGradientBrush(WrapMode wrapMode, ReadOnlySpan<PointF> points)
    {
        if (points.Length < 2) throw new OutOfMemoryException();

        _figures = [points.ToArray()];
        WrapMode = wrapMode;
        CenterPoint = Centroid(_figures);
    }

    public PathGradientBrush(GraphicsPath path)
    {
        ArgumentNullException.ThrowIfNull(path);

        using var flat = (GraphicsPath)path.Clone();
        flat.Flatten();

        var points = flat.PathPoints;
        _figures = flat.Figures()
            .Select(figure => points[figure.First..(figure.Last + 1)])
            .Where(figure => figure.Length >= 2)
            .ToList();

        if (_figures.Count == 0) throw new OutOfMemoryException();

        CenterPoint = Centroid(_figures);
    }

    public Color CenterColor { get; set; } = Color.Black;

    public PointF CenterPoint { get; set; }

    public PointF FocusScales { get; set; }

    public WrapMode WrapMode { get; set; } = WrapMode.Clamp;

    public RectangleF Rectangle
    {
        get
        {
            var all = _figures.SelectMany(figure => figure).ToList();
            var left = all.Min(point => point.X);
            var top = all.Min(point => point.Y);

            return RectangleF.FromLTRB(left, top, all.Max(point => point.X), all.Max(point => point.Y));
        }
    }

    /// <summary>One colour per boundary point; points beyond the last colour take it.</summary>
    public Color[] SurroundColors
    {
        get => (Color[])_surroundColors.Clone();
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Length == 0 || value.Length > PointCount) throw new ArgumentException("Parameter is not valid.", nameof(value));

            _surroundColors = (Color[])value.Clone();
        }
    }

    // Both copied in and out, as GDI+ copies them: a Blend changed after it was
    // given, or one read and then changed, must not change the brush.
    public Blend Blend
    {
        get => _blend is null ? new Blend { Factors = [1f], Positions = [0f] } : Copy(_blend);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Factors.Length != value.Positions.Length || value.Factors.Length == 0) throw new ArgumentException("Parameter is not valid.", nameof(value));

            _blend = Copy(value);
            _interpolationColors = null;
        }
    }

    public ColorBlend InterpolationColors
    {
        get => _interpolationColors is null ? new ColorBlend() : Copy(_interpolationColors);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (value.Colors.Length != value.Positions.Length || value.Colors.Length < 2) throw new ArgumentException("Parameter is not valid.", nameof(value));

            _interpolationColors = Copy(value);
            _blend = null;
        }
    }

    private static Blend Copy(Blend blend) =>
        new(blend.Factors.Length) { Factors = (float[])blend.Factors.Clone(), Positions = (float[])blend.Positions.Clone() };

    private static ColorBlend Copy(ColorBlend blend) =>
        new(blend.Colors.Length) { Colors = (Color[])blend.Colors.Clone(), Positions = (float[])blend.Positions.Clone() };

    public Matrix Transform
    {
        get => new(_transform);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _transform = value.Value;
        }
    }

    public void ResetTransform() => _transform = Matrix3x2.Identity;

    public void MultiplyTransform(Matrix matrix) => MultiplyTransform(matrix, MatrixOrder.Prepend);

    public void MultiplyTransform(Matrix matrix, MatrixOrder order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _transform = MatrixInterop.Combine(_transform, matrix.Value, order);
    }

    public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

    public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateTranslation(dx, dy), order);

    public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

    public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateScale(sx, sy), order);

    public void RotateTransform(float angle) => RotateTransform(angle, MatrixOrder.Prepend);

    public void RotateTransform(float angle, MatrixOrder order)
    {
        using var rotation = new Matrix();
        rotation.Rotate(angle);

        _transform = MatrixInterop.Combine(_transform, rotation.Value, order);
    }

    /// <summary>The colour rises from the edge to scale at focus, then falls back to the edge colour at the centre.</summary>
    public void SetBlendTriangularShape(float focus) => SetBlendTriangularShape(focus, 1f);

    public void SetBlendTriangularShape(float focus, float scale)
    {
        CheckFraction(focus, nameof(focus));
        CheckFraction(scale, nameof(scale));

        Blend = focus switch
        {
            0f => new Blend(2) { Positions = [0f, 1f], Factors = [scale, 0f] },
            1f => new Blend(2) { Positions = [0f, 1f], Factors = [0f, scale] },
            _ => new Blend(3) { Positions = [0f, focus, 1f], Factors = [0f, scale, 0f] },
        };
    }

    /// <summary>
    /// A bell-shaped rise to scale at focus. GDI+ uses a sigma curve; this is a
    /// cosine bell of the same reach, not compared with Windows.
    /// </summary>
    public void SetSigmaBellShape(float focus) => SetSigmaBellShape(focus, 1f);

    public void SetSigmaBellShape(float focus, float scale)
    {
        CheckFraction(focus, nameof(focus));
        CheckFraction(scale, nameof(scale));

        const int Samples = 33;
        var positions = new float[Samples];
        var factors = new float[Samples];

        for (var i = 0; i < Samples; i++)
        {
            var t = i / (float)(Samples - 1);
            var half = t < focus ? focus : 1f - focus;
            var distance = half <= 0f ? 0f : MathF.Abs(t - focus) / half;

            positions[i] = t;
            factors[i] = scale * 0.5f * (1f + MathF.Cos(MathF.PI * MathF.Min(distance, 1f)));
        }

        Blend = new Blend(Samples) { Positions = positions, Factors = factors };
    }

    public override object Clone()
    {
        var clone = (PathGradientBrush)MemberwiseClone();
        clone._surroundColors = (Color[])_surroundColors.Clone();

        return clone;
    }

    internal override void ApplyTo(SKPaint paint)
    {
        var bounds = Rectangle;
        var tile = new SKRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(tile);
        DrawFan(canvas);
        using var picture = recorder.EndRecording();

        var (tileX, tileY) = WrapMode switch
        {
            WrapMode.Tile => (SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
            WrapMode.TileFlipX => (SKShaderTileMode.Mirror, SKShaderTileMode.Repeat),
            WrapMode.TileFlipY => (SKShaderTileMode.Repeat, SKShaderTileMode.Mirror),
            WrapMode.TileFlipXY => (SKShaderTileMode.Mirror, SKShaderTileMode.Mirror),

            // Outside the shape a clamped path gradient paints nothing.
            _ => (SKShaderTileMode.Decal, SKShaderTileMode.Decal),
        };

        paint.Color = SKColors.White;
        // A picture shader puts the corner of its tile at the origin: measured,
        // a shape at (50, 50) had its centre colour 50 units up and left. The
        // translation puts the picture back where it was recorded, then the
        // brush's own transform applies.
        var local = SKMatrix.CreateTranslation(tile.Left, tile.Top).PostConcat(_transform.ToSkia());
        // Read without filtering: Skia redraws the picture at the device's own
        // resolution, and a linear filter, reading half a pixel off because of
        // the pixel-grid tie-break, mixed each pixel with the row above it,
        // which along the fan's outer edge is empty (measured: alpha 135 at
        // the inner corner of an L shape).
        paint.Shader = picture.ToShader(tileX, tileY, SKFilterMode.Nearest, local, tile);
    }

    private int PointCount => _figures.Sum(figure => figure.Length);

    private void DrawFan(SKCanvas canvas)
    {
        var rings = _blend is null && _interpolationColors is null ? 1 : CurvedRings;
        var positions = new List<SKPoint>();
        var colors = new List<SKColor>();
        var centre = new Vector2(CenterPoint.X, CenterPoint.Y);
        var focus = new Vector2(FocusScales.X, FocusScales.Y);
        var surroundIndex = 0;

        foreach (var figure in _figures)
        {
            for (var i = 0; i < figure.Length; i++)
            {
                var j = (i + 1) % figure.Length;
                var start = new Vector2(figure[i].X, figure[i].Y);
                var end = new Vector2(figure[j].X, figure[j].Y);
                var startColour = SurroundAt(surroundIndex + i);
                var endColour = SurroundAt(surroundIndex + j);

                // Where the centre colour begins: the edge scaled towards the centre.
                var startFocus = centre + (start - centre) * focus;
                var endFocus = centre + (end - centre) * focus;

                for (var ring = 0; ring < rings; ring++)
                {
                    var outer = ring / (float)rings;
                    var inner = (ring + 1) / (float)rings;

                    AddTriangle(positions, colors,
                        (Vector2.Lerp(start, startFocus, outer), ColourAt(outer, startColour)),
                        (Vector2.Lerp(end, endFocus, outer), ColourAt(outer, endColour)),
                        (Vector2.Lerp(end, endFocus, inner), ColourAt(inner, endColour)));
                    AddTriangle(positions, colors,
                        (Vector2.Lerp(start, startFocus, outer), ColourAt(outer, startColour)),
                        (Vector2.Lerp(end, endFocus, inner), ColourAt(inner, endColour)),
                        (Vector2.Lerp(start, startFocus, inner), ColourAt(inner, startColour)));
                }

                // Inside the focus, the centre colour alone.
                var middle = ColourAt(1f, startColour);
                AddTriangle(positions, colors, (startFocus, middle), (endFocus, middle), (centre, middle));
            }

            surroundIndex += figure.Length;
        }

        using var paint = new SKPaint { IsAntialias = false };
        using var vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions.ToArray(), null, colors.ToArray());
        canvas.DrawVertices(vertices, SKBlendMode.Dst, paint);
    }

    private static void AddTriangle(List<SKPoint> positions, List<SKColor> colors, (Vector2 At, SKColor Colour) a, (Vector2 At, SKColor Colour) b, (Vector2 At, SKColor Colour) c)
    {
        positions.Add(new SKPoint(a.At.X, a.At.Y));
        positions.Add(new SKPoint(b.At.X, b.At.Y));
        positions.Add(new SKPoint(c.At.X, c.At.Y));
        colors.Add(a.Colour);
        colors.Add(b.Colour);
        colors.Add(c.Colour);
    }

    private Color SurroundAt(int index) => _surroundColors[Math.Min(index, _surroundColors.Length - 1)];

    /// <summary>
    /// The colour at a fraction of the way from the edge (0) to the focus (1).
    /// </summary>
    private SKColor ColourAt(float fraction, Color surround)
    {
        if (_interpolationColors is { } stops) return Lookup(stops.Positions, stops.Colors, fraction);

        var factor = _blend is { } blend ? Interpolate(blend.Positions, blend.Factors, fraction) : fraction;

        return Lerp(surround, CenterColor, factor);
    }

    private static SKColor Lookup(float[] positions, Color[] colours, float fraction)
    {
        for (var i = 0; i + 1 < positions.Length; i++)
        {
            if (fraction <= positions[i + 1])
            {
                var span = positions[i + 1] - positions[i];
                var t = span <= 0 ? 0 : (fraction - positions[i]) / span;

                return Lerp(colours[i], colours[i + 1], t);
            }
        }

        return colours[^1].ToSkia();
    }

    private static float Interpolate(float[] positions, float[] values, float fraction)
    {
        if (positions.Length == 1) return values[0];

        for (var i = 0; i + 1 < positions.Length; i++)
        {
            if (fraction <= positions[i + 1])
            {
                var span = positions[i + 1] - positions[i];
                var t = span <= 0 ? 0 : (fraction - positions[i]) / span;

                return values[i] + (values[i + 1] - values[i]) * t;
            }
        }

        return values[^1];
    }

    private static SKColor Lerp(Color from, Color to, float t) => new(
        (byte)MathF.Round(from.R + (to.R - from.R) * t),
        (byte)MathF.Round(from.G + (to.G - from.G) * t),
        (byte)MathF.Round(from.B + (to.B - from.B) * t),
        (byte)MathF.Round(from.A + (to.A - from.A) * t));

    /// <summary>The centre of the shape's area; the average of its points when it has none.</summary>
    private static PointF Centroid(List<PointF[]> figures)
    {
        double area = 0;
        double x = 0;
        double y = 0;

        foreach (var figure in figures)
        {
            for (var i = 0; i < figure.Length; i++)
            {
                var a = figure[i];
                var b = figure[(i + 1) % figure.Length];
                var cross = (double)a.X * b.Y - (double)b.X * a.Y;

                area += cross;
                x += (a.X + b.X) * cross;
                y += (a.Y + b.Y) * cross;
            }
        }

        if (Math.Abs(area) < 1e-9)
        {
            var all = figures.SelectMany(figure => figure).ToList();

            return new PointF(all.Average(point => point.X), all.Average(point => point.Y));
        }

        return new PointF((float)(x / (3 * area)), (float)(y / (3 * area)));
    }

    private static void CheckFraction(float value, string name)
    {
        if (value < 0f || value > 1f) throw new ArgumentException("Parameter is not valid.", name);
    }

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
