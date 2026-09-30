namespace System.Drawing;

/// <summary>What the outline of a shape is drawn with.</summary>
public sealed class Pen : MarshalByRefObject, ICloneable, IDisposable
{
    // True for the pens of Pens and SystemPens, which many callers share.
    private readonly bool _isShared;
    private Brush? _brush;
    private Color _color;
    private float _width;

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

    public object Clone() => _brush is null ? new Pen(Color, Width) : new Pen((Brush)_brush.Clone(), Width);

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
