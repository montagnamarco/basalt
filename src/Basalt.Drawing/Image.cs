using System.Drawing.Imaging;
using System.Runtime.Serialization;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A picture made of pixels.</summary>
public abstract class Image : MarshalByRefObject, ICloneable, IDisposable, ISerializable
{
    // Premultiplied BGRA, because a Skia canvas cannot draw on anything else.
    // The cost is that a pixel set with a very low alpha does not read back
    // with exactly the colour it was given, which GDI+ does preserve.
    private SKBitmap? _pixels;

    private protected Image(SKBitmap pixels, PixelFormat format, ImageFormat rawFormat)
    {
        _pixels = pixels;
        PixelFormat = format;
        RawFormat = rawFormat;
    }

    ~Image() => Dispose(false);

    internal SKBitmap Pixels => _pixels ?? throw new ArgumentException("Parameter is not valid.");

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    public Size Size => new(Width, Height);

    public PixelFormat PixelFormat { get; }

    public ImageFormat RawFormat { get; }

    public object? Tag { get; set; }

    public static Image FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        return new Bitmap(stream);
    }

    public static Image FromFile(string filename) => new Bitmap(filename);

    public object Clone() => new Bitmap(Pixels.Copy(), PixelFormat, RawFormat);

    public void Save(string filename) => Save(filename, ImageFormat.Png);

    public void Save(string filename, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(filename);

        using var file = File.Create(filename);
        Save(file, format);
    }

    public void Save(Stream stream, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(format);

        if (format.Guid != ImageFormat.Png.Guid && format.Guid != ImageFormat.Jpeg.Guid && format.Guid != ImageFormat.Webp.Guid)
        {
            // Not PlatformNotSupportedException: that one says "never off
            // Windows", and code catches it to mean exactly that.
            throw new NotImplementedException($"Saving as {format} is not implemented yet: Png, Jpeg and Webp are.");
        }

        var encoding = format.Guid == ImageFormat.Png.Guid ? SKEncodedImageFormat.Png
            : format.Guid == ImageFormat.Jpeg.Guid ? SKEncodedImageFormat.Jpeg
            : SKEncodedImageFormat.Webp;

        // 75 is the quality GDI+ uses for JPEG when none is asked for.
        using var data = Pixels.Encode(encoding, 75);
        data.SaveTo(stream);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing) return;

        _pixels?.Dispose();
        _pixels = null;
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        using var stream = new MemoryStream();
        Save(stream, ImageFormat.Png);
        info.AddValue("Data", stream.ToArray(), typeof(byte[]));
    }
}
