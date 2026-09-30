using SkiaSharp;

namespace System.Drawing;

/// <summary>A brush of a single colour.</summary>
public sealed class SolidBrush : Brush
{
    public SolidBrush(Color color) => Color = color;

    public Color Color { get; set; }

    public override object Clone() => new SolidBrush(Color);

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    internal override void ApplyTo(SKPaint paint) => paint.Color = Color.ToSkia();
}
