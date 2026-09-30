using System.Drawing;
using System.Drawing.Imaging;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The System.Drawing.Imaging types: the codecs ImageCodecInfo reports, a BMP
/// round trip through the library's own writer, JPEG quality through
/// EncoderParameters, and the small value types (ColorPalette, FrameDimension).
/// </summary>
public class ImagingTests
{
    [Fact]
    public void GetImageEncoders_lists_only_the_formats_this_library_writes()
    {
        var names = Array.ConvertAll(ImageCodecInfo.GetImageEncoders(), c => c.FormatDescription);

        Assert.Contains("BMP", names);
        Assert.Contains("JPEG", names);
        Assert.Contains("PNG", names);
        Assert.DoesNotContain("GIF", names); // Read-only in this library.
        Assert.DoesNotContain("TIFF", names); // Neither read nor written.
    }

    [Fact]
    public void GetImageDecoders_lists_only_the_formats_this_library_reads()
    {
        var names = Array.ConvertAll(ImageCodecInfo.GetImageDecoders(), c => c.FormatDescription);

        Assert.Contains("BMP", names);
        Assert.Contains("JPEG", names);
        Assert.Contains("GIF", names);
        Assert.Contains("PNG", names);
        Assert.DoesNotContain("TIFF", names);
    }

    [Fact]
    public void Each_codec_carries_GDI_plus_own_format_id_and_mime_type()
    {
        var png = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatDescription == "PNG")!;

        Assert.Equal(ImageFormat.Png.Guid, png.FormatID);
        Assert.Equal("image/png", png.MimeType);
        Assert.NotEqual(Guid.Empty, png.Clsid);
    }

    /// <summary>32-bit BMP: BITMAPFILEHEADER + BITMAPINFOHEADER + straight-alpha pixels, bottom-up.</summary>
    [Fact]
    public void Saving_a_bitmap_as_Bmp_and_reading_it_back_keeps_the_pixels()
    {
        using var bitmap = new Bitmap(3, 2);
        bitmap.SetPixel(2, 1, Color.FromArgb(255, 10, 20, 30));
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 200, 150, 90));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Bmp);
        stream.Position = 0;

        // "BM" magic bytes, GDI+'s own signature for the format.
        Assert.Equal((byte)'B', stream.ReadByte());
        Assert.Equal((byte)'M', stream.ReadByte());
        stream.Position = 0;

        using var loaded = new Bitmap(stream);
        Assert.Equal(new Size(3, 2), loaded.Size);
        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), loaded.GetPixel(2, 1).ToArgb());
        Assert.Equal(Color.FromArgb(255, 200, 150, 90).ToArgb(), loaded.GetPixel(0, 0).ToArgb());
    }

    /// <summary>A width whose 24-bit row is not a whole DWORD still round-trips through the padding.</summary>
    [Fact]
    public void Saving_a_Format24bppRgb_bitmap_as_Bmp_pads_every_row_to_4_bytes()
    {
        using var bitmap = new Bitmap(5, 3, PixelFormat.Format24bppRgb); // 5 * 3 = 15 bytes/row, pads to 16.
        bitmap.SetPixel(4, 2, Color.FromArgb(255, 7, 8, 9));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Bmp);
        stream.Position = 0;

        using var loaded = new Bitmap(stream);
        Assert.Equal(new Size(5, 3), loaded.Size);
        Assert.Equal(Color.FromArgb(255, 7, 8, 9).ToArgb(), loaded.GetPixel(4, 2).ToArgb());
    }

    /// <summary>
    /// A higher JPEG quality keeps more detail and so, for a bitmap with real
    /// detail to keep, encodes to more bytes.
    /// </summary>
    [Fact]
    public void A_higher_JPEG_quality_produces_more_bytes()
    {
        using var bitmap = new Bitmap(32, 32);
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                // Enough varying detail that quantisation strength actually shows up in size.
                bitmap.SetPixel(x, y, Color.FromArgb(255, (x * 37 + y * 91) % 256, (x * 61) % 256, (y * 53) % 256));
            }
        }

        Assert.True(SavedJpegLength(bitmap, 90) > SavedJpegLength(bitmap, 5));
    }

    private static int SavedJpegLength(Bitmap bitmap, long quality)
    {
        var jpeg = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.FormatID == ImageFormat.Jpeg.Guid)!;
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        using var stream = new MemoryStream();
        bitmap.Save(stream, jpeg, parameters);
        return (int)stream.Length;
    }

    [Fact]
    public void ColorPalette_of_type_FixedBlackAndWhite_holds_exactly_black_and_white()
    {
        var palette = new ColorPalette(PaletteType.FixedBlackAndWhite);

        Assert.Equal([Color.Black, Color.White], palette.Entries);
    }

    [Fact]
    public void A_bitmaps_own_palette_starts_empty_because_pixels_are_never_stored_indexed()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Empty(bitmap.Palette.Entries);
    }

    [Fact]
    public void FrameDimension_Time_Page_and_Resolution_are_the_three_distinct_GDI_plus_GUIDs()
    {
        Assert.NotEqual(FrameDimension.Time.Guid, FrameDimension.Page.Guid);
        Assert.NotEqual(FrameDimension.Time.Guid, FrameDimension.Resolution.Guid);
        Assert.NotEqual(FrameDimension.Page.Guid, FrameDimension.Resolution.Guid);
    }

    [Fact]
    public void A_single_frame_image_reports_a_frame_count_of_1()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Equal(1, bitmap.GetFrameCount(FrameDimension.Page));
        Assert.Equal(0, bitmap.SelectActiveFrame(FrameDimension.Page, 0));
    }
}
