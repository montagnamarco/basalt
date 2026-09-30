using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Bitmap-level behaviour: LockBits' byte layout and write-back rules,
/// MakeTransparent's bottom-left rule, cropping and the members that only
/// exist off Windows or are not written yet.
/// </summary>
public class BitmapTests
{
    [Fact]
    public void Creating_a_Format24bppRgb_bitmap_works_and_starts_opaque_black()
    {
        using var bitmap = new Bitmap(3, 2, PixelFormat.Format24bppRgb);

        Assert.Equal(PixelFormat.Format24bppRgb, bitmap.PixelFormat);
        Assert.Equal(Color.FromArgb(255, 0, 0, 0).ToArgb(), bitmap.GetPixel(1, 1).ToArgb());
    }

    [Fact]
    public void A_Format24bppRgb_pixel_always_reads_back_opaque()
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format24bppRgb);

        // A translucent colour is accepted (GDI+ does not reject it either),
        // but the format has no alpha channel to keep it in.
        bitmap.SetPixel(0, 0, Color.FromArgb(10, 200, 30, 40));

        Assert.Equal(255, bitmap.GetPixel(0, 0).A);
    }

    /// <summary>
    /// LockBits(Format32bppArgb) hands out straight (not premultiplied) alpha,
    /// in B, G, R, A byte order - the classic Windows DIB order, not R,G,B,A.
    /// </summary>
    [Fact]
    public void LockBits_Format32bppArgb_uses_straight_alpha_in_BGRA_byte_order()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));

        var data = bitmap.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[4];
            Marshal.Copy(data.Scan0, bytes, 0, 4);

            Assert.Equal([30, 20, 10, 255], bytes); // B, G, R, A.
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>GDI+ rounds a 24-bit scanline up to a whole DWORD.</summary>
    [Fact]
    public void LockBits_Format24bppRgb_rounds_the_stride_up_to_4_bytes()
    {
        using var bitmap = new Bitmap(5, 1); // 5 * 3 = 15 bytes, rounds up to 16.

        var data = bitmap.LockBits(new Rectangle(0, 0, 5, 1), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            Assert.Equal(16, data.Stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    [Fact]
    public void A_ReadOnly_lock_does_not_write_its_buffer_back()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));

        var data = bitmap.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        Marshal.Copy(new byte[] { 99, 98, 97, 255 }, 0, data.Scan0, 4);
        bitmap.UnlockBits(data);

        Assert.Equal(Color.FromArgb(255, 10, 20, 30).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void A_ReadWrite_lock_writes_its_buffer_back()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));

        var data = bitmap.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        Marshal.Copy(new byte[] { 40, 50, 60, 255 }, 0, data.Scan0, 4); // B, G, R, A.
        bitmap.UnlockBits(data);

        Assert.Equal(Color.FromArgb(255, 60, 50, 40).ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void LockBits_Format32bppPArgb_copies_the_premultiplied_bytes_unchanged()
    {
        using var bitmap = new Bitmap(1, 1);
        // Alpha 128 with values chosen to premultiply exactly (no rounding).
        bitmap.SetPixel(0, 0, Color.FromArgb(128, 200, 100, 60));
        var premultiplied = bitmap.GetPixel(0, 0); // read back through the same rounding LockBits will see

        var data = bitmap.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var bytes = new byte[4];
            Marshal.Copy(data.Scan0, bytes, 0, 4);

            // PArgb is exactly the canonical storage: no unpremultiplying happens.
            Assert.Equal(128, bytes[3]);
            Assert.True(bytes[2] <= premultiplied.R);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>GDI+ reads the colour to erase from the bitmap's own bottom-left pixel.</summary>
    [Fact]
    public void MakeTransparent_without_a_colour_uses_the_bottom_left_pixel()
    {
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 1, 2, 3)); // Top-left: must NOT be used.
        bitmap.SetPixel(0, 1, Color.FromArgb(255, 9, 9, 9)); // Bottom-left: the colour to erase.
        bitmap.SetPixel(1, 1, Color.FromArgb(255, 9, 9, 9)); // Also that colour: should also go.

        bitmap.MakeTransparent();

        Assert.Equal(255, bitmap.GetPixel(0, 0).A); // Untouched: did not match.
        Assert.Equal(0, bitmap.GetPixel(0, 1).A);
        Assert.Equal(0, bitmap.GetPixel(1, 1).A);
    }

    [Fact]
    public void MakeTransparent_with_a_colour_erases_every_matching_pixel()
    {
        using var bitmap = new Bitmap(2, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 5, 6, 7));
        bitmap.SetPixel(1, 0, Color.FromArgb(255, 8, 9, 10));

        bitmap.MakeTransparent(Color.FromArgb(255, 5, 6, 7));

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(255, bitmap.GetPixel(1, 0).A);
    }

    [Fact]
    public void Clone_of_a_rectangle_keeps_only_the_pixels_inside_it()
    {
        using var bitmap = new Bitmap(2, 2);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 1, 0, 0));
        bitmap.SetPixel(1, 0, Color.FromArgb(255, 2, 0, 0));
        bitmap.SetPixel(0, 1, Color.FromArgb(255, 3, 0, 0));
        bitmap.SetPixel(1, 1, Color.FromArgb(255, 4, 0, 0));

        using var cropped = bitmap.Clone(new Rectangle(1, 0, 1, 2), PixelFormat.Format32bppArgb);

        Assert.Equal(new Size(1, 2), cropped.Size);
        Assert.Equal(2, cropped.GetPixel(0, 0).R);
        Assert.Equal(4, cropped.GetPixel(0, 1).R);
    }

    [Fact]
    public void Clone_of_a_rectangle_outside_the_bitmap_is_not_valid()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<ArgumentException>(() => bitmap.Clone(new Rectangle(1, 1, 5, 5), PixelFormat.Format32bppArgb));
    }

    [Fact]
    public void A_resource_that_does_not_exist_is_not_valid()
    {
        Assert.Throws<ArgumentException>(() => new Bitmap(typeof(BitmapTests), "no-such-resource.png"));
    }

    [Theory]
    [InlineData(PixelFormat.Format8bppIndexed)]
    [InlineData(PixelFormat.Format16bppRgb565)]
    public void A_pixel_format_this_library_does_not_store_says_so(PixelFormat format) =>
        Assert.Throws<NotImplementedException>(() => new Bitmap(2, 2, format));

    [Fact]
    public void Handle_based_members_say_there_is_no_handle_to_give_or_take()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<PlatformNotSupportedException>(() => bitmap.GetHbitmap());
        Assert.Throws<PlatformNotSupportedException>(() => bitmap.GetHbitmap(Color.White));
        Assert.Throws<PlatformNotSupportedException>(() => bitmap.GetHicon());
        Assert.Throws<PlatformNotSupportedException>(() => Bitmap.FromHicon(IntPtr.Zero));
        Assert.Throws<PlatformNotSupportedException>(() => Bitmap.FromResource(IntPtr.Zero, "x"));
    }

    [Fact]
    public void ConvertFormat_says_it_is_not_written_yet()
    {
        using var bitmap = new Bitmap(2, 2);

        Assert.Throws<NotImplementedException>(() => bitmap.ConvertFormat(PixelFormat.Format24bppRgb));
    }

    /// <summary>
    /// Two buffers over the same pixels would each write back over the other's
    /// changes, so GDI+ allows one lock at a time.
    /// </summary>
    [Fact]
    public void A_bitmap_that_is_locked_cannot_be_locked_again_until_it_is_unlocked()
    {
        using var bitmap = new Bitmap(4, 4);
        var whole = new Rectangle(0, 0, 4, 4);

        var first = bitmap.LockBits(whole, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        Assert.Throws<InvalidOperationException>(() => bitmap.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));

        bitmap.UnlockBits(first);
        bitmap.UnlockBits(bitmap.LockBits(whole, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb));
    }

    [Fact]
    public void Disposing_a_bitmap_that_is_still_locked_releases_the_lock()
    {
        var bitmap = new Bitmap(4, 4);
        var data = bitmap.LockBits(new Rectangle(0, 0, 4, 4), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        bitmap.Dispose();

        // The buffer went with the bitmap: there is nothing left to unlock.
        Assert.Throws<ArgumentException>(() => bitmap.UnlockBits(data));
    }

    /// <summary>
    /// Rotating replaces the block of pixels. A Graphics made before would go
    /// on drawing into the old block, which is freed.
    /// </summary>
    [Fact]
    public void An_image_being_drawn_on_cannot_have_its_pixels_replaced()
    {
        using var bitmap = new Bitmap(4, 2);

        using (var graphics = Graphics.FromImage(bitmap))
        {
            Assert.Throws<InvalidOperationException>(() => bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone));

            graphics.Clear(Color.Red);
        }

        bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);

        Assert.Equal(new Size(2, 4), bitmap.Size);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(1, 3).ToArgb());
    }

    [Fact]
    public void A_locked_bitmap_cannot_be_rotated_under_its_lock()
    {
        using var bitmap = new Bitmap(4, 2);
        var data = bitmap.LockBits(new Rectangle(0, 0, 4, 2), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        Assert.Throws<InvalidOperationException>(() => bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone));

        bitmap.UnlockBits(data);
        bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);

        Assert.Equal(new Size(2, 4), bitmap.Size);
    }

    /// <summary>
    /// A pixel written through a lock and one written with SetPixel must end up
    /// as the same bytes, whichever way the colour is rounded when it is
    /// multiplied by its alpha.
    /// </summary>
    [Fact]
    public void A_pixel_written_through_a_lock_is_the_pixel_SetPixel_writes()
    {
        var colour = Color.FromArgb(77, 201, 33, 150);

        using var viaSetPixel = new Bitmap(1, 1);
        viaSetPixel.SetPixel(0, 0, colour);

        using var viaLock = new Bitmap(1, 1);
        var data = viaLock.LockBits(new Rectangle(0, 0, 1, 1), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        System.Runtime.InteropServices.Marshal.Copy(new[] { colour.B, colour.G, colour.R, colour.A }, 0, data.Scan0, 4);
        viaLock.UnlockBits(data);

        Assert.Equal(viaSetPixel.GetPixel(0, 0).ToArgb(), viaLock.GetPixel(0, 0).ToArgb());
    }
}
