using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.CompilerServices;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Image-level behaviour: what a decoded file reports about itself, the eight
/// RotateFlip transforms, resolution, property items and the format-from-extension
/// rule Save(string) follows.
/// </summary>
public class ImageTests
{
    private static readonly Color Red = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color Green = Color.FromArgb(255, 0, 255, 0);
    private static readonly Color Blue = Color.FromArgb(255, 0, 0, 255);
    private static readonly Color Yellow = Color.FromArgb(255, 255, 255, 0);

    [Fact]
    public void A_bitmap_made_in_memory_reports_MemoryBmp()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Equal(ImageFormat.MemoryBmp.Guid, bitmap.RawFormat.Guid);
    }

    [Fact]
    public void A_decoded_png_reports_the_format_it_was_decoded_from()
    {
        using var original = new Bitmap(3, 3);
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        using var loaded = new Bitmap(stream);

        Assert.Equal(ImageFormat.Png.Guid, loaded.RawFormat.Guid);
    }

    [Fact]
    public void A_decoded_jpeg_reports_the_format_it_was_decoded_from()
    {
        using var original = new Bitmap(3, 3);
        using var stream = new MemoryStream();
        original.Save(stream, ImageFormat.Jpeg);
        stream.Position = 0;

        using var loaded = new Bitmap(stream);

        Assert.Equal(ImageFormat.Jpeg.Guid, loaded.RawFormat.Guid);
    }

    /// <summary>
    /// Every one of RotateFlipType's 16 named values collapses to 8 distinct
    /// transforms; a 2x2 bitmap with four different corners can tell all 8
    /// apart, because the group each transform belongs to (the square's
    /// symmetries) acts on four distinct labels without ever repeating an
    /// arrangement.
    /// </summary>
    [Theory]
    [InlineData(RotateFlipType.RotateNoneFlipNone)]
    [InlineData(RotateFlipType.Rotate90FlipNone)]
    [InlineData(RotateFlipType.Rotate180FlipNone)]
    [InlineData(RotateFlipType.Rotate270FlipNone)]
    [InlineData(RotateFlipType.RotateNoneFlipX)]
    [InlineData(RotateFlipType.Rotate90FlipX)]
    [InlineData(RotateFlipType.Rotate180FlipX)]
    [InlineData(RotateFlipType.Rotate270FlipX)]
    public void RotateFlip_produces_the_arrangement_the_transform_names(RotateFlipType type)
    {
        using var bitmap = Corners();

        bitmap.RotateFlip(type);

        var (tl, tr, bl, br) = Expected(type);
        Assert.Equal(tl.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(tr.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(bl.ToArgb(), bitmap.GetPixel(0, 1).ToArgb());
        Assert.Equal(br.ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    /// <summary>All 8 results are pairwise different: the test bitmap really is asymmetric enough.</summary>
    [Fact]
    public void RotateFlip_gives_all_8_values_a_different_result()
    {
        RotateFlipType[] values =
        [
            RotateFlipType.RotateNoneFlipNone, RotateFlipType.Rotate90FlipNone,
            RotateFlipType.Rotate180FlipNone, RotateFlipType.Rotate270FlipNone,
            RotateFlipType.RotateNoneFlipX, RotateFlipType.Rotate90FlipX,
            RotateFlipType.Rotate180FlipX, RotateFlipType.Rotate270FlipX,
        ];

        var arrangements = new HashSet<(int, int, int, int)>();
        foreach (var type in values)
        {
            var (tl, tr, bl, br) = Expected(type);
            arrangements.Add((tl.ToArgb(), tr.ToArgb(), bl.ToArgb(), br.ToArgb()));
        }

        Assert.Equal(8, arrangements.Count);
    }

    [Fact]
    public void SetResolution_changes_what_HorizontalResolution_and_VerticalResolution_report()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Equal(96f, bitmap.HorizontalResolution);
        Assert.Equal(96f, bitmap.VerticalResolution);

        bitmap.SetResolution(150f, 300f);

        Assert.Equal(150f, bitmap.HorizontalResolution);
        Assert.Equal(300f, bitmap.VerticalResolution);
    }

    [Fact]
    public void Property_items_can_be_set_read_listed_and_removed()
    {
        using var bitmap = new Bitmap(2, 2);

        var item = NewPropertyItem(0x0112, [1, 0, 0, 0]);
        bitmap.SetPropertyItem(item);

        Assert.Contains(0x0112, bitmap.PropertyIdList);
        Assert.Same(item, bitmap.GetPropertyItem(0x0112));
        Assert.Contains(bitmap.PropertyItems, i => i.Id == 0x0112);

        bitmap.RemovePropertyItem(0x0112);

        Assert.DoesNotContain(0x0112, bitmap.PropertyIdList);
        Assert.Throws<ArgumentException>(() => bitmap.GetPropertyItem(0x0112));
    }

    [Fact]
    public void GetPropertyItem_of_a_missing_id_throws_ArgumentException()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<ArgumentException>(() => bitmap.GetPropertyItem(999));
    }

    [Fact]
    public void Save_with_only_a_filename_picks_the_format_from_the_extension()
    {
        using var bitmap = new Bitmap(2, 2);
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var jpegPath = Path.Combine(directory.FullName, "picture.jpg");
            bitmap.Save(jpegPath);
            using (var reloaded = new Bitmap(jpegPath)) Assert.Equal(ImageFormat.Jpeg.Guid, reloaded.RawFormat.Guid);

            // An extension GDI+ does not recognise falls back to PNG.
            var unknownPath = Path.Combine(directory.FullName, "picture.made-up-extension");
            bitmap.Save(unknownPath);
            using (var reloaded = new Bitmap(unknownPath)) Assert.Equal(ImageFormat.Png.Guid, reloaded.RawFormat.Guid);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Saving_as_a_format_with_no_writer_says_it_is_not_implemented()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<NotImplementedException>(() => bitmap.Save(new MemoryStream(), ImageFormat.Gif));
        Assert.Throws<NotImplementedException>(() => bitmap.Save(new MemoryStream(), ImageFormat.Tiff));
    }

    [Fact]
    public void FromHbitmap_says_there_is_no_handle_to_take()
    {
        var ex = Assert.Throws<PlatformNotSupportedException>(() => Image.FromHbitmap(IntPtr.Zero));
        Assert.Throws<PlatformNotSupportedException>(() => Image.FromHbitmap(IntPtr.Zero, IntPtr.Zero));
        Assert.Contains("SkiaSharp", ex.Message);
    }

    [Theory]
    [InlineData(PixelFormat.Format32bppArgb, 32)]
    [InlineData(PixelFormat.Format32bppPArgb, 32)]
    [InlineData(PixelFormat.Format24bppRgb, 24)]
    [InlineData(PixelFormat.Format8bppIndexed, 8)]
    public void GetPixelFormatSize_reads_the_bit_count_GDI_plus_packs_into_the_format(PixelFormat format, int bits) =>
        Assert.Equal(bits, Image.GetPixelFormatSize(format));

    [Fact]
    public void IsAlphaPixelFormat_is_true_only_for_the_formats_with_an_alpha_channel()
    {
        Assert.True(Image.IsAlphaPixelFormat(PixelFormat.Format32bppArgb));
        Assert.True(Image.IsAlphaPixelFormat(PixelFormat.Format32bppPArgb));
        Assert.False(Image.IsAlphaPixelFormat(PixelFormat.Format32bppRgb));
        Assert.False(Image.IsAlphaPixelFormat(PixelFormat.Format24bppRgb));
    }

    [Fact]
    public void GetBounds_reports_the_whole_image_in_pixels()
    {
        using var bitmap = new Bitmap(5, 4);
        var unit = GraphicsUnit.World;

        var bounds = bitmap.GetBounds(ref unit);

        Assert.Equal(GraphicsUnit.Pixel, unit);
        Assert.Equal(new RectangleF(0, 0, 5, 4), bounds);
    }

    private static Bitmap Corners()
    {
        var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Red);
        bitmap.SetPixel(1, 0, Green);
        bitmap.SetPixel(0, 1, Blue);
        bitmap.SetPixel(1, 1, Yellow);
        return bitmap;
    }

    /// <summary>
    /// Hand-derived from RotateFlip's own rule (rotationSteps = value &amp; 3,
    /// a horizontal mirror first when bit 2 is set) so the test does not just
    /// check the code against itself.
    /// </summary>
    private static (Color Tl, Color Tr, Color Bl, Color Br) Expected(RotateFlipType type) => type switch
    {
        RotateFlipType.RotateNoneFlipNone => (Red, Green, Blue, Yellow),
        RotateFlipType.Rotate90FlipNone => (Blue, Red, Yellow, Green),
        RotateFlipType.Rotate180FlipNone => (Yellow, Blue, Green, Red),
        RotateFlipType.Rotate270FlipNone => (Green, Yellow, Red, Blue),
        RotateFlipType.RotateNoneFlipX => (Green, Red, Yellow, Blue),
        RotateFlipType.Rotate90FlipX => (Yellow, Green, Blue, Red),
        RotateFlipType.Rotate180FlipX => (Blue, Yellow, Red, Green),
        _ => (Red, Blue, Green, Yellow), // Rotate270FlipX
    };

    private static PropertyItem NewPropertyItem(int id, byte[] value)
    {
        var item = (PropertyItem)RuntimeHelpers.GetUninitializedObject(typeof(PropertyItem));
        item.Id = id;
        item.Type = 3;
        item.Value = value;
        item.Len = value.Length;
        return item;
    }
}
