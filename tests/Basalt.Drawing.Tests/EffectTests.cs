using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Imaging.Effects;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The bitmap effects of System.Drawing.Imaging.Effects: applied in place by
/// Bitmap.ApplyEffect, drawn through by Graphics.DrawImage, and the pixel
/// cache of CachedBitmap.
/// </summary>
public class EffectTests
{
    [Fact]
    public void Invert_turns_red_into_cyan()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.Red);

        using var effect = new InvertEffect();
        bitmap.ApplyEffect(effect, Rectangle.Empty);

        Assert.Equal(Color.Cyan.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Grayscale_makes_every_channel_equal()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 200, 100, 50));

        using var effect = new GrayScaleEffect();
        bitmap.ApplyEffect(effect, Rectangle.Empty);

        var pixel = bitmap.GetPixel(0, 0);
        Assert.Equal(pixel.R, pixel.G);
        Assert.Equal(pixel.G, pixel.B);
    }

    [Fact]
    public void Brightness_raises_every_channel()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 100, 100, 100));

        using var effect = new BrightnessContrastEffect(50, 0);
        bitmap.ApplyEffect(effect, Rectangle.Empty);

        var pixel = bitmap.GetPixel(0, 0);
        Assert.True(pixel.R > 100, $"Expected a brighter red channel, got {pixel.R}.");
    }

    /// <summary>
    /// A blurred single bright pixel spreads into its neighbours: they must no
    /// longer be black, and the centre must no longer be at full brightness.
    /// </summary>
    [Fact]
    public void Blur_spreads_a_single_pixel_into_its_neighbours()
    {
        using var bitmap = new Bitmap(5, 5);
        for (var y = 0; y < 5; y++)
        {
            for (var x = 0; x < 5; x++) bitmap.SetPixel(x, y, Color.Black);
        }

        bitmap.SetPixel(2, 2, Color.White);

        using var effect = new BlurEffect(2f, expandEdge: true);
        bitmap.ApplyEffect(effect, Rectangle.Empty);

        Assert.True(bitmap.GetPixel(2, 2).R < 255, "The centre pixel should have lost some of its own brightness to its neighbours.");
        Assert.True(bitmap.GetPixel(1, 2).R > 0, "A neighbour should have gained some brightness from the blur.");
        Assert.True(bitmap.GetPixel(2, 1).R > 0, "A neighbour should have gained some brightness from the blur.");
    }

    [Fact]
    public void ApplyEffect_on_a_sub_rectangle_leaves_the_rest_unchanged()
    {
        using var bitmap = new Bitmap(3, 1);
        bitmap.SetPixel(0, 0, Color.Red);
        bitmap.SetPixel(1, 0, Color.Red);
        bitmap.SetPixel(2, 0, Color.Red);

        using var effect = new InvertEffect();
        bitmap.ApplyEffect(effect, new Rectangle(1, 0, 1, 1));

        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Color.Cyan.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
    }

    [Fact]
    public void ApplyEffect_refuses_a_bitmap_with_an_outstanding_lock()
    {
        using var bitmap = new Bitmap(2, 2);
        var data = bitmap.LockBits(new Rectangle(0, 0, 2, 2), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);

        using var effect = new InvertEffect();
        Assert.Throws<InvalidOperationException>(() => bitmap.ApplyEffect(effect, Rectangle.Empty));

        bitmap.UnlockBits(data);
    }

    [Fact]
    public void A_color_lookup_table_remaps_each_channel_independently()
    {
        using var bitmap = new Bitmap(1, 1);
        bitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));

        var zero = new byte[256];
        var identity = new byte[256];
        for (var i = 0; i < 256; i++) identity[i] = (byte)i;

        // Red is zeroed, green and blue and alpha pass through unchanged.
        using var effect = new ColorLookupTableEffect(zero, identity, identity, identity);
        bitmap.ApplyEffect(effect, Rectangle.Empty);

        var pixel = bitmap.GetPixel(0, 0);
        Assert.Equal(0, pixel.R);
        Assert.Equal(20, pixel.G);
        Assert.Equal(30, pixel.B);
    }

    [Fact]
    public void A_color_lookup_table_needs_256_entries()
    {
        var tooShort = new byte[10];
        var full = new byte[256];

        Assert.Throws<ArgumentException>(() => new ColorLookupTableEffect(tooShort, full, full, full));
    }

    [Theory]
    [InlineData(-256, 0)]
    [InlineData(256, 0)]
    [InlineData(0, -101)]
    [InlineData(0, 101)]
    public void BrightnessContrast_refuses_a_level_out_of_range(int brightness, int contrast)
    {
        Assert.Throws<ArgumentException>(() => new BrightnessContrastEffect(brightness, contrast));
    }

    [Fact]
    public void Blur_refuses_a_radius_out_of_range()
    {
        Assert.Throws<ArgumentException>(() => new BlurEffect(-1f, false));
        Assert.Throws<ArgumentException>(() => new BlurEffect(256f, false));
    }

    [Fact]
    public void Sharpen_refuses_an_amount_out_of_range()
    {
        Assert.Throws<ArgumentException>(() => new SharpenEffect(1f, -1f));
        Assert.Throws<ArgumentException>(() => new SharpenEffect(1f, 101f));
    }

    [Fact]
    public void Tint_refuses_a_hue_out_of_range()
    {
        Assert.Throws<ArgumentException>(() => new TintEffect(-1, 50));
        Assert.Throws<ArgumentException>(() => new TintEffect(360, 50));
    }

    [Fact]
    public void DrawImage_through_an_effect_recolours_the_drawn_pixels()
    {
        using var source = new Bitmap(1, 1);
        source.SetPixel(0, 0, Color.Red);
        using var target = new Bitmap(1, 1);

        using (var graphics = Graphics.FromImage(target))
        using (var effect = new InvertEffect())
        {
            graphics.DrawImage(source, effect);
        }

        Assert.Equal(Color.Cyan.ToArgb(), target.GetPixel(0, 0).ToArgb());

        // The source bitmap itself must be untouched: the effect runs only on
        // what gets drawn, like Bitmap.ApplyEffect was never called on it.
        Assert.Equal(Color.Red.ToArgb(), source.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void DrawCachedBitmap_copies_pixels_one_for_one()
    {
        using var source = new Bitmap(2, 2);
        source.SetPixel(0, 0, Color.Red);
        source.SetPixel(1, 0, Color.Lime);
        source.SetPixel(0, 1, Color.Blue);
        source.SetPixel(1, 1, Color.Yellow);

        using var target = new Bitmap(4, 4);

        using (var graphics = Graphics.FromImage(target))
        using (var cached = new CachedBitmap(source, graphics))
        {
            // Change the source after caching: the cache must not see this.
            source.SetPixel(0, 0, Color.Black);

            graphics.DrawCachedBitmap(cached, 1, 1);
        }

        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), target.GetPixel(2, 1).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), target.GetPixel(1, 2).ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), target.GetPixel(2, 2).ToArgb());
    }

    /// <summary>DrawCachedBitmap ignores the world transform's rotation and scale, keeping only its translation, as GDI+ documents.</summary>
    [Fact]
    public void DrawCachedBitmap_ignores_scaling_in_the_world_transform()
    {
        using var source = new Bitmap(1, 1);
        source.SetPixel(0, 0, Color.Red);

        using var target = new Bitmap(4, 4);

        using (var graphics = Graphics.FromImage(target))
        using (var cached = new CachedBitmap(source, graphics))
        {
            graphics.ScaleTransform(2f, 2f);
            graphics.DrawCachedBitmap(cached, 1, 1);
        }

        // At 2x scale a normal DrawImage would land at (2,2); DrawCachedBitmap
        // must still land at (1,1), unscaled.
        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(1, 1).ToArgb());
        Assert.Equal(0, target.GetPixel(2, 2).A);
    }
}
