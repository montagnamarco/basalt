using System.Numerics;
using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>A brush that blends smoothly between two (or more) colours along a straight line.</summary>
public sealed class LinearGradientBrush : Brush
{
    private PointF _start;
    private PointF _end;
    private Color _color1;
    private Color _color2;
    private RectangleF _rectangle;
    private WrapMode _wrapMode = WrapMode.Tile;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private bool _gammaCorrection;

    // Null until a caller reaches for a custom falloff or a custom colour
    // stop list: a plain two-colour gradient needs neither.
    private Blend? _blend;
    private ColorBlend? _interpolationColors;

    public LinearGradientBrush(Point point1, Point point2, Color color1, Color color2)
        : this(new PointF(point1.X, point1.Y), new PointF(point2.X, point2.Y), color1, color2)
    {
    }

    public LinearGradientBrush(PointF point1, PointF point2, Color color1, Color color2)
    {
        if (point1 == point2) throw new ArgumentException("The two points of a linear gradient brush must be different.");

        _start = point1;
        _end = point2;
        _color1 = color1;
        _color2 = color2;
        _rectangle = BoundsOf(point1, point2);
    }

    public LinearGradientBrush(Rectangle rect, Color color1, Color color2, LinearGradientMode linearGradientMode)
        : this((RectangleF)rect, color1, color2, linearGradientMode)
    {
    }

    public LinearGradientBrush(Rectangle rect, Color color1, Color color2, float angle)
        : this((RectangleF)rect, color1, color2, angle)
    {
    }

    public LinearGradientBrush(Rectangle rect, Color color1, Color color2, float angle, bool isAngleScaleable)
        : this((RectangleF)rect, color1, color2, angle, isAngleScaleable)
    {
    }

    // Whether GDI+ scales the 45 degrees of the diagonal modes with a
    // rectangle that is not square has not been compared with Windows; on a
    // square, and for Horizontal and Vertical, the two readings agree.
    public LinearGradientBrush(RectangleF rect, Color color1, Color color2, LinearGradientMode linearGradientMode)
        : this(rect, color1, color2, AngleOf(linearGradientMode), isAngleScaleable: false)
    {
    }

    public LinearGradientBrush(RectangleF rect, Color color1, Color color2, float angle)
        : this(rect, color1, color2, angle, isAngleScaleable: false)
    {
    }

    public LinearGradientBrush(RectangleF rect, Color color1, Color color2, float angle, bool isAngleScaleable)
    {
        if (rect.Width == 0 || rect.Height == 0) throw new ArgumentException("Rectangle cannot have a width or height equal to 0.", nameof(rect));

        _color1 = color1;
        _color2 = color2;
        _rectangle = rect;
        (_start, _end) = GradientLineOf(rect, angle, isAngleScaleable);
    }

    public Color[] LinearColors
    {
        get => [_color1, _color2];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length != 2) throw new ArgumentException("LinearColors must have exactly 2 elements.", nameof(value));

            _color1 = value[0];
            _color2 = value[1];
        }
    }

    public RectangleF Rectangle => _rectangle;

    public Blend Blend
    {
        get => _blend ?? new Blend();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Factors.Length != value.Positions.Length) throw new ArgumentException("Blend.Factors and Blend.Positions must have the same length.", nameof(value));

            _blend = value;
        }
    }

    public ColorBlend InterpolationColors
    {
        get => _interpolationColors ?? new ColorBlend();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Colors.Length != value.Positions.Length) throw new ArgumentException("ColorBlend.Colors and ColorBlend.Positions must have the same length.", nameof(value));

            _interpolationColors = value;
        }
    }

    public WrapMode WrapMode
    {
        get => _wrapMode;
        set => _wrapMode = value;
    }

    /// <summary>
    /// Whether the gradient is meant to interpolate in a gamma-corrected
    /// (linear-light) colour space. Skia's linear gradient shader always
    /// interpolates the raw sRGB bytes, the same space GDI+ itself uses when
    /// this is false; there is no Skia knob for the true-gamma mode, so this
    /// flag is stored and reported back but never changes what is drawn.
    /// </summary>
    public bool GammaCorrection
    {
        get => _gammaCorrection;
        set => _gammaCorrection = value;
    }

    public Drawing2D.Matrix Transform
    {
        get => new(_transform);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _transform = value.Value;
        }
    }

    public void MultiplyTransform(Drawing2D.Matrix matrix) => MultiplyTransform(matrix, MatrixOrder.Prepend);

    public void MultiplyTransform(Drawing2D.Matrix matrix, MatrixOrder order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _transform = MatrixInterop.Combine(_transform, matrix.Value, order);
    }

    public void ResetTransform() => _transform = Matrix3x2.Identity;

    public void RotateTransform(float angle) => RotateTransform(angle, MatrixOrder.Prepend);

    public void RotateTransform(float angle, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateRotation(angle * MathF.PI / 180f), order);

    public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

    public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateScale(sx, sy), order);

    public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

    public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateTranslation(dx, dy), order);

    /// <summary>A symmetrical triangular falloff, rising from 0 at the start to scale at focus, back to 0 at the end.</summary>
    public void SetBlendTriangularShape(float focus) => SetBlendTriangularShape(focus, 1f);

    public void SetBlendTriangularShape(float focus, float scale)
    {
        if (focus < 0f || focus > 1f) throw new ArgumentException("Focus must be between 0 and 1.", nameof(focus));
        if (scale < 0f || scale > 1f) throw new ArgumentException("Scale must be between 0 and 1.", nameof(scale));

        // A degenerate focus (0 or 1) collapses the triangle to a two-point
        // ramp: a Positions array cannot repeat a value, so the middle point
        // that would sit on top of an end point is dropped instead.
        (float[] Positions, float[] Factors) points = focus switch
        {
            0f => ([0f, 1f], [scale, 0f]),
            1f => ([0f, 1f], [0f, scale]),
            _ => ([0f, focus, 1f], [0f, scale, 0f]),
        };

        _blend = new Blend(points.Positions.Length) { Positions = points.Positions, Factors = points.Factors };
    }

    /// <summary>
    /// A bell-shaped falloff peaking at scale at focus. GDI+ computes this
    /// from a closed-form sigma spline; this approximates the same shape with
    /// a cosine bell sampled at a fixed number of points, which has not been
    /// compared against Microsoft's exact curve.
    /// </summary>
    public void SetSigmaBellShape(float focus) => SetSigmaBellShape(focus, 1f);

    public void SetSigmaBellShape(float focus, float scale)
    {
        if (focus < 0f || focus > 1f) throw new ArgumentException("Focus must be between 0 and 1.", nameof(focus));
        if (scale < 0f || scale > 1f) throw new ArgumentException("Scale must be between 0 and 1.", nameof(scale));

        const int Samples = 11;
        var positions = new float[Samples];
        var factors = new float[Samples];
        for (var i = 0; i < Samples; i++)
        {
            var t = i / (float)(Samples - 1);
            positions[i] = t;

            // A half-cosine bump on each side of focus, 0 at both ends, scale at focus.
            var half = t < focus ? focus : 1f - focus;
            var distance = half <= 0f ? 0f : MathF.Abs(t - focus) / half;
            factors[i] = scale * 0.5f * (1f + MathF.Cos(MathF.PI * MathF.Min(distance, 1f)));
        }

        _blend = new Blend(Samples) { Positions = positions, Factors = factors };
    }

    public override object Clone()
    {
        var clone = new LinearGradientBrush(_start, _end, _color1, _color2)
        {
            _rectangle = _rectangle,
            _wrapMode = _wrapMode,
            _transform = _transform,
            _gammaCorrection = _gammaCorrection,
            _blend = _blend,
            _interpolationColors = _interpolationColors,
        };

        return clone;
    }

    internal override void ApplyTo(SKPaint paint)
    {
        var (colors, positions) = ColorStopsOf();
        var mode = _wrapMode switch
        {
            WrapMode.Clamp => SKShaderTileMode.Clamp,
            WrapMode.Tile => SKShaderTileMode.Repeat,

            // A linear gradient only varies along its own line, so "flip on X"
            // and "flip on Y" look the same as each other and as "flip on
            // both": Skia's Mirror tile mode is that one shape for all three.
            _ => SKShaderTileMode.Mirror,
        };

        paint.Color = SKColors.White;
        paint.Shader = SKShader.CreateLinearGradient(_start.ToSkia(), _end.ToSkia(), colors, positions, mode, _transform.ToSkia());
    }

    private (SKColor[] Colors, float[] Positions) ColorStopsOf()
    {
        if (_interpolationColors is not null)
        {
            return (Array.ConvertAll(_interpolationColors.Colors, c => c.ToSkia()), _interpolationColors.Positions);
        }

        if (_blend is not null)
        {
            var colors = new SKColor[_blend.Factors.Length];
            for (var i = 0; i < colors.Length; i++) colors[i] = LerpColor(_color1, _color2, _blend.Factors[i]);
            return (colors, _blend.Positions);
        }

        return ([_color1.ToSkia(), _color2.ToSkia()], [0f, 1f]);
    }

    private static SKColor LerpColor(Color a, Color b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t),
        (byte)(a.A + (b.A - a.A) * t));

    private static RectangleF BoundsOf(PointF a, PointF b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static float AngleOf(LinearGradientMode mode) => mode switch
    {
        LinearGradientMode.Horizontal => 0f,
        LinearGradientMode.Vertical => 90f,
        LinearGradientMode.ForwardDiagonal => 45f,
        LinearGradientMode.BackwardDiagonal => 135f,
        _ => 0f,
    };

    /// <summary>
    /// The two points on the rectangle's boundary that the gradient runs
    /// between, for the angle-based constructors. GDI+'s own geometry for
    /// this has not been confirmed against Windows; this projects the
    /// rectangle's corners onto the angle's direction and spans exactly that
    /// projection, which reproduces the documented behaviour at the four
    /// cardinal angles (color1 at the edge the angle points away from).
    /// </summary>
    private static (PointF Start, PointF End) GradientLineOf(RectangleF rect, float angle, bool isAngleScaleable)
    {
        var radians = angle * MathF.PI / 180f;

        // isAngleScaleable warps the angle to account for a non-square
        // rectangle, the same way GDI+ documents it: a 45 degree gradient on
        // a wide rectangle leans closer to horizontal.
        if (isAngleScaleable && rect.Width != rect.Height)
        {
            radians = MathF.Atan2(rect.Height * MathF.Sin(radians), rect.Width * MathF.Cos(radians));
        }

        var direction = new Vector2(MathF.Cos(radians), MathF.Sin(radians));
        var center = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);

        PointF[] corners =
        [
            new(rect.Left, rect.Top), new(rect.Right, rect.Top),
            new(rect.Left, rect.Bottom), new(rect.Right, rect.Bottom),
        ];

        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var corner in corners)
        {
            var projected = Vector2.Dot(new Vector2(corner.X, corner.Y) - center, direction);
            min = Math.Min(min, projected);
            max = Math.Max(max, projected);
        }

        var start = center + direction * min;
        var end = center + direction * max;
        return (new PointF(start.X, start.Y), new PointF(end.X, end.Y));
    }
}
