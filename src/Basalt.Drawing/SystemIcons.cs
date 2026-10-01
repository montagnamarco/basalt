using System.Drawing.Imaging;
using SkiaSharp;

namespace System.Drawing;

/// <summary>The icons of the system's message boxes and windows.</summary>
/// <remarks>
/// Windows hands out its own pictures here; they are Microsoft's and are not
/// copied. These are drawn in the same spirit, in the flat colours of the
/// Windows 2000 look the controls use, at 16 and 32 pixels. The question mark,
/// exclamation mark and "i" are letters of the platform's default font.
/// </remarks>
public static class SystemIcons
{
    private static readonly Lazy<Icon> ApplicationIcon = new(() => Make(DrawApplication));
    private static readonly Lazy<Icon> ErrorIcon = new(() => Make(DrawError));
    private static readonly Lazy<Icon> WarningIcon = new(() => Make(DrawWarning));
    private static readonly Lazy<Icon> InformationIcon = new(() => Make(size => DrawLetterInCircle(size, "i", new SKColor(0x00, 0x48, 0xC8))));
    private static readonly Lazy<Icon> QuestionIcon = new(() => Make(size => DrawLetterInCircle(size, "?", new SKColor(0x00, 0x48, 0xC8))));
    private static readonly Lazy<Icon> ShieldIcon = new(() => Make(DrawShield));

    public static Icon Application => ApplicationIcon.Value;

    public static Icon Asterisk => InformationIcon.Value;

    public static Icon Error => ErrorIcon.Value;

    public static Icon Exclamation => WarningIcon.Value;

    public static Icon Hand => ErrorIcon.Value;

    public static Icon Information => InformationIcon.Value;

    public static Icon Question => QuestionIcon.Value;

    public static Icon Shield => ShieldIcon.Value;

    public static Icon Warning => WarningIcon.Value;

    public static Icon WinLogo => ApplicationIcon.Value;

    public static Icon GetStockIcon(StockIconId stockIcon, StockIconOptions options = StockIconOptions.Default) =>
        throw new PlatformNotSupportedException(StockIcons);

    public static Icon GetStockIcon(StockIconId stockIcon, int size) => throw new PlatformNotSupportedException(StockIcons);

    private const string StockIcons = "Stock icons come from the Windows shell, which Basalt.Drawing does not have.";

    private static Icon Make(Action<SKCanvas, int> draw) => Make((Func<int, SKBitmap>)(size => Render(size, draw)));

    private static Icon Make(Func<int, SKBitmap> render)
    {
        using var small = render(16);
        using var large = render(32);

        return new Icon(IconFile.FromImages([small, large]), 32, 32);
    }

    private static SKBitmap Render(int size, Action<SKCanvas, int> draw)
    {
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);

        canvas.Clear(SKColors.Transparent);
        draw(canvas, size);

        return bitmap;
    }

    private static void DrawApplication(SKCanvas canvas, int size)
    {
        var unit = size / 16f;
        using var frame = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = unit, IsAntialias = false };
        using var window = new SKPaint { Color = SKColors.White };
        using var title = new SKPaint { Color = new SKColor(0x0A, 0x24, 0x6A) };

        var outer = new SKRect(unit, 2 * unit, size - unit, size - 2 * unit);
        canvas.DrawRect(outer, window);
        canvas.DrawRect(new SKRect(outer.Left, outer.Top, outer.Right, outer.Top + 3 * unit), title);
        canvas.DrawRect(outer, frame);
    }

    private static void DrawError(SKCanvas canvas, int size)
    {
        var unit = size / 16f;
        using var red = new SKPaint { Color = new SKColor(0xD0, 0x00, 0x00), IsAntialias = true };
        using var cross = new SKPaint { Color = SKColors.White, StrokeWidth = 2 * unit, IsAntialias = true, StrokeCap = SKStrokeCap.Round };

        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - unit / 2, red);
        canvas.DrawLine(5 * unit, 5 * unit, size - 5 * unit, size - 5 * unit, cross);
        canvas.DrawLine(size - 5 * unit, 5 * unit, 5 * unit, size - 5 * unit, cross);
    }

    private static void DrawWarning(SKCanvas canvas, int size)
    {
        var unit = size / 16f;
        using var yellow = new SKPaint { Color = new SKColor(0xFF, 0xD8, 0x00), IsAntialias = true };
        using var outline = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = unit / 2, IsAntialias = true };
        using var triangle = new SKPath();

        triangle.MoveTo(size / 2f, unit);
        triangle.LineTo(size - unit, size - unit);
        triangle.LineTo(unit, size - unit);
        triangle.Close();

        canvas.DrawPath(triangle, yellow);
        canvas.DrawPath(triangle, outline);
        DrawLetter(canvas, size, "!", SKColors.Black, size * 0.62f, size * 0.9f);
    }

    private static void DrawLetterInCircle(SKCanvas canvas, int size, string letter, SKColor colour)
    {
        var unit = size / 16f;
        using var disc = new SKPaint { Color = colour, IsAntialias = true };

        canvas.DrawCircle(size / 2f, size / 2f, size / 2f - unit / 2, disc);
        DrawLetter(canvas, size, letter, SKColors.White, size * 0.7f, size * 0.75f);
    }

    private static SKBitmap DrawLetterInCircle(int size, string letter, SKColor colour) =>
        Render(size, (canvas, s) => DrawLetterInCircle(canvas, s, letter, colour));

    private static void DrawShield(SKCanvas canvas, int size)
    {
        var unit = size / 16f;
        using var blue = new SKPaint { Color = new SKColor(0x1F, 0x5F, 0xC8), IsAntialias = true };
        using var yellow = new SKPaint { Color = new SKColor(0xF0, 0xC0, 0x00), IsAntialias = true };
        using var shield = new SKPath();

        shield.MoveTo(size / 2f, unit);
        shield.LineTo(size - 2 * unit, 3 * unit);
        shield.CubicTo(size - 2 * unit, 10 * unit, size / 2f + 2 * unit, 13 * unit, size / 2f, size - unit);
        shield.CubicTo(size / 2f - 2 * unit, 13 * unit, 2 * unit, 10 * unit, 2 * unit, 3 * unit);
        shield.Close();

        canvas.DrawPath(shield, blue);
        canvas.Save();
        canvas.ClipPath(shield, antialias: true);
        canvas.DrawRect(new SKRect(size / 2f, unit, size, size / 2f), yellow);
        canvas.DrawRect(new SKRect(0, size / 2f, size / 2f, size), yellow);
        canvas.Restore();
    }

    private static void DrawLetter(SKCanvas canvas, int size, string letter, SKColor colour, float height, float baseline)
    {
        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using var font = new SKFont(typeface, height);
        using var paint = new SKPaint { Color = colour, IsAntialias = true };

        var width = font.MeasureText(letter);
        canvas.DrawText(letter, (size - width) / 2, baseline, SKTextAlign.Left, font, paint);
    }
}
