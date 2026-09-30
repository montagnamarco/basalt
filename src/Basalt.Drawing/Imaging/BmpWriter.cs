using System.Text;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// Writes a BMP the way GDI+ does: a BITMAPFILEHEADER and a BITMAPINFOHEADER,
/// bottom-up, 24 bits for an opaque image and 32 for one with an alpha channel.
/// </summary>
/// <remarks>
/// SkiaSharp's encoder does not offer BMP at all, so this is a small writer of
/// our own rather than a call into Skia. Reading a BMP is still SKCodec's job:
/// it already understands the format.
/// </remarks>
internal static class BmpWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    // 96 DPI, GDI+'s default, expressed as GDI+'s BITMAPINFOHEADER unit: pixels per metre.
    private const int PixelsPerMetreAt96Dpi = 3780;

    public static void Write(Stream stream, SKBitmap pixels, PixelFormat format)
    {
        var writeAlpha = PixelStorage.HasAlpha(format);
        var bitsPerPixel = writeAlpha ? 32 : 24;
        var width = pixels.Width;
        var height = pixels.Height;
        var stride = PixelStorage.StrideFor(width, writeAlpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        var pixelDataSize = stride * height;
        var dataOffset = FileHeaderSize + InfoHeaderSize;

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // BITMAPFILEHEADER.
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write(dataOffset + pixelDataSize);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write(dataOffset);

        // BITMAPINFOHEADER.
        writer.Write(InfoHeaderSize);
        writer.Write(width);
        writer.Write(height); // Positive height is GDI+'s signal for bottom-up rows.
        writer.Write((short)1);
        writer.Write((short)bitsPerPixel);
        writer.Write(0); // BI_RGB: no compression.
        writer.Write(pixelDataSize);
        writer.Write(PixelsPerMetreAt96Dpi);
        writer.Write(PixelsPerMetreAt96Dpi);
        writer.Write(0);
        writer.Write(0);

        var sourceRowBytes = pixels.RowBytes;
        var sourceSpan = pixels.GetPixelSpan();
        var row = new byte[stride];

        // Bottom-up: the first row written to the file is the bottom row of the image.
        for (var y = height - 1; y >= 0; y--)
        {
            var sourceRowStart = y * sourceRowBytes;

            for (var x = 0; x < width; x++)
            {
                var s = sourceRowStart + x * 4;
                byte b = sourceSpan[s], g = sourceSpan[s + 1], r = sourceSpan[s + 2], a = sourceSpan[s + 3];

                if (writeAlpha)
                {
                    var d = x * 4;
                    row[d] = PixelStorage.Unpremultiply(b, a);
                    row[d + 1] = PixelStorage.Unpremultiply(g, a);
                    row[d + 2] = PixelStorage.Unpremultiply(r, a);
                    row[d + 3] = a;
                }
                else
                {
                    // No alpha channel: the pixels are opaque already (see
                    // PixelStorage.Allocate), so premultiplied and straight agree.
                    var d = x * 3;
                    row[d] = b;
                    row[d + 1] = g;
                    row[d + 2] = r;
                }
            }

            // Padding bytes beyond the pixel data stay zero, as GDI+ writes them.
            Array.Clear(row, width * (writeAlpha ? 4 : 3), stride - width * (writeAlpha ? 4 : 3));
            writer.Write(row);
        }
    }
}
