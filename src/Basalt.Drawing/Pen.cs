using System.Drawing.Drawing2D;
using System.Numerics;

namespace System.Drawing;

/// <summary>What the outline of a shape is drawn with.</summary>
public sealed class Pen : MarshalByRefObject, ICloneable, IDisposable
{
    // True for the pens of Pens and SystemPens, which many callers share.
    private readonly bool _isShared;
    private Brush? _brush;
    private Color _color;
    private float _width;

    private PenAlignment _alignment = PenAlignment.Center;
    private float[] _compoundArray = [];
    private DashCap _dashCap = DashCap.Flat;
    private float _dashOffset;

    // Null while DashStyle is a preset (Solid, Dash, Dot, ...): the pattern is
    // then computed from the style itself. Only DashStyle.Custom, reached
    // either by DashStyle's setter or by setting DashPattern, keeps its own array.
    private float[]? _customDashPattern;
    private DashStyle _dashStyle = DashStyle.Solid;
    private LineCap _startCap = LineCap.Flat;
    private LineCap _endCap = LineCap.Flat;
    private LineJoin _lineJoin = LineJoin.Miter;
    private float _miterLimit = 10f;

    // Kept as the bare matrix, like Graphics.Transform: the getter wraps it in
    // a fresh Matrix each time, exactly as GDI+ hands out a copy.
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public Pen(Color color)
        : this(color, 1f)
    {
    }

    public Pen(Color color, float width)
    {
        _color = color;
        _width = width;
    }

    internal Pen(Color color, float width, bool isShared)
        : this(color, width)
    {
        _isShared = isShared;
    }

    public Pen(Brush brush)
        : this(brush, 1f)
    {
    }

    public Pen(Brush brush, float width)
    {
        ArgumentNullException.ThrowIfNull(brush);

        _brush = brush;
        _color = brush is SolidBrush solid ? solid.Color : Color.Black;
        _width = width;
    }

    ~Pen() => Dispose(false);

    public Color Color
    {
        get => _color;
        set
        {
            CheckNotShared();

            _color = value;

            // The colour is the pen's own from now on, not the brush it was made from.
            _brush = null;
        }
    }

    public float Width
    {
        get => _width;
        set
        {
            CheckNotShared();

            _width = value;
        }
    }

    public Brush Brush
    {
        get => _brush ?? new SolidBrush(_color);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            CheckNotShared();

            _brush = value;
            if (value is SolidBrush solid) _color = solid.Color;
        }
    }

    /// <summary>Where the pen's width sits relative to the outline it strokes.</summary>
    /// <remarks>
    /// GDI+ treats Left, Right and Outset the same as Center for a pen used to
    /// stroke a shape (only Inset moves the ink), so those three are accepted
    /// and simply behave like Center; Inset itself is refused by
    /// Graphics.Stroke, see the remark there.
    /// </remarks>
    public PenAlignment Alignment
    {
        get => _alignment;
        set
        {
            CheckNotShared();

            _alignment = value;
        }
    }

    /// <summary>
    /// The relative widths, each between 0 and 1, of the alternating opaque
    /// and empty bands a wide pen draws across its own width. Empty means "one
    /// solid band", GDI+'s default.
    /// </summary>
    public float[] CompoundArray
    {
        get => (float[])_compoundArray.Clone();
        set
        {
            CheckNotShared();
            ArgumentNullException.ThrowIfNull(value);

            if (value.Length > 0)
            {
                if (value.Length % 2 != 0) throw new ArgumentException("A compound array must have an even number of elements.", nameof(value));

                for (var i = 0; i < value.Length; i++)
                {
                    if (value[i] < 0f || value[i] > 1f) throw new ArgumentException("A compound array's elements must be between 0 and 1.", nameof(value));
                    if (i > 0 && value[i] <= value[i - 1]) throw new ArgumentException("A compound array's elements must increase.", nameof(value));
                }
            }

            _compoundArray = (float[])value.Clone();
        }
    }

    /// <summary>How the ends of each dash are capped, while DashStyle is not Solid.</summary>
    public DashCap DashCap
    {
        get => _dashCap;
        set
        {
            CheckNotShared();

            _dashCap = value;
        }
    }

    /// <summary>How far, in pen widths, the dash pattern is shifted along the line.</summary>
    public float DashOffset
    {
        get => _dashOffset;
        set
        {
            CheckNotShared();

            _dashOffset = value;
        }
    }

    /// <summary>
    /// The dash and gap lengths, in pen widths, that DashStyle currently draws
    /// with: the fixed arrays GDI+ uses for its five presets, or the array a
    /// caller set last for Custom.
    /// </summary>
    public float[] DashPattern
    {
        get => _dashStyle switch
        {
            DashStyle.Dash => [3f, 1f],
            DashStyle.Dot => [1f, 1f],
            DashStyle.DashDot => [3f, 1f, 1f, 1f],
            DashStyle.DashDotDot => [3f, 1f, 1f, 1f, 1f, 1f],
            DashStyle.Custom => (float[])(_customDashPattern ?? []).Clone(),

            // Solid: GDI+ has no dash array to report.
            _ => [],
        };
        set
        {
            CheckNotShared();
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length == 0) throw new ArgumentException("A dash pattern must have at least one element.", nameof(value));

            foreach (var length in value)
            {
                if (length <= 0f) throw new ArgumentException("A dash pattern's elements must be greater than 0.", nameof(value));
            }

            _customDashPattern = (float[])value.Clone();
            _dashStyle = DashStyle.Custom;
        }
    }

    public DashStyle DashStyle
    {
        get => _dashStyle;
        set
        {
            CheckNotShared();

            _dashStyle = value;

            // Switching away from Custom by way of DashStyle, not DashPattern,
            // leaves no custom array behind: GDI+ forgets it too.
            if (value != DashStyle.Custom) _customDashPattern = null;
        }
    }

    public LineCap StartCap
    {
        get => _startCap;
        set
        {
            CheckNotShared();

            _startCap = value;
        }
    }

    public LineCap EndCap
    {
        get => _endCap;
        set
        {
            CheckNotShared();

            _endCap = value;
        }
    }

    public LineJoin LineJoin
    {
        get => _lineJoin;
        set
        {
            CheckNotShared();

            _lineJoin = value;
        }
    }

    public float MiterLimit
    {
        get => _miterLimit;
        set
        {
            CheckNotShared();

            _miterLimit = value;
        }
    }

    /// <summary>What kind of brush this pen paints with: SolidColor unless a brush was given.</summary>
    public PenType PenType => Brush switch
    {
        Drawing2D.HatchBrush => PenType.HatchFill,
        TextureBrush => PenType.TextureFill,
        Drawing2D.LinearGradientBrush => PenType.LinearGradient,
        _ => PenType.SolidColor,
    };

    /// <summary>
    /// The transform of the pen's own space, applied to the stroke (its width
    /// and the pattern it is dashed or compounded with), never to the path it
    /// strokes: a rotated pen still draws along the same line, only thicker or
    /// thinner across it.
    /// </summary>
    public Drawing2D.Matrix Transform
    {
        get => new(_transform);
        set
        {
            CheckNotShared();
            ArgumentNullException.ThrowIfNull(value);

            _transform = value.Value;
        }
    }

    public void MultiplyTransform(Drawing2D.Matrix matrix) => MultiplyTransform(matrix, MatrixOrder.Prepend);

    public void MultiplyTransform(Drawing2D.Matrix matrix, MatrixOrder order)
    {
        CheckNotShared();
        ArgumentNullException.ThrowIfNull(matrix);

        _transform = MatrixInterop.Combine(_transform, matrix.Value, order);
    }

    public void ResetTransform()
    {
        CheckNotShared();

        _transform = Matrix3x2.Identity;
    }

    public void RotateTransform(float angle) => RotateTransform(angle, MatrixOrder.Prepend);

    public void RotateTransform(float angle, MatrixOrder order)
    {
        CheckNotShared();

        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateRotation(angle * MathF.PI / 180f), order);
    }

    public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

    public void ScaleTransform(float sx, float sy, MatrixOrder order)
    {
        CheckNotShared();

        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateScale(sx, sy), order);
    }

    public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

    public void TranslateTransform(float dx, float dy, MatrixOrder order)
    {
        CheckNotShared();

        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateTranslation(dx, dy), order);
    }

    /// <summary>Sets StartCap, EndCap and DashCap together, as GDI+'s one native call does.</summary>
    public void SetLineCap(LineCap startCap, LineCap endCap, DashCap dashCap)
    {
        CheckNotShared();

        _startCap = startCap;
        _endCap = endCap;
        _dashCap = dashCap;
    }

    public object Clone()
    {
        var clone = _brush is null ? new Pen(Color, Width) : new Pen((Brush)_brush.Clone(), Width);

        clone._alignment = _alignment;
        clone._compoundArray = (float[])_compoundArray.Clone();
        clone._dashCap = _dashCap;
        clone._dashOffset = _dashOffset;
        clone._customDashPattern = (float[]?)_customDashPattern?.Clone();
        clone._dashStyle = _dashStyle;
        clone._startCap = _startCap;
        clone._endCap = _endCap;
        clone._lineJoin = _lineJoin;
        clone._miterLimit = _miterLimit;
        clone._transform = _transform;

        return clone;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }

    private void CheckNotShared()
    {
        if (_isShared) throw new ArgumentException(Unsupported.SharedObject("Pen"));
    }
}
