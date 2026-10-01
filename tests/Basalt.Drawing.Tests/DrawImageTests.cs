using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Where the pixels of an image land: copied one for one at its own size,
/// scaled to a rectangle, or mapped onto a parallelogram.
/// </summary>
public class DrawImageTests
{
    private static readonly Color[,] Source =
    {
        { Color.Red, Color.Lime, Color.Blue },
        { Color.Yellow, Color.Cyan, Color.Magenta },
    };

    /// <summary>
    /// The case every button with an icon depends on: at its own size and on
    /// whole pixels, every pixel arrives unchanged, not blended with its
    /// neighbour by the pixel-grid shift.
    /// </summary>
    [Fact]
    public void At_its_own_size_an_image_is_copied_pixel_for_pixel()
    {
        using var image = MakeSource();
        using var target = new Bitmap(8, 6);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.DrawImage(image, 2, 1);
        }

        AssertCopied(target, 2, 1, mirrored: false);
        Assert.Equal(0, target.GetPixel(1, 1).A);
        Assert.Equal(0, target.GetPixel(5, 1).A);
        Assert.Equal(0, target.GetPixel(2, 3).A);
    }

    [Fact]
    public void An_image_keeps_its_physical_size_at_another_resolution()
    {
        using var image = MakeSource();
        using var target = new Bitmap(10, 6);
        image.SetResolution(48, 48);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(image, 0, 0);
        }

        // At half of 96 dpi each pixel covers two.
        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), target.GetPixel(2, 0).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), target.GetPixel(5, 3).ToArgb());
        Assert.Equal(0, target.GetPixel(6, 0).A);
    }

    [Fact]
    public void Drawn_into_a_rectangle_an_image_is_scaled_to_fill_it()
    {
        using var image = MakeSource();
        using var target = new Bitmap(6, 4);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(image, new Rectangle(0, 0, 6, 4));
        }

        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 6; x++)
            {
                Assert.Equal(Source[y / 2, x / 2].ToArgb(), target.GetPixel(x, y).ToArgb());
            }
        }
    }

    [Fact]
    public void Three_points_map_the_image_onto_a_parallelogram()
    {
        using var image = MakeSource();
        using var target = new Bitmap(5, 4);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;

            // Upper-left on the right and upper-right on the left: a mirror image.
            graphics.DrawImage(image, new[] { new PointF(4, 1), new PointF(1, 1), new PointF(4, 3) });
        }

        AssertCopied(target, 1, 1, mirrored: true);
    }

    /// <summary>
    /// The lower-left point slants the image: each row starts further right
    /// by the slope, here a pixel and a half per row.
    /// </summary>
    [Fact]
    public void A_lower_left_point_to_the_right_slants_the_image()
    {
        using var image = MakeSource();
        using var target = new Bitmap(8, 3);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.DrawImage(image, new[] { new PointF(1, 0), new PointF(4, 0), new PointF(4, 2) });
        }

        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(2, 0).ToArgb());
        Assert.Equal(Color.Yellow.ToArgb(), target.GetPixel(3, 1).ToArgb());
        Assert.Equal(Color.Cyan.ToArgb(), target.GetPixel(4, 1).ToArgb());
    }

    [Fact]
    public void A_part_of_the_image_can_be_drawn_into_a_rectangle()
    {
        using var image = MakeSource();
        using var target = new Bitmap(4, 4);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.DrawImage(image, new Rectangle(1, 1, 2, 1), new Rectangle(1, 1, 2, 1), GraphicsUnit.Pixel);
        }

        Assert.Equal(Color.Cyan.ToArgb(), target.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Magenta.ToArgb(), target.GetPixel(2, 1).ToArgb());
        Assert.Equal(0, target.GetPixel(0, 1).A);
        Assert.Equal(0, target.GetPixel(1, 0).A);
    }

    [Fact]
    public void Unscaled_and_clipped_stops_at_the_rectangle()
    {
        using var image = MakeSource();
        using var target = new Bitmap(6, 4);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.DrawImageUnscaledAndClipped(image, new Rectangle(1, 1, 2, 5));
        }

        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(1, 1).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), target.GetPixel(2, 1).ToArgb());
        Assert.Equal(Color.Cyan.ToArgb(), target.GetPixel(2, 2).ToArgb());
        Assert.Equal(0, target.GetPixel(3, 1).A);
    }

    [Fact]
    public void An_image_obeys_the_transform_and_the_clip()
    {
        using var image = MakeSource();
        using var target = new Bitmap(8, 6);

        using (var graphics = Graphics.FromImage(target))
        {
            graphics.SetClip(new Rectangle(0, 0, 5, 6));
            graphics.TranslateTransform(3, 2);
            graphics.DrawImage(image, 0, 0);
        }

        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(3, 2).ToArgb());
        Assert.Equal(Color.Lime.ToArgb(), target.GetPixel(4, 2).ToArgb());

        // Column 5 is outside the clip.
        Assert.Equal(0, target.GetPixel(5, 2).A);
    }

    [Fact]
    public void Destination_points_must_be_three()
    {
        using var image = MakeSource();
        using var target = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(target);

        Assert.Throws<ArgumentException>(() => graphics.DrawImage(image, new[] { new PointF(0, 0), new PointF(1, 0) }));
        Assert.Throws<NotImplementedException>(() => graphics.DrawImage(image, new[] { new PointF(0, 0), new PointF(1, 0), new PointF(0, 1), new PointF(1, 1) }));
        Assert.Throws<ArgumentNullException>(() => graphics.DrawImage(null!, 0, 0));
        Assert.Throws<ArgumentException>(() => graphics.DrawImage(image, new Rectangle(0, 0, 2, 2), new Rectangle(0, 0, 0, 1), GraphicsUnit.Pixel));
    }

    private static Bitmap MakeSource()
    {
        var image = new Bitmap(3, 2);

        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                image.SetPixel(x, y, Source[y, x]);
            }
        }

        return image;
    }

    private static void AssertCopied(Bitmap target, int left, int top, bool mirrored)
    {
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var expected = Source[y, mirrored ? 2 - x : x];
                Assert.Equal(expected.ToArgb(), target.GetPixel(left + x, top + y).ToArgb());
            }
        }
    }
}
