using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>LinearGradientBrush: the colour at each end, WrapMode, Blend and the rectangle-and-angle constructors.</summary>
public class LinearGradientBrushTests
{
    private static readonly Color Start = Color.FromArgb(255, 255, 0, 0);
    private static readonly Color End = Color.FromArgb(255, 0, 0, 255);

    /// <summary>
    /// The pixel on the gradient's start point shows LinearColors[0] and the
    /// one on its end point LinearColors[1]. Within a few levels per channel:
    /// where inside a pixel GDI+ reads a gradient (the whole coordinate, or
    /// half a pixel on) has not been compared with Windows, and the two differ
    /// by that much here. Clamp keeps the end from wrapping round to the start.
    /// </summary>
    [Fact]
    public void The_gradient_is_the_start_colour_at_its_start_point_and_the_end_colour_at_its_end_point()
    {
        using var bitmap = new Bitmap(10, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(9, 0), Start, End))
        {
            brush.WrapMode = WrapMode.Clamp;
            graphics.FillRectangle(brush, 0, 0, 10, 1);
        }

        AssertClose(Start, bitmap.GetPixel(0, 0));
        AssertClose(End, bitmap.GetPixel(9, 0));
    }

    [Fact]
    public void Two_points_that_coincide_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new LinearGradientBrush(new PointF(1, 1), new PointF(1, 1), Start, End));
    }

    [Fact]
    public void LinearColors_reports_the_two_colours_given_to_the_constructor()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.Equal([Start.ToArgb(), End.ToArgb()], Array.ConvertAll(brush.LinearColors, c => c.ToArgb()));
    }

    [Fact]
    public void LinearColors_setter_requires_exactly_two_colours()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.Throws<ArgumentException>(() => brush.LinearColors = [Start]);
        Assert.Throws<ArgumentException>(() => brush.LinearColors = [Start, End, Start]);
    }

    [Fact]
    public void InterpolationColors_overrides_the_two_plain_LinearColors()
    {
        var green = Color.FromArgb(255, 0, 255, 0);

        using var bitmap = new Bitmap(19, 1);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(18, 0), Start, End))
        {
            brush.InterpolationColors = new ColorBlend(3) { Colors = [Start, green, End], Positions = [0f, 0.5f, 1f] };
            graphics.FillRectangle(brush, 0, 0, 19, 1);
        }

        // Pixel 9 is the middle of the line from 0 to 18: green, which the
        // plain red-to-blue gradient never shows.
        AssertClose(green, bitmap.GetPixel(9, 0));
    }

    [Fact]
    public void A_rectangle_and_LinearGradientMode_Horizontal_runs_left_to_right()
    {
        using var bitmap = new Bitmap(10, 4);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new LinearGradientBrush(new Rectangle(0, 0, 10, 4), Start, End, LinearGradientMode.Horizontal))
        {
            graphics.FillRectangle(brush, 0, 0, 10, 4);
        }

        // The rectangle runs from 0 to 10: pixel 0 is its start, and the
        // colour moves towards the end colour column by column.
        AssertClose(Start, bitmap.GetPixel(0, 2));

        for (var x = 1; x < 10; x++)
        {
            Assert.True(bitmap.GetPixel(x, 2).B > bitmap.GetPixel(x - 1, 2).B, $"Column {x} is not closer to the end colour than column {x - 1}.");
        }

        // The same colour down each column: a horizontal gradient does not vary with y.
        Assert.Equal(bitmap.GetPixel(5, 0).ToArgb(), bitmap.GetPixel(5, 3).ToArgb());
    }

    [Fact]
    public void WrapMode_defaults_to_Tile_and_is_stored_when_set()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.Equal(WrapMode.Tile, brush.WrapMode);

        brush.WrapMode = WrapMode.Clamp;
        Assert.Equal(WrapMode.Clamp, brush.WrapMode);
    }

    [Fact]
    public void Blend_setter_requires_matching_Factors_and_Positions_lengths()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.Throws<ArgumentException>(() => brush.Blend = new Blend { Factors = [0f, 1f], Positions = [0f] });
    }

    [Fact]
    public void SetBlendTriangularShape_rejects_a_focus_or_scale_outside_0_to_1()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.Throws<ArgumentException>(() => brush.SetBlendTriangularShape(1.5f));
        Assert.Throws<ArgumentException>(() => brush.SetBlendTriangularShape(0.5f, -1f));
    }

    [Fact]
    public void GammaCorrection_is_stored_even_though_it_does_not_change_the_rendered_colours()
    {
        using var brush = new LinearGradientBrush(new PointF(0, 0), new PointF(1, 0), Start, End);

        Assert.False(brush.GammaCorrection);
        brush.GammaCorrection = true;
        Assert.True(brush.GammaCorrection);
    }

    /// <summary>Equal within four levels per channel: see the first test for why.</summary>
    private static void AssertClose(Color expected, Color actual)
    {
        Assert.True(
            Math.Abs(expected.A - actual.A) <= 4 && Math.Abs(expected.R - actual.R) <= 4 &&
            Math.Abs(expected.G - actual.G) <= 4 && Math.Abs(expected.B - actual.B) <= 4,
            $"Expected about {expected}, got {actual}.");
    }
}
