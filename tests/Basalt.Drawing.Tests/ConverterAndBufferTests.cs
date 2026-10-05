using System.Buffers.Binary;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;

namespace Basalt.Drawing.Tests;

/// <summary>
/// ImageConverter, ImageFormatConverter and IconConverter round trips;
/// BufferedGraphics drawing reaching its target only on Render; ImageAnimator's
/// CanAnimate on an image that cannot animate; ToolboxBitmapAttribute's
/// fallback default image.
/// </summary>
public class ConverterAndBufferTests
{
    [Fact]
    public void A_MemoryBmp_bitmap_converts_to_PNG_bytes()
    {
        using var bitmap = new Bitmap(2, 2);
        var converter = new ImageConverter();

        var bytes = (byte[])converter.ConvertTo(null, null, bitmap, typeof(byte[]))!;

        // PNG's own signature, not GDI+'s MemoryBmp (which no decoder reads back).
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public void A_decoded_jpeg_converts_back_to_jpeg_bytes_not_png()
    {
        using var original = new Bitmap(3, 3);
        using var jpegStream = new MemoryStream();
        original.Save(jpegStream, ImageFormat.Jpeg);
        jpegStream.Position = 0;

        using var decoded = new Bitmap(jpegStream);
        var converter = new ImageConverter();

        var bytes = (byte[])converter.ConvertTo(null, null, decoded, typeof(byte[]))!;

        // JPEG's own signature: converting back must keep the format a real
        // file was decoded from, only a freshly drawn bitmap becomes PNG.
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);
    }

    [Fact]
    public void A_PNG_byte_array_round_trips_through_ImageConverter()
    {
        using var original = new Bitmap(4, 5);
        var converter = new ImageConverter();
        var bytes = (byte[])converter.ConvertTo(null, null, original, typeof(byte[]))!;

        using var restored = (Image)converter.ConvertFrom(null, null, bytes)!;

        Assert.Equal(4, restored.Width);
        Assert.Equal(5, restored.Height);
    }

    /// <summary>
    /// A .resx compiled with preserialized resources stores icons and images as
    /// bytes, and the runtime reader turns them back through
    /// TypeDescriptor.GetConverter(type), so the converter must be declared on
    /// the type itself, as System.Drawing declares it.
    /// </summary>
    [Theory]
    [InlineData(typeof(Icon), typeof(IconConverter))]
    [InlineData(typeof(Image), typeof(ImageConverter))]
    [InlineData(typeof(Bitmap), typeof(ImageConverter))]
    [InlineData(typeof(ImageFormat), typeof(ImageFormatConverter))]
    [InlineData(typeof(System.Drawing.Printing.Margins), typeof(System.Drawing.Printing.MarginsConverter))]
    public void Resource_types_declare_the_converter_the_resource_reader_asks_for(Type type, Type converter)
    {
        Assert.IsType(converter, TypeDescriptor.GetConverter(type));
    }

    [Fact]
    public void Resource_bytes_become_an_icon_and_a_bitmap_through_their_type_converters()
    {
        using var original = new Bitmap(16, 16);
        var pngBytes = (byte[])new ImageConverter().ConvertTo(null, null, original, typeof(byte[]))!;

        using var bitmap = (Bitmap)TypeDescriptor.GetConverter(typeof(Bitmap)).ConvertFrom(pngBytes)!;
        Assert.Equal(16, bitmap.Width);

        using var icon = (Icon)TypeDescriptor.GetConverter(typeof(Icon)).ConvertFrom(IconFileWithOnePng(pngBytes, 16))!;
        Assert.Equal(16, icon.Width);
    }

    // An .ico file holding one PNG image: the 6-byte ICONDIR, one 16-byte
    // ICONDIRENTRY, then the image itself at offset 22.
    private static byte[] IconFileWithOnePng(byte[] png, byte size)
    {
        var file = new byte[22 + png.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);
        file[6] = size;
        file[7] = size;
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(12), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(14), (uint)png.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(18), 22);
        png.CopyTo(file, 22);
        return file;
    }

    /// <summary>
    /// A Visual Studio .resx that pre-dates PNG serialisation wraps the real
    /// image bytes in an old OLE header; ImageConverter must find the PNG
    /// signature wherever it actually starts instead of at offset 0.
    /// </summary>
    [Fact]
    public void ImageConverter_skips_an_old_OLE_header_before_the_real_image_signature()
    {
        using var original = new Bitmap(2, 2);
        using var pngStream = new MemoryStream();
        original.Save(pngStream, ImageFormat.Png);

        var oleHeader = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 };
        var wrapped = oleHeader.Concat(pngStream.ToArray()).ToArray();

        var converter = new ImageConverter();
        using var restored = (Image)converter.ConvertFrom(null, null, wrapped)!;

        Assert.Equal(2, restored.Width);
        Assert.Equal(2, restored.Height);
    }

    [Fact]
    public void ImageConverter_CanConvert_reports_byte_array_support()
    {
        var converter = new ImageConverter();

        Assert.True(converter.CanConvertFrom(null, typeof(byte[])));
        Assert.True(converter.CanConvertTo(null, typeof(byte[])));
    }

    [Fact]
    public void An_icon_round_trips_through_IconConverter()
    {
        var iconBytes = TestIcon.OnePixelIco();
        var converter = new IconConverter();

        using var icon = (Icon)converter.ConvertFrom(null, null, iconBytes)!;
        var savedBack = (byte[])converter.ConvertTo(null, null, icon, typeof(byte[]))!;

        using var reloaded = (Icon)converter.ConvertFrom(null, null, savedBack)!;
        Assert.Equal(icon.Width, reloaded.Width);
        Assert.Equal(icon.Height, reloaded.Height);
    }

    [Theory]
    [InlineData("Png")]
    [InlineData("Jpeg")]
    [InlineData("Bmp")]
    [InlineData("Gif")]
    public void ImageFormatConverter_converts_a_known_name_both_ways(string name)
    {
        var converter = new ImageFormatConverter();

        var format = (ImageFormat)converter.ConvertFrom(null, null, name)!;
        var roundTripped = (string)converter.ConvertTo(null, null, format, typeof(string))!;

        Assert.Equal(name, roundTripped);
    }

    [Fact]
    public void ImageFormatConverter_rejects_a_name_GDI_plus_does_not_have()
    {
        var converter = new ImageFormatConverter();

        Assert.Throws<ArgumentException>(() => converter.ConvertFrom(null, null, "NotAFormat"));
    }

    [Fact]
    public void ImageFormatConverter_lists_every_named_format_as_a_standard_value()
    {
        var converter = new ImageFormatConverter();

        Assert.True(converter.GetStandardValuesSupported(null));

        var values = converter.GetStandardValues(null)!.Cast<ImageFormat>().ToList();

        Assert.Contains(values, f => f.Equals(ImageFormat.Png));
        Assert.Contains(values, f => f.Equals(ImageFormat.Jpeg));
        Assert.Contains(values, f => f.Equals(ImageFormat.MemoryBmp));
    }

    [Fact]
    public void BufferedGraphics_draws_nothing_on_the_target_before_Render()
    {
        using var target = new Bitmap(10, 10);
        using var targetGraphics = Graphics.FromImage(target);
        using var context = new BufferedGraphicsContext();

        using var buffered = context.Allocate(targetGraphics, new Rectangle(2, 3, 4, 4));
        buffered.Graphics.Clear(Color.FromArgb(255, 255, 0, 0));

        // Nothing drawn into the buffer has touched the target yet.
        Assert.Equal(Color.FromArgb(0, 0, 0, 0), target.GetPixel(3, 4));
    }

    [Fact]
    public void BufferedGraphics_Render_copies_the_buffer_at_the_rectangles_location()
    {
        using var target = new Bitmap(10, 10);
        using var targetGraphics = Graphics.FromImage(target);
        using var context = new BufferedGraphicsContext();

        var rectangle = new Rectangle(2, 3, 4, 4);
        using var buffered = context.Allocate(targetGraphics, rectangle);
        buffered.Graphics.Clear(Color.FromArgb(255, 255, 0, 0));

        buffered.Render();

        // Inside the rectangle, at its offset: red, as drawn into the buffer.
        Assert.Equal(Color.FromArgb(255, 255, 0, 0), target.GetPixel(rectangle.X + 1, rectangle.Y + 1));

        // Outside the rectangle: Render must not have touched it.
        Assert.Equal(Color.FromArgb(0, 0, 0, 0), target.GetPixel(0, 0));
    }

    [Fact]
    public void BufferedGraphics_Render_to_an_IntPtr_is_not_supported()
    {
        using var target = new Bitmap(10, 10);
        using var targetGraphics = Graphics.FromImage(target);
        using var context = new BufferedGraphicsContext();
        using var buffered = context.Allocate(targetGraphics, new Rectangle(0, 0, 4, 4));

        Assert.Throws<PlatformNotSupportedException>(() => buffered.Render(IntPtr.Zero));
    }

    [Fact]
    public void BufferedGraphicsContext_Allocate_by_handle_is_not_supported()
    {
        using var context = new BufferedGraphicsContext();

        Assert.Throws<PlatformNotSupportedException>(() => context.Allocate(IntPtr.Zero, new Rectangle(0, 0, 4, 4)));
    }

    [Fact]
    public void BufferedGraphicsManager_Current_is_the_same_context_on_the_same_thread()
    {
        Assert.Same(BufferedGraphicsManager.Current, BufferedGraphicsManager.Current);
    }

    [Fact]
    public void A_single_frame_bitmap_cannot_animate()
    {
        using var bitmap = new Bitmap(4, 4);

        Assert.False(ImageAnimator.CanAnimate(bitmap));
    }

    [Fact]
    public void CanAnimate_of_a_null_image_is_false()
    {
        Assert.False(ImageAnimator.CanAnimate(null));
    }

    [Fact]
    public void ToolboxBitmapAttribute_falls_back_to_a_16_by_16_default_image()
    {
        // No embedded resource exists for this test's own type, so GetImage
        // must fall back to the default image rather than throw.
        using var image = ToolboxBitmapAttribute.Default.GetImage(typeof(ConverterAndBufferTests));

        Assert.Equal(16, image.Width);
        Assert.Equal(16, image.Height);
    }

    [Fact]
    public void ToolboxBitmapAttribute_GetImageFromResource_also_falls_back_to_the_default_size()
    {
        using var image = ToolboxBitmapAttribute.GetImageFromResource(typeof(ConverterAndBufferTests), null, largeImage: false);

        Assert.Equal(16, image.Width);
        Assert.Equal(16, image.Height);
    }
    /// <summary>
    /// A two-frame GIF written byte by byte: one pixel, red then blue, shown
    /// for a tenth and then a fifth of a second, looping forever.
    /// </summary>
    private static byte[] TwoFrameGif()
    {
        byte[] Frame(byte delay, byte lzwFirstByte) =>
        [
            0x21, 0xF9, 0x04, 0x00, delay, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x02, 0x02, lzwFirstByte, 0x01, 0x00,
        ];

        return
        [
            .. "GIF89a"u8,
            0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00,
            0xFF, 0x00, 0x00, 0x00, 0x00, 0xFF,
            0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00,
            .. Frame(10, 0x44),
            .. Frame(20, 0x4C),
            0x3B,
        ];
    }

    [Fact]
    public void A_GIF_s_frame_delays_are_read_into_its_properties()
    {
        using var gif = new Bitmap(new MemoryStream(TwoFrameGif()));

        var delays = gif.GetPropertyItem(0x5100)!.Value;

        Assert.Equal(2, gif.GetFrameCount(System.Drawing.Imaging.FrameDimension.Time));
        Assert.Equal(10, BitConverter.ToInt32(delays, 0));
        Assert.Equal(20, BitConverter.ToInt32(delays, 4));
    }

    /// <summary>
    /// Animating calls back when a frame is due; UpdateFrames then moves the
    /// image on to it; after StopAnimate no more calls come.
    /// </summary>
    [Fact]
    public void An_animated_GIF_moves_to_its_next_frame_when_told()
    {
        using var gif = new Bitmap(new MemoryStream(TwoFrameGif()));
        using var due = new ManualResetEventSlim();
        EventHandler onFrame = (_, _) => due.Set();

        Assert.True(ImageAnimator.CanAnimate(gif));
        Assert.Equal(Color.Red.ToArgb(), gif.GetPixel(0, 0).ToArgb());

        ImageAnimator.Animate(gif, onFrame);
        try
        {
            Assert.True(due.Wait(TimeSpan.FromSeconds(5)), "No frame came due within five seconds.");

            ImageAnimator.UpdateFrames(gif);

            Assert.Equal(Color.Blue.ToArgb(), gif.GetPixel(0, 0).ToArgb());
        }
        finally
        {
            ImageAnimator.StopAnimate(gif, onFrame);
        }

        due.Reset();
        Assert.False(due.Wait(TimeSpan.FromMilliseconds(400)), "Frames kept coming after StopAnimate.");
    }
}

/// <summary>
/// A tiny one-image .ico file, built the same way IconTests builds its own
/// fixtures: a PNG entry wrapped in an ICONDIR, which Skia reads directly.
/// </summary>
internal static class TestIcon
{
    public static byte[] OnePixelIco()
    {
        using var bitmap = new Bitmap(1, 1);
        using var pngStream = new MemoryStream();
        bitmap.Save(pngStream, ImageFormat.Png);
        var png = pngStream.ToArray();

        var header = 6 + 16;
        var file = new byte[header + png.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);

        file[6] = 1; // Width
        file[7] = 1; // Height
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6 + 4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(6 + 6), 32);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(6 + 8), png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(6 + 12), header);
        png.CopyTo(file, header);

        return file;
    }

}
