using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Where GDI+ puts pixels, read back from the bitmap. These are the rules every
/// border of every control depends on.
/// </summary>
public class PixelRuleTests
{
    private static readonly Color Ink = Color.FromArgb(255, 200, 30, 40);

    [Fact]
    public void A_new_bitmap_is_transparent_black()
    {
        using var bitmap = new Bitmap(3, 2);

        Assert.Equal(new Size(3, 2), bitmap.Size);
        Assert.Equal(PixelFormat.Format32bppArgb, bitmap.PixelFormat);
        Assert.Equal(0, bitmap.GetPixel(2, 1).ToArgb());
    }

    [Fact]
    public void An_opaque_pixel_reads_back_as_it_was_set()
    {
        using var bitmap = new Bitmap(2, 2);

        bitmap.SetPixel(1, 0, Ink);

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Reading_outside_the_bitmap_names_the_coordinate_that_is_wrong()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Equal("x", Assert.Throws<ArgumentOutOfRangeException>(() => bitmap.GetPixel(2, 0)).ParamName);
        Assert.Equal("y", Assert.Throws<ArgumentOutOfRangeException>(() => bitmap.GetPixel(0, -1)).ParamName);
    }

    /// <summary>
    /// The rule WinForms code is written around: a rectangle of width 10 drawn
    /// with a pen of 1 is 11 pixels wide, crisp, and starts at the pixel asked
    /// for.
    /// </summary>
    [Fact]
    public void A_rectangle_outline_is_one_pixel_wider_than_its_width()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawRectangle(pen, 2, 3, 10, 8);
        }

        Assert.Equal((2, 3, 12, 11), InkBounds(bitmap));

        // One pixel thick and nothing blended into its neighbours.
        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(2, 3).ToArgb());
        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(12, 11).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(3, 4).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(1, 3).ToArgb());
        Assert.Equal(2 * 11 + 2 * 7, CountInk(bitmap));
    }

    [Fact]
    public void A_filled_rectangle_is_exactly_as_wide_as_its_width()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillRectangle(brush, 2, 3, 10, 8);
        }

        Assert.Equal((2, 3, 11, 10), InkBounds(bitmap));
        Assert.Equal(10 * 8, CountInk(bitmap));
    }

    /// <summary>
    /// The centre of a GDI+ pixel is on the whole coordinates: a fill starting
    /// at 2.3 does not reach the centre of pixel 2 and starts at pixel 3.
    /// </summary>
    [Fact]
    public void A_fill_covers_the_pixels_whose_centres_are_whole_coordinates_inside_it()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillRectangle(brush, 2.3f, 3.3f, 10, 8);
        }

        Assert.Equal((3, 4, 12, 11), InkBounds(bitmap));
        Assert.Equal(10 * 8, CountInk(bitmap));
    }

    /// <summary>
    /// With smoothing on, an outline on whole coordinates is still one solid
    /// pixel thick: it runs through pixel centres, not between two rows.
    /// </summary>
    [Fact]
    public void A_smoothed_outline_on_whole_coordinates_stays_crisp()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.DrawRectangle(pen, 2, 3, 10, 8);
        }

        Assert.Equal((2, 3, 12, 11), InkBounds(bitmap));
        Assert.Equal(2 * 11 + 2 * 7, CountInk(bitmap));
    }

    /// <summary>
    /// With PixelOffsetMode.Half the centre of a pixel moves to the halves, so
    /// the same outline straddles two rows of pixels and, without smoothing,
    /// lands half a pixel up and left.
    /// </summary>
    [Fact]
    public void Half_pixel_offset_moves_where_an_outline_lands()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawRectangle(pen, 2.5f, 3.5f, 10, 8);
        }

        Assert.Equal((2, 3, 12, 11), InkBounds(bitmap));
    }

    [Fact]
    public void Clear_paints_every_pixel()
    {
        using var bitmap = new Bitmap(4, 4);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Ink);
        }

        Assert.Equal(16, CountInk(bitmap));
    }

    [Fact]
    public void A_bitmap_saved_as_png_loads_with_the_same_pixels()
    {
        using var bitmap = new Bitmap(5, 4);
        bitmap.SetPixel(3, 2, Ink);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;

        using var loaded = new Bitmap(stream);

        Assert.Equal(new Size(5, 4), loaded.Size);
        Assert.Equal(Ink.ToArgb(), loaded.GetPixel(3, 2).ToArgb());
        Assert.Equal(0, loaded.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void There_is_no_device_context_to_hand_out()
    {
        using var bitmap = new Bitmap(2, 2);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.Throws<PlatformNotSupportedException>(() => graphics.GetHdc());
    }

    /// <summary>
    /// "Not written yet" and "cannot exist off Windows" are different answers,
    /// and code catches the second to mean exactly that.
    /// </summary>
    [Fact]
    public void What_is_not_implemented_yet_says_so_and_not_that_the_platform_cannot()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<NotImplementedException>(() => bitmap.Save(new MemoryStream(), ImageFormat.Tiff));
        Assert.Throws<NotImplementedException>(() => new Bitmap(2, 2, PixelFormat.Format8bppIndexed));
    }

    /// <summary>
    /// A canvas does not keep the pixels it draws into alive. Code that writes
    /// Graphics.FromImage(new Bitmap(...)) holds only the Graphics, and drawing
    /// into pixels the collector has freed ends the process.
    /// </summary>
    [Fact]
    public void A_graphics_keeps_the_image_it_draws_on_alive()
    {
        var (graphics, image) = GraphicsOverAnImageNobodyElseHolds();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.True(image.IsAlive, "The image was collected while a Graphics was still drawing on it.");

        using var brush = new SolidBrush(Ink);
        graphics.FillRectangle(brush, 0, 0, 4, 4);
        graphics.Dispose();
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static (Graphics Graphics, WeakReference Image) GraphicsOverAnImageNobodyElseHolds()
    {
        var bitmap = new Bitmap(8, 8);
        return (Graphics.FromImage(bitmap), new WeakReference(bitmap));
    }

    private static int CountInk(Bitmap bitmap)
    {
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == Ink.ToArgb()) count++;
            }
        }

        return count;
    }

    /// <summary>The first and last column and row holding any ink at all, blended or not.</summary>
    private static (int Left, int Top, int Right, int Bottom) InkBounds(Bitmap bitmap)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A == 0) continue;

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        return (left, top, right, bottom);
    }
}
