using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Imaging;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Icons: which image of the file is chosen, what is written back, and how
/// they are drawn.
/// </summary>
public class IconTests
{
    [Fact]
    public void Without_a_size_the_32_pixel_image_is_chosen_and_with_one_the_closest()
    {
        var file = IconFileOf(Png(16, Color.Red), Png(32, Color.Blue), Png(48, Color.Lime));

        using var standard = new Icon(new MemoryStream(file));
        using var small = new Icon(new MemoryStream(file), 16, 16);
        using var near = new Icon(new MemoryStream(file), 44, 44);

        Assert.Equal(new Size(32, 32), standard.Size);
        Assert.Equal(Color.Blue.ToArgb(), PixelOf(standard));
        Assert.Equal(new Size(16, 16), small.Size);
        Assert.Equal(Color.Red.ToArgb(), PixelOf(small));
        Assert.Equal(new Size(48, 48), near.Size);
    }

    [Fact]
    public void An_icon_made_from_another_chooses_again_from_the_whole_file()
    {
        var file = IconFileOf(Png(16, Color.Red), Png(32, Color.Blue));
        using var large = new Icon(new MemoryStream(file));

        using var small = new Icon(large, new Size(16, 16));

        Assert.Equal(Color.Red.ToArgb(), PixelOf(small));
    }

    [Fact]
    public void Save_writes_back_the_whole_file_unchanged()
    {
        var file = IconFileOf(Png(16, Color.Red), Png(32, Color.Blue));
        using var icon = new Icon(new MemoryStream(file), 16, 16);
        using var saved = new MemoryStream();

        icon.Save(saved);

        Assert.Equal(file, saved.ToArray());
    }

    /// <summary>
    /// Most icons in older .resx files are Windows bitmaps with a mask, not
    /// PNG: a 2×2 image whose mask makes the top-right pixel transparent.
    /// </summary>
    [Fact]
    public void A_bitmap_entry_with_a_mask_is_read_with_its_transparency()
    {
        using var icon = new Icon(new MemoryStream(IconFileOf(MaskedBitmapEntry())));
        using var bitmap = icon.ToBitmap();

        Assert.Equal(new Size(2, 2), icon.Size);
        Assert.Equal(Color.FromArgb(255, 0, 0, 255).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(1, 0).A);
    }

    [Fact]
    public void A_file_that_is_not_an_icon_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Icon(new MemoryStream(Png(16, Color.Red))));
    }

    [Fact]
    public void Windows_handles_do_not_exist_here()
    {
        using var icon = new Icon(new MemoryStream(IconFileOf(Png(16, Color.Red))));

        Assert.Throws<PlatformNotSupportedException>(() => icon.Handle);
        Assert.Throws<PlatformNotSupportedException>(() => Icon.FromHandle(IntPtr.Zero));
    }

    [Fact]
    public void System_icons_are_32_pixels_and_drawn_in_their_colour()
    {
        using var error = SystemIcons.Error.ToBitmap();

        Assert.Equal(new Size(32, 32), SystemIcons.Error.Size);
        Assert.Same(SystemIcons.Error, SystemIcons.Hand);
        Assert.True(error.GetPixel(16, 4).R > 150);
        Assert.Equal(0, error.GetPixel(0, 0).A);
    }

    [Fact]
    public void DrawIcon_scales_and_DrawIconUnstretched_cuts()
    {
        using var icon = new Icon(new MemoryStream(IconFileOf(Png(16, Color.Red))), 16, 16);
        using var scaled = new Bitmap(40, 40);
        using var cut = new Bitmap(40, 40);

        using (var graphics = Graphics.FromImage(scaled))
        {
            graphics.DrawIcon(icon, new Rectangle(2, 2, 32, 32));
        }

        using (var graphics = Graphics.FromImage(cut))
        {
            graphics.DrawIconUnstretched(icon, new Rectangle(2, 2, 8, 30));
        }

        Assert.Equal(Color.Red.ToArgb(), scaled.GetPixel(33, 33).ToArgb());
        Assert.Equal(0, scaled.GetPixel(34, 34).A);
        Assert.Equal(Color.Red.ToArgb(), cut.GetPixel(9, 17).ToArgb());
        Assert.Equal(0, cut.GetPixel(10, 10).A);
        Assert.Equal(0, cut.GetPixel(5, 18).A);
    }

    private static int PixelOf(Icon icon)
    {
        using var bitmap = icon.ToBitmap();

        return bitmap.GetPixel(icon.Width / 2, icon.Height / 2).ToArgb();
    }

    private static byte[] Png(int size, Color colour)
    {
        using var bitmap = new Bitmap(size, size);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(colour);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);

        return stream.ToArray();
    }

    /// <summary>
    /// A 2×2, 32-bit Windows bitmap entry: the height in the header is doubled
    /// for the mask, rows run bottom-up, and a set mask bit means transparent.
    /// </summary>
    private static byte[] MaskedBitmapEntry()
    {
        var entry = new byte[40 + 2 * 2 * 4 + 2 * 4];
        BinaryPrimitives.WriteInt32LittleEndian(entry, 40);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(entry.AsSpan(8), 4);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(12), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(entry.AsSpan(14), 32);

        // Every pixel opaque blue (B, G, R, A), bottom row first.
        for (var i = 0; i < 4; i++)
        {
            entry[40 + i * 4] = 255;
            entry[40 + i * 4 + 3] = 255;
        }

        // Mask rows of 4 bytes, bottom row first: in the top row, bit 6 (second pixel) set.
        entry[40 + 16 + 4] = 0b0100_0000;

        // A 32-bit entry carries its own alpha; take the masked pixel's away too,
        // so that the mask and the alpha agree.
        entry[40 + 2 * 2 * 4 - 1] = 0;

        return entry;
    }

    private static byte[] IconFileOf(params byte[][] images)
    {
        var header = 6 + 16 * images.Length;
        var file = new byte[header + images.Sum(image => image.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), (ushort)images.Length);

        var offset = header;
        for (var i = 0; i < images.Length; i++)
        {
            var size = SizeOf(images[i]);
            var at = 6 + i * 16;
            file[at] = (byte)size;
            file[at + 1] = (byte)size;
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(at + 6), 32);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 8), images[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(at + 12), offset);
            images[i].CopyTo(file, offset);
            offset += images[i].Length;
        }

        return file;
    }

    private static int SizeOf(byte[] image) =>
        image[0] == 0x89 ? BinaryPrimitives.ReadInt32BigEndian(image.AsSpan(16)) : BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(4));
}
