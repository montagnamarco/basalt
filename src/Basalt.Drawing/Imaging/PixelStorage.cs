using System.Runtime.InteropServices;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// What the library stores pixels as, and the small set of GDI+ pixel formats
/// it can present a bitmap as without actually storing them that way.
/// </summary>
/// <remarks>
/// Every bitmap is premultiplied BGRA8888 underneath, because that is the only
/// layout a Skia canvas can draw on. A caller asking for a different
/// <see cref="PixelFormat"/> (through a constructor, LockBits or Clone) gets a
/// conversion at the boundary, not a different storage layout.
/// </remarks>
internal static class PixelStorage
{
    /// <summary>The GDI+ pixel formats this library can store a bitmap as.</summary>
    public static bool IsSupported(PixelFormat format) =>
        format is PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb or PixelFormat.Format32bppRgb or PixelFormat.Format24bppRgb;

    public static void EnsureSupported(PixelFormat format)
    {
        if (IsSupported(format)) return;

        throw new NotImplementedException(
            $"A bitmap of format {format} is not implemented yet: Format32bppArgb, Format32bppPArgb, Format32bppRgb and Format24bppRgb are.");
    }

    /// <summary>Whether GDI+ gives this format an alpha channel at all.</summary>
    public static bool HasAlpha(PixelFormat format) => format is PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb;

    /// <summary>Bytes per pixel of the format's own byte layout (not our internal storage).</summary>
    public static int BytesPerPixel(PixelFormat format) => format == PixelFormat.Format24bppRgb ? 3 : 4;

    /// <summary>GDI+ rounds a scanline up to a whole DWORD.</summary>
    public static int StrideFor(int width, PixelFormat format) => ((width * BytesPerPixel(format)) + 3) / 4 * 4;

    /// <summary>
    /// Allocates the one layout every bitmap is stored as: premultiplied BGRA8888.
    /// A format without an alpha channel starts fully opaque, not transparent:
    /// GDI+ has nothing to make those pixels see-through with.
    /// </summary>
    public static SKBitmap Allocate(int width, int height, PixelFormat format)
    {
        EnsureSupported(format);
        if (width <= 0 || height <= 0) throw new ArgumentException("Parameter is not valid.");

        var pixels = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul), SKBitmapAllocFlags.ZeroPixels);
        if (!HasAlpha(format)) pixels.Erase(new SKColor(0, 0, 0, 255));

        return pixels;
    }

    public static byte Unpremultiply(byte channel, byte alpha)
    {
        if (alpha == 0) return 0;
        if (alpha == 255) return channel;

        return (byte)Math.Min(255, channel * 255 / alpha);
    }

    // Rounded to the nearest, which is what Skia does when SetPixel stores a
    // colour: the two ways of writing a pixel must leave the same bytes.
    public static byte Premultiply(byte channel, byte alpha) => (byte)((channel * alpha + 127) / 255);

    /// <summary>
    /// Copies a rectangle out of the canonical storage into a caller's buffer, in
    /// the byte layout <paramref name="format"/> names.
    /// </summary>
    public static void CopyOut(SKBitmap source, Rectangle rect, PixelFormat format, IntPtr destination, int destinationStride)
    {
        // Marshal, not a pointer cast: the project does not enable `unsafe`, and
        // Marshal.Copy is the safe-code way to move bytes across a raw pointer.
        var bytesPerPixel = BytesPerPixel(format);
        var row = new byte[destinationStride];
        var sourceRowBytes = source.RowBytes;
        var sourceSpan = source.GetPixelSpan();

        for (var y = 0; y < rect.Height; y++)
        {
            var sourceRowStart = (rect.Y + y) * sourceRowBytes + rect.X * 4;

            for (var x = 0; x < rect.Width; x++)
            {
                var s = sourceRowStart + x * 4;
                byte b = sourceSpan[s], g = sourceSpan[s + 1], r = sourceSpan[s + 2], a = sourceSpan[s + 3];

                var d = x * bytesPerPixel;
                switch (format)
                {
                    case PixelFormat.Format32bppPArgb:
                        row[d] = b; row[d + 1] = g; row[d + 2] = r; row[d + 3] = a;
                        break;

                    case PixelFormat.Format32bppArgb:
                        row[d] = Unpremultiply(b, a); row[d + 1] = Unpremultiply(g, a); row[d + 2] = Unpremultiply(r, a); row[d + 3] = a;
                        break;

                    case PixelFormat.Format32bppRgb:
                        // No alpha channel to unpremultiply against: the canonical
                        // storage is opaque wherever this format is in play (see
                        // Allocate and Bitmap.SetPixel), so the bytes already are
                        // the straight colour.
                        row[d] = b; row[d + 1] = g; row[d + 2] = r; row[d + 3] = 0xFF;
                        break;

                    case PixelFormat.Format24bppRgb:
                        row[d] = b; row[d + 1] = g; row[d + 2] = r;
                        break;

                    default:
                        throw new NotImplementedException($"Reading pixels as {format} is not implemented yet.");
                }
            }

            Marshal.Copy(row, 0, destination + y * destinationStride, destinationStride);
        }
    }

    /// <summary>
    /// Writes a caller's buffer, in the byte layout <paramref name="format"/>
    /// names, back into a rectangle of the canonical storage.
    /// </summary>
    public static void CopyIn(SKBitmap destination, Rectangle rect, PixelFormat format, IntPtr source, int sourceStride)
    {
        var bytesPerPixel = BytesPerPixel(format);
        var row = new byte[sourceStride];
        var destRowBytes = destination.RowBytes;
        var destSpan = destination.GetPixelSpan();

        for (var y = 0; y < rect.Height; y++)
        {
            Marshal.Copy(source + y * sourceStride, row, 0, sourceStride);
            var destRowStart = (rect.Y + y) * destRowBytes + rect.X * 4;

            for (var x = 0; x < rect.Width; x++)
            {
                var s = x * bytesPerPixel;
                var d = destRowStart + x * 4;

                switch (format)
                {
                    case PixelFormat.Format32bppPArgb:
                        destSpan[d] = row[s]; destSpan[d + 1] = row[s + 1]; destSpan[d + 2] = row[s + 2]; destSpan[d + 3] = row[s + 3];
                        break;

                    case PixelFormat.Format32bppArgb:
                        {
                            var a = row[s + 3];
                            destSpan[d] = Premultiply(row[s], a); destSpan[d + 1] = Premultiply(row[s + 1], a);
                            destSpan[d + 2] = Premultiply(row[s + 2], a); destSpan[d + 3] = a;
                            break;
                        }

                    case PixelFormat.Format32bppRgb:
                        destSpan[d] = row[s]; destSpan[d + 1] = row[s + 1]; destSpan[d + 2] = row[s + 2]; destSpan[d + 3] = 0xFF;
                        break;

                    case PixelFormat.Format24bppRgb:
                        destSpan[d] = row[s]; destSpan[d + 1] = row[s + 1]; destSpan[d + 2] = row[s + 2]; destSpan[d + 3] = 0xFF;
                        break;

                    default:
                        throw new NotImplementedException($"Writing pixels as {format} is not implemented yet.");
                }
            }
        }
    }
}
