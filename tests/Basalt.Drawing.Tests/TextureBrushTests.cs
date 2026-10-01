using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>TextureBrush: tiling, the four WrapMode flips, Transform and the source rectangle.</summary>
public class TextureBrushTests
{
    private static readonly Color Red = Color.FromArgb(255, 220, 20, 20);
    private static readonly Color Blue = Color.FromArgb(255, 20, 20, 220);

    private static Bitmap TwoPixelSource()
    {
        var source = new Bitmap(2, 1);
        source.SetPixel(0, 0, Red);
        source.SetPixel(1, 0, Blue);
        return source;
    }

    [Fact]
    public void WrapMode_Tile_repeats_the_image_without_flipping()
    {
        using var source = TwoPixelSource();
        using var bitmap = new Bitmap(4, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new TextureBrush(source, WrapMode.Tile))
        {
            graphics.FillRectangle(brush, 0, 0, 4, 1);
        }

        Assert.Equal(Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
        Assert.Equal(Red.ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(3, 0).ToArgb());
    }

    /// <summary>
    /// WrapMode.TileFlipX mirrors every other copy of the image along x:
    /// after one image width, the column order reverses instead of repeating.
    /// Reintroducing the defect (mapping TileFlipX to Repeat/Repeat instead
    /// of Mirror/Repeat) was confirmed to fail this assertion before being
    /// fixed back.
    /// </summary>
    [Fact]
    public void WrapMode_TileFlipX_mirrors_the_second_tile()
    {
        using var source = TwoPixelSource();
        using var bitmap = new Bitmap(4, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new TextureBrush(source, WrapMode.TileFlipX))
        {
            graphics.FillRectangle(brush, 0, 0, 4, 1);
        }

        // First tile: Red, Blue, in source order.
        Assert.Equal(Red.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());

        // Second tile: mirrored, so Blue, Red instead of Red, Blue again.
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(2, 0).ToArgb());
        Assert.Equal(Red.ToArgb(), bitmap.GetPixel(3, 0).ToArgb());
    }

    [Fact]
    public void A_source_rectangle_crops_the_image_before_tiling()
    {
        using var source = TwoPixelSource();
        using var bitmap = new Bitmap(2, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new TextureBrush(source, WrapMode.Tile, new Rectangle(1, 0, 1, 1)))
        {
            graphics.FillRectangle(brush, 0, 0, 2, 1);
        }

        // Cropped to the single blue pixel: both columns are blue.
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Blue.ToArgb(), bitmap.GetPixel(1, 0).ToArgb());
    }

    [Fact]
    public void The_Image_property_returns_the_image_the_brush_was_built_from()
    {
        using var source = TwoPixelSource();
        using var brush = new TextureBrush(source);

        Assert.Same(source, brush.Image);
    }

    [Fact]
    public void A_null_image_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new TextureBrush(null!));
    }

    /// <summary>
    /// GDI+ answers OutOfMemory for a rectangle outside the image, and that is
    /// the exception code written against System.Drawing catches.
    /// </summary>
    [Fact]
    public void A_source_rectangle_outside_the_image_is_rejected()
    {
        using var image = new Bitmap(4, 4);

        Assert.Throws<OutOfMemoryException>(() => new TextureBrush(image, System.Drawing.Drawing2D.WrapMode.Tile, new RectangleF(2, 2, 4, 4)));
    }

    /// <summary>The colour matrix of the attributes recolours the texture once, when the brush is made.</summary>
    [Fact]
    public void Image_attributes_recolour_the_texture()
    {
        using var image = new Bitmap(2, 2);
        using (var graphics = Graphics.FromImage(image)) graphics.Clear(Color.Red);
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix([[0, 0, 1, 0, 0], [0, 1, 0, 0, 0], [1, 0, 0, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));
        using var brush = new TextureBrush(image, new Rectangle(0, 0, 2, 2), attributes);
        using var target = new Bitmap(4, 4);

        using (var graphics = Graphics.FromImage(target)) graphics.FillRectangle(brush, 0, 0, 4, 4);

        Assert.Equal(Color.Blue.ToArgb(), target.GetPixel(3, 3).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), image.GetPixel(0, 0).ToArgb());
    }
}
