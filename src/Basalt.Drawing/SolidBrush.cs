using SkiaSharp;

namespace System.Drawing;

/// <summary>A brush of a single colour.</summary>
public sealed class SolidBrush : Brush
{
    // True for the brushes of Brushes and SystemBrushes, which many callers share.
    private readonly bool _isShared;
    private Color _color;

    public SolidBrush(Color color) => _color = color;

    internal SolidBrush(Color color, bool isShared)
    {
        _color = color;
        _isShared = isShared;
    }

    public Color Color
    {
        get => _color;
        set
        {
            if (_isShared) throw new ArgumentException(Unsupported.SharedObject("Brush"));

            _color = value;
        }
    }

    // A copy is the caller's own, so it can be changed.
    public override object Clone() => new SolidBrush(Color);

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    internal override void ApplyTo(SKPaint paint) => paint.Color = Color.ToSkia();
}
