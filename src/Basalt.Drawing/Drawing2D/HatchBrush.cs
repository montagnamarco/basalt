using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>A brush that fills with a repeating 8x8 two-colour bitmap pattern.</summary>
public sealed class HatchBrush : Brush
{
    private readonly HatchStyle _hatchStyle;
    private readonly Color _foreColor;
    private readonly Color _backColor;

    public HatchBrush(HatchStyle hatchStyle, Color foreColor)
        : this(hatchStyle, foreColor, Color.Black)
    {
    }

    public HatchBrush(HatchStyle hatchStyle, Color foreColor, Color backColor)
    {
        _hatchStyle = hatchStyle;
        _foreColor = foreColor;
        _backColor = backColor;
    }

    public HatchStyle HatchStyle => _hatchStyle;

    public Color ForegroundColor => _foreColor;

    public Color BackgroundColor => _backColor;

    public override object Clone() => new HatchBrush(_hatchStyle, _foreColor, _backColor);

    /// <summary>
    /// Tiles from local coordinate (0, 0), the same corner the caller's own
    /// path coordinates start from, rather than from the shape being filled:
    /// two overlapping hatch-filled shapes line up exactly as GDI+'s do.
    /// </summary>
    internal override void ApplyTo(SKPaint paint)
    {
        var rows = HatchPatterns.Rows(_hatchStyle);
        using var bitmap = new SKBitmap(8, 8, SKColorType.Bgra8888, SKAlphaType.Unpremul);

        for (var r = 0; r < 8; r++)
        {
            for (var c = 0; c < 8; c++)
            {
                var isForeground = (rows[r] & (0x80 >> c)) != 0;
                bitmap.SetPixel(c, r, (isForeground ? _foreColor : _backColor).ToSkia());
            }
        }

        // SKImage.FromBitmap takes its own copy of the pixels, so the shader
        // it hands out keeps working after the local bitmap is disposed here.
        bitmap.SetImmutable();
        using var image = SKImage.FromBitmap(bitmap);

        // The image is copied into the shader; SKPaint.Color must be left at
        // full white so it does not tint the pattern's own colours.
        paint.Color = SKColors.White;
        paint.Shader = image.ToShader(SKShaderTileMode.Repeat, SKShaderTileMode.Repeat);
    }
}
