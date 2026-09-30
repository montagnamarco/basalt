namespace System.Drawing;

/// <summary>What the outline of a shape is drawn with.</summary>
public sealed class Pen : MarshalByRefObject, ICloneable, IDisposable
{
    private Brush? _brush;

    public Pen(Color color)
        : this(color, 1f)
    {
    }

    public Pen(Color color, float width)
    {
        Color = color;
        Width = width;
    }

    public Pen(Brush brush)
        : this(brush, 1f)
    {
    }

    public Pen(Brush brush, float width)
    {
        ArgumentNullException.ThrowIfNull(brush);

        _brush = brush;
        Color = brush is SolidBrush solid ? solid.Color : Color.Black;
        Width = width;
    }

    ~Pen() => Dispose(false);

    public Color Color { get; set; }

    public float Width { get; set; }

    public Brush Brush
    {
        get => _brush ?? new SolidBrush(Color);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _brush = value;
            if (value is SolidBrush solid) Color = solid.Color;
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
}
