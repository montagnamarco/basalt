using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;

namespace Basalt.Tests;

/// <summary>
/// A control rasterised once, with pixels read back as colours.
///
/// A rendering claim ("the title bar is dark in the dark theme") is only
/// proved by looking at what was drawn, so the helpers here keep the
/// channel order out of the tests: the surface is BGRA on some platforms and
/// RGBA on others.
/// </summary>
public sealed class Raster
{
    private readonly byte[] _pixels;
    private readonly int _width;
    private readonly bool _bgra;

    private Raster(byte[] pixels, int width, bool bgra)
    {
        _pixels = pixels;
        _width = width;
        _bgra = bgra;
    }

    /// <summary>Draws the control at its current size.</summary>
    public static Raster Of(Control control)
    {
        var width = (int)Math.Ceiling(control.Bounds.Width);
        var height = (int)Math.Ceiling(control.Bounds.Height);

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(control);

        var pixels = new byte[width * height * 4];
        var pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);

        try { bitmap.CopyPixels(new PixelRect(0, 0, width, height), pinned.AddrOfPinnedObject(), pixels.Length, width * 4); }
        finally { pinned.Free(); }

        return new Raster(pixels, width, bitmap.Format != PixelFormat.Rgba8888);
    }

    public Color At(int x, int y)
    {
        var i = (y * _width + x) * 4;

        return _bgra
            ? Color.FromRgb(_pixels[i + 2], _pixels[i + 1], _pixels[i])
            : Color.FromRgb(_pixels[i], _pixels[i + 1], _pixels[i + 2]);
    }

    /// <summary>The control's rectangle in the coordinates of the window it is in.</summary>
    public static PixelRect BoundsIn(Control control, Visual window)
    {
        var origin = control.TranslatePoint(new Point(0, 0), window) ?? default;

        return new PixelRect((int)Math.Round(origin.X), (int)Math.Round(origin.Y),
            (int)Math.Round(control.Bounds.Width), (int)Math.Round(control.Bounds.Height));
    }

    /// <summary>The largest contrast ratio any pixel in the area has against a colour.</summary>
    public double BestContrastAgainst(PixelRect area, Color background)
    {
        var best = 1.0;

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
                best = Math.Max(best, Contrast(At(x, y), background));

        return best;
    }

    /// <summary>Whether any pixel in the area is the colour, within a tolerance.</summary>
    public bool Contains(PixelRect area, Color colour, int tolerance = 3)
    {
        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
                if (Near(At(x, y), colour, tolerance)) return true;

        return false;
    }

    public static bool Near(Color a, Color b, int tolerance = 3) =>
        Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;

    /// <summary>WCAG contrast ratio between two colours.</summary>
    public static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);

        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    public static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
