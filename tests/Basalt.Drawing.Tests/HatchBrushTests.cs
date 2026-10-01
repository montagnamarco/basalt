using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>HatchBrush: the 8x8 tiled pattern, its two colours, and Clone.</summary>
public class HatchBrushTests
{
    private static readonly Color Fore = Color.FromArgb(255, 200, 30, 40);
    private static readonly Color Back = Color.FromArgb(255, 10, 20, 250);

    /// <summary>
    /// HatchStyle.Horizontal's line sits on row 3 of every 8-pixel tile: every
    /// other row of the tile is background, and the pattern repeats from
    /// (0, 0). Reintroducing the defect (using row 0 instead of row 3 in
    /// HatchPatterns.Rows) was confirmed to fail this assertion before being
    /// fixed back.
    /// </summary>
    [Fact]
    public void Horizontal_fills_row_3_of_every_8_pixel_tile_and_leaves_the_rest_background()
    {
        using var bitmap = new Bitmap(8, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new HatchBrush(HatchStyle.Horizontal, Fore, Back))
        {
            graphics.FillRectangle(brush, 0, 0, 8, 16);
        }

        for (var y = 0; y < 16; y++)
        {
            var expected = y % 8 == 3 ? Fore : Back;
            Assert.Equal(expected.ToArgb(), bitmap.GetPixel(0, y).ToArgb());
        }
    }

    [Fact]
    public void Vertical_fills_column_3_of_every_8_pixel_tile()
    {
        using var bitmap = new Bitmap(16, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new HatchBrush(HatchStyle.Vertical, Fore, Back))
        {
            graphics.FillRectangle(brush, 0, 0, 16, 8);
        }

        for (var x = 0; x < 16; x++)
        {
            var expected = x % 8 == 3 ? Fore : Back;
            Assert.Equal(expected.ToArgb(), bitmap.GetPixel(x, 0).ToArgb());
        }
    }

    [Fact]
    public void The_default_background_colour_is_black()
    {
        using var brush = new HatchBrush(HatchStyle.Horizontal, Fore);

        Assert.Equal(Color.Black.ToArgb(), brush.BackgroundColor.ToArgb());
        Assert.Equal(Fore.ToArgb(), brush.ForegroundColor.ToArgb());
        Assert.Equal(HatchStyle.Horizontal, brush.HatchStyle);
    }

    [Fact]
    public void Clone_keeps_the_same_style_and_colours()
    {
        using var brush = new HatchBrush(HatchStyle.Cross, Fore, Back);
        using var clone = (HatchBrush)brush.Clone();

        Assert.Equal(brush.HatchStyle, clone.HatchStyle);
        Assert.Equal(brush.ForegroundColor.ToArgb(), clone.ForegroundColor.ToArgb());
        Assert.Equal(brush.BackgroundColor.ToArgb(), clone.BackgroundColor.ToArgb());
    }

    [Fact]
    public void Cross_fills_both_the_row_and_the_column_of_its_tile()
    {
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new HatchBrush(HatchStyle.Cross, Fore, Back))
        {
            graphics.FillRectangle(brush, 0, 0, 8, 8);
        }

        // Row 3 is entirely foreground (the horizontal bar of the cross).
        for (var x = 0; x < 8; x++) Assert.Equal(Fore.ToArgb(), bitmap.GetPixel(x, 3).ToArgb());

        // Column 3 is entirely foreground (the vertical bar), elsewhere on that row.
        for (var y = 0; y < 8; y++) Assert.Equal(Fore.ToArgb(), bitmap.GetPixel(3, y).ToArgb());

        // A corner belongs to neither bar.
        Assert.Equal(Back.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
    }

    /// <summary>A dashed row is the left half of the plain row, not the whole row.</summary>
    [Fact]
    public void DashedHorizontal_keeps_only_the_left_half_of_its_row()
    {
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.DashedHorizontal, Color.Black, Color.White))
        {
            graphics.FillRectangle(brush, 0, 0, 8, 8);
        }

        var black = Enumerable.Range(0, 8).Count(x => bitmap.GetPixel(x, 3).R == 0);
        Assert.Equal(4, black);
        Assert.Equal(0, bitmap.GetPixel(0, 3).R);
        Assert.Equal(255, bitmap.GetPixel(7, 3).R);
    }
}
