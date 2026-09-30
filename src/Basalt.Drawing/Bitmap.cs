using System.Drawing.Imaging;
using SkiaSharp;

namespace System.Drawing;

/// <summary>An image whose pixels can be read, written and drawn on.</summary>
public sealed class Bitmap : Image
{
    public Bitmap(int width, int height)
        : this(width, height, PixelFormat.Format32bppArgb)
    {
    }

    public Bitmap(int width, int height, PixelFormat format)
        : base(Allocate(width, height), format, ImageFormat.MemoryBmp)
    {
        if (format is not (PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb or PixelFormat.Format32bppRgb))
        {
            throw new NotImplementedException($"A bitmap of format {format} is not implemented yet: the 32 bit formats are.");
        }
    }

    public Bitmap(Stream stream)
        : this(Decode(stream), PixelFormat.Format32bppArgb, ImageFormat.MemoryBmp)
    {
    }

    public Bitmap(string filename)
        : this(Decode(filename), PixelFormat.Format32bppArgb, ImageFormat.MemoryBmp)
    {
    }

    internal Bitmap(SKBitmap pixels, PixelFormat format, ImageFormat rawFormat)
        : base(pixels, format, rawFormat)
    {
    }

    public Color GetPixel(int x, int y)
    {
        CheckInside(x, y);

        return Pixels.GetPixel(x, y).ToDrawing();
    }

    public void SetPixel(int x, int y, Color color)
    {
        CheckInside(x, y);

        Pixels.SetPixel(x, y, color.ToSkia());
    }

    private void CheckInside(int x, int y)
    {
        // The exceptions GDI+ throws, which code catches by type and parameter.
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
    }

    private static SKBitmap Allocate(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Parameter is not valid.");

        // A new GDI+ bitmap is transparent black, and SKBitmap's memory is
        // zeroed only when asked.
        var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), SKBitmapAllocFlags.ZeroPixels);
        return pixels;
    }

    private static SKBitmap Decode(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        using var file = File.OpenRead(filename);
        return Decode(file);
    }

    private static SKBitmap Decode(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var decoded = SKBitmap.Decode(stream) ?? throw new ArgumentException("Parameter is not valid.");

        // Whatever the file held, the pixels become the one format the rest of
        // the library draws on.
        var pixels = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!decoded.CopyTo(pixels, SKColorType.Bgra8888)) throw new ArgumentException("Parameter is not valid.");

        return pixels;
    }
}
