using System.Drawing.Imaging;
using System.Drawing.Imaging.Effects;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing;

/// <summary>An image whose pixels can be read, written and drawn on.</summary>
public sealed class Bitmap : Image
{
    // Tracks an outstanding LockBits call by the exact BitmapData it handed
    // out, so UnlockBits knows what rectangle, format and buffer to write back
    // (or, for a ReadOnly lock, to simply free without writing anything).
    private readonly Dictionary<BitmapData, LockRegion> _locks = new();

    public Bitmap(int width, int height)
        : this(width, height, PixelFormat.Format32bppArgb)
    {
    }

    public Bitmap(int width, int height, PixelFormat format)
        : base(PixelStorage.Allocate(width, height, format), format, ImageFormat.MemoryBmp)
    {
    }

    public Bitmap(Stream stream)
        : this(stream, useIcm: false)
    {
    }

    public Bitmap(Stream stream, bool useIcm)
        : this(DecodeSource(ReadAll(stream)))
    {
    }

    public Bitmap(string filename)
        : this(filename, useIcm: false)
    {
    }

    public Bitmap(string filename, bool useIcm)
        : this(DecodeSource(File.ReadAllBytes(NotNull(filename))))
    {
    }

    public Bitmap(Image original)
        : base(NotNull(original).Pixels.Copy(), original.PixelFormat, original.RawFormat)
    {
    }

    public Bitmap(Image original, Size newSize)
        : this(original, newSize.Width, newSize.Height)
    {
    }

    public Bitmap(Image original, int width, int height)
        : base(Resize(NotNull(original), width, height), original.PixelFormat, original.RawFormat)
    {
    }

    public Bitmap(int width, int height, Graphics g)
        : this(width, height)
    {
        // 96 DPI is already Image's default resolution; nothing else about a
        // Graphics is public enough for a constructor to read from it.
        ArgumentNullException.ThrowIfNull(g);
    }

    public Bitmap(int width, int height, int stride, PixelFormat format, IntPtr scan0)
        : base(FromScan0(width, height, stride, format, scan0), format, ImageFormat.MemoryBmp)
    {
    }

    public Bitmap(Type type, string resource)
        : this(DecodeSource(ReadResource(type, resource)))
    {
    }

    internal Bitmap(SKBitmap pixels, PixelFormat format, ImageFormat rawFormat)
        : base(pixels, format, rawFormat)
    {
    }

    private Bitmap((SKBitmap Pixels, ImageFormat RawFormat, byte[] Source) decoded)
        : base(decoded.Pixels, PixelFormat.Format32bppArgb, decoded.RawFormat, decoded.Source)
    {
    }

    public void ApplyEffect(Effect effect, Rectangle rectangle)
    {
        ArgumentNullException.ThrowIfNull(effect);

        throw new NotImplementedException(
            "Bitmap.ApplyEffect is not implemented yet: no System.Drawing.Imaging.Effects.Effect subclass (BlurEffect, SharpenEffect, and so on) is implemented to apply.");
    }

    public Bitmap Clone(Rectangle rect, PixelFormat format) => CloneCore(rect, format);

    public Bitmap Clone(RectangleF rect, PixelFormat format) =>
        CloneCore(new Rectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height), format);

    public void ConvertFormat(PixelFormat format) =>
        throw new NotImplementedException($"Bitmap.ConvertFormat to {format} is not implemented yet: pixels are always stored premultiplied BGRA8888.");

    public void ConvertFormat(PixelFormat format, DitherType ditherType, PaletteType paletteType, ColorPalette? palette, float alphaThresholdPercent) =>
        throw new NotImplementedException($"Bitmap.ConvertFormat to {format} is not implemented yet: pixels are always stored premultiplied BGRA8888.");

    public static Bitmap FromHicon(IntPtr hicon) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Bitmap FromResource(IntPtr hinstance, string bitmapName) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHbitmap() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHbitmap(Color background) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public IntPtr GetHicon() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public Color GetPixel(int x, int y)
    {
        CheckInside(x, y);

        var pixel = Pixels.GetPixel(x, y).ToDrawing();
        return PixelStorage.HasAlpha(PixelFormat) ? pixel : Color.FromArgb(255, pixel.R, pixel.G, pixel.B);
    }

    public void SetPixel(int x, int y, Color color)
    {
        CheckInside(x, y);

        var stored = PixelStorage.HasAlpha(PixelFormat) ? color : Color.FromArgb(255, color.R, color.G, color.B);
        Pixels.SetPixel(x, y, stored.ToSkia());
    }

    public BitmapData LockBits(Rectangle rect, ImageLockMode flags, PixelFormat format) =>
        LockBits(rect, flags, format, new BitmapData());

    public BitmapData LockBits(Rectangle rect, ImageLockMode flags, PixelFormat format, BitmapData bitmapData)
    {
        ArgumentNullException.ThrowIfNull(bitmapData);
        PixelStorage.EnsureSupported(format);
        CheckWithinBounds(rect);

        // One lock at a time, as GDI+: two buffers over the same pixels would
        // each write back over what the other changed.
        if (_locks.Count > 0) throw new InvalidOperationException("Bitmap region is already locked.");

        IntPtr buffer;
        int stride;
        bool ownsBuffer;

        if ((flags & ImageLockMode.UserInputBuffer) != 0)
        {
            // The caller already sized and owns this buffer: use exactly what it put in bitmapData.
            buffer = bitmapData.Scan0;
            stride = bitmapData.Stride;
            ownsBuffer = false;
            if (buffer == IntPtr.Zero) throw new ArgumentException("Parameter is not valid.");
        }
        else
        {
            stride = PixelStorage.StrideFor(rect.Width, format);
            buffer = Marshal.AllocHGlobal(stride * rect.Height);
            ownsBuffer = true;

            bitmapData.Width = rect.Width;
            bitmapData.Height = rect.Height;
            bitmapData.Stride = stride;
            bitmapData.PixelFormat = format;
            bitmapData.Scan0 = buffer;
            bitmapData.Reserved = 0;
        }

        // GDI+'s ImageLockMode.ReadOnly bit means the caller may read the
        // buffer: fill it. WriteOnly alone leaves it whatever AllocHGlobal gave,
        // just like GDI+ does not bother initialising a write-only lock either.
        if (((int)flags & (int)ImageLockMode.ReadOnly) != 0)
        {
            try
            {
                PixelStorage.CopyOut(Pixels, rect, format, buffer, stride);
            }
            catch
            {
                // Not registered yet, so nothing else would ever free it.
                if (ownsBuffer) Marshal.FreeHGlobal(buffer);

                throw;
            }
        }

        _locks[bitmapData] = new LockRegion(rect, flags, format, buffer, stride, ownsBuffer);

        // A lock remembers a rectangle of these pixels and writes back into it:
        // rotating the image meanwhile would send that write somewhere else.
        MarkInUse();

        return bitmapData;
    }

    public void UnlockBits(BitmapData bitmapData)
    {
        ArgumentNullException.ThrowIfNull(bitmapData);
        if (!_locks.Remove(bitmapData, out var region)) throw new ArgumentException("Parameter is not valid.");

        MarkFree();

        // The WriteOnly bit (set by both WriteOnly and ReadWrite) is GDI+'s
        // signal that the buffer holds pixels to keep; a ReadOnly lock must not
        // write anything back, even though it never changed the buffer either.
        if (((int)region.Mode & (int)ImageLockMode.WriteOnly) != 0)
        {
            PixelStorage.CopyIn(Pixels, region.Rect, region.Format, region.Buffer, region.Stride);
        }

        if (region.OwnsBuffer) Marshal.FreeHGlobal(region.Buffer);
    }

    /// <summary>
    /// Frees the buffers of locks nobody unlocked. They are unmanaged memory, so
    /// this runs from the finalizer too.
    /// </summary>
    private protected override void ReleaseUnmanaged()
    {
        foreach (var region in _locks.Values)
        {
            if (region.OwnsBuffer) Marshal.FreeHGlobal(region.Buffer);

            MarkFree();
        }

        _locks.Clear();
    }

    public void MakeTransparent() => MakeTransparent(GetPixel(0, Height - 1));

    public void MakeTransparent(Color color)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var pixel = GetPixel(x, y);
                if (pixel.R == color.R && pixel.G == color.G && pixel.B == color.B)
                {
                    SetPixel(x, y, Color.FromArgb(0, 0, 0, 0));
                }
            }
        }
    }

    public void SetResolution(float xDpi, float yDpi) => SetResolutionCore(xDpi, yDpi);

    private Bitmap CloneCore(Rectangle rect, PixelFormat format)
    {
        PixelStorage.EnsureSupported(format);
        CheckWithinBounds(rect);

        var subset = new SKBitmap(new SKImageInfo(rect.Width, rect.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!Pixels.ExtractSubset(subset, new SKRectI(rect.Left, rect.Top, rect.Right, rect.Bottom)))
        {
            subset.Dispose();
            throw new ArgumentException("Parameter is not valid.");
        }

        return new Bitmap(subset, format, ImageFormat.MemoryBmp);
    }

    private void CheckWithinBounds(Rectangle rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > Width || rect.Bottom > Height)
        {
            throw new ArgumentException("Parameter is not valid.");
        }
    }

    private void CheckInside(int x, int y)
    {
        // The exceptions GDI+ throws, which code catches by type and parameter.
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
    }

    private static SKBitmap FromScan0(int width, int height, int stride, PixelFormat format, IntPtr scan0)
    {
        var pixels = PixelStorage.Allocate(width, height, format);
        if (scan0 != IntPtr.Zero)
        {
            PixelStorage.CopyIn(pixels, new Rectangle(0, 0, width, height), format, scan0, stride);
        }

        return pixels;
    }

    private static SKBitmap Resize(Image original, int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentException("Parameter is not valid.");
        if (width == original.Width && height == original.Height) return original.Pixels.Copy();

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        return original.Pixels.Resize(info, SKSamplingOptions.Default) ?? throw new ArgumentException("Parameter is not valid.");
    }

    private static byte[] ReadResource(Type type, string resource)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(resource);

        using var stream = type.Assembly.GetManifestResourceStream(type, resource)
            ?? throw new ArgumentException($"Resource '{resource}' was not found for type '{type}'.");

        return ReadAll(stream);
    }

    private static byte[] ReadAll(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>
    /// Decodes a whole file's bytes into the one layout every bitmap is stored
    /// as, and reports the format the bytes were actually in - PNG, JPEG, GIF,
    /// BMP, ICO or WebP, whatever SKCodec recognised them as - as RawFormat.
    /// The bytes themselves are kept so GetFrameCount and SelectActiveFrame can
    /// go back to frames the first decode did not need.
    /// </summary>
    private static (SKBitmap Pixels, ImageFormat RawFormat, byte[] Source) DecodeSource(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new ArgumentException("Parameter is not valid.");

        var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
        var pixels = new SKBitmap(info);
        var result = codec.GetPixels(info, pixels.GetPixels(), pixels.RowBytes, new SKCodecOptions(0));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            pixels.Dispose();
            throw new ArgumentException("Parameter is not valid.");
        }

        return (pixels, MapFormat(codec.EncodedFormat), bytes);
    }

    private static ImageFormat MapFormat(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => ImageFormat.Png,
        SKEncodedImageFormat.Jpeg => ImageFormat.Jpeg,
        SKEncodedImageFormat.Gif => ImageFormat.Gif,
        SKEncodedImageFormat.Bmp => ImageFormat.Bmp,
        SKEncodedImageFormat.Ico => ImageFormat.Icon,
        SKEncodedImageFormat.Webp => ImageFormat.Webp,
        _ => throw new NotImplementedException($"Decoding {format} is not implemented yet: Png, Jpeg, Gif, Bmp, Ico and Webp are."),
    };

    private static string NotNull(string? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value;
    }

    private static Image NotNull(Image? image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return image;
    }

    private sealed record LockRegion(Rectangle Rect, ImageLockMode Mode, PixelFormat Format, IntPtr Buffer, int Stride, bool OwnsBuffer);
}
