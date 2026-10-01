using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The colour of a path gradient along each ray from its centre to its edge.
/// </summary>
public class PathGradientBrushTests
{
    private static readonly PointF[] Square = [new(0, 0), new(40, 0), new(40, 40), new(0, 40)];

    [Fact]
    public void The_colour_runs_from_the_centre_to_the_edge()
    {
        using var brush = new PathGradientBrush(Square) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };

        var bitmap = Fill(brush, 40, 40);

        AssertNear(Color.Blue, bitmap.GetPixel(20, 20), 12);
        AssertNear(Color.Red, bitmap.GetPixel(0, 20), 16);

        // Half way along the ray from the centre to the left edge.
        AssertNear(Color.FromArgb(128, 0, 128), bitmap.GetPixel(10, 20), 16);
    }

    [Fact]
    public void A_shape_away_from_the_origin_has_its_centre_where_it_is()
    {
        PointF[] far = [new(50, 50), new(90, 50), new(90, 90), new(50, 90)];
        using var brush = new PathGradientBrush(far) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };

        var bitmap = Fill(brush, 100, 100);

        AssertNear(Color.Blue, bitmap.GetPixel(70, 70), 12);
        AssertNear(Color.Red, bitmap.GetPixel(50, 70), 16);
        Assert.Equal(0, bitmap.GetPixel(45, 70).A);
    }

    [Fact]
    public void Outside_the_shape_a_clamped_gradient_paints_nothing()
    {
        PointF[] triangle = [new(0, 0), new(20, 0), new(0, 20)];
        using var brush = new PathGradientBrush(triangle) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };

        var bitmap = Fill(brush, 30, 30);

        Assert.Equal(0, bitmap.GetPixel(18, 18).A);
        Assert.Equal(0, bitmap.GetPixel(25, 5).A);
        Assert.Equal(255, bitmap.GetPixel(4, 4).A);
    }

    /// <summary>
    /// Beside a square, where its edge colour would be smeared outwards if the
    /// gradient were clamped like a texture, nothing is painted.
    /// </summary>
    [Fact]
    public void Beside_the_shape_the_edge_colour_does_not_spread()
    {
        PointF[] small = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        using var brush = new PathGradientBrush(small) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };

        var bitmap = Fill(brush, 20, 20);

        Assert.Equal(0, bitmap.GetPixel(15, 5).A);
        Assert.Equal(0, bitmap.GetPixel(5, 15).A);
    }

    [Fact]
    public void Each_corner_takes_its_own_surround_colour()
    {
        PointF[] triangle = [new(0, 0), new(40, 0), new(0, 40)];
        using var brush = new PathGradientBrush(triangle)
        {
            CenterColor = Color.White,
            SurroundColors = [Color.Red, Color.Lime, Color.Blue],
        };

        var bitmap = Fill(brush, 40, 40);

        Assert.True(bitmap.GetPixel(1, 1).R > 200 && bitmap.GetPixel(1, 1).G < 120);
        Assert.True(bitmap.GetPixel(37, 1).G > 200 && bitmap.GetPixel(37, 1).R < 120);
        Assert.True(bitmap.GetPixel(1, 37).B > 200 && bitmap.GetPixel(1, 37).R < 120);
    }

    [Fact]
    public void The_default_centre_is_the_centre_of_the_area()
    {
        using var brush = new PathGradientBrush(Square);

        Assert.Equal(new PointF(20, 20), brush.CenterPoint);
        Assert.Equal(new RectangleF(0, 0, 40, 40), brush.Rectangle);
    }

    /// <summary>
    /// A triangular blend peaking half way: the centre colour on the ring half
    /// way in, the edge colour both at the edge and at the very centre.
    /// </summary>
    [Fact]
    public void A_triangular_blend_peaks_where_its_focus_is()
    {
        using var brush = new PathGradientBrush(Square) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };
        brush.SetBlendTriangularShape(0.5f);

        var bitmap = Fill(brush, 40, 40);

        AssertNear(Color.Red, bitmap.GetPixel(20, 20), 24);
        AssertNear(Color.Blue, bitmap.GetPixel(10, 20), 24);
    }

    [Fact]
    public void Inside_the_focus_scales_the_centre_colour_is_solid()
    {
        using var brush = new PathGradientBrush(Square)
        {
            CenterColor = Color.Blue,
            SurroundColors = [Color.Red],
            FocusScales = new PointF(0.5f, 0.5f),
        };

        var bitmap = Fill(brush, 40, 40);

        AssertNear(Color.Blue, bitmap.GetPixel(14, 20), 4);
        AssertNear(Color.Blue, bitmap.GetPixel(25, 25), 4);
        Assert.True(bitmap.GetPixel(4, 20).R > 100);
    }

    [Fact]
    public void Interpolation_colours_replace_the_centre_and_surround_colours()
    {
        using var brush = new PathGradientBrush(Square) { CenterColor = Color.Black, SurroundColors = [Color.Black] };
        brush.InterpolationColors = new ColorBlend(3) { Colors = [Color.Red, Color.Lime, Color.Blue], Positions = [0f, 0.5f, 1f] };

        var bitmap = Fill(brush, 40, 40);

        AssertNear(Color.Blue, bitmap.GetPixel(20, 20), 24);
        AssertNear(Color.Lime, bitmap.GetPixel(10, 20), 24);
    }

    [Fact]
    public void A_tiled_gradient_repeats_past_its_rectangle()
    {
        PointF[] small = [new(0, 0), new(10, 0), new(10, 10), new(0, 10)];
        using var brush = new PathGradientBrush(small, WrapMode.Tile) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };

        var bitmap = Fill(brush, 20, 20);

        Assert.Equal(bitmap.GetPixel(3, 4).ToArgb(), bitmap.GetPixel(13, 14).ToArgb());
        Assert.Equal(255, bitmap.GetPixel(13, 14).A);
    }

    [Fact]
    public void A_concave_shape_is_covered_with_no_holes()
    {
        PointF[] shapeL = [new(0, 0), new(10, 0), new(10, 30), new(30, 30), new(30, 40), new(0, 40)];
        using var brush = new PathGradientBrush(shapeL) { CenterColor = Color.Blue, SurroundColors = [Color.Red] };
        using var path = new GraphicsPath();
        path.AddPolygon(shapeL);

        var bitmap = new Bitmap(40, 40);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.FillPath(brush, path);
        }

        for (var y = 1; y < 39; y++)
        {
            for (var x = 1; x < 29; x++)
            {
                if (path.IsVisible(x, y) && path.IsVisible(x - 1, y) && path.IsVisible(x + 1, y))
                {
                    Assert.True(bitmap.GetPixel(x, y).A == 255, $"Pixel ({x}, {y}) inside the shape was left empty.");
                }
            }
        }
    }

    [Fact]
    public void A_path_with_two_figures_takes_surround_colours_across_both()
    {
        using var path = new GraphicsPath();
        path.AddPolygon(new[] { new PointF(0, 0), new PointF(20, 0), new PointF(0, 20) });
        path.AddPolygon(new[] { new PointF(40, 40), new PointF(20, 40), new PointF(40, 20) });
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(20, 20),
            CenterColor = Color.White,
            SurroundColors = [Color.Red, Color.Red, Color.Red, Color.Blue, Color.Blue, Color.Blue],
        };

        var bitmap = Fill(brush, 40, 40);

        Assert.True(bitmap.GetPixel(2, 2).R > 200 && bitmap.GetPixel(2, 2).B < 120);
        Assert.True(bitmap.GetPixel(37, 37).B > 200 && bitmap.GetPixel(37, 37).R < 120);
        Assert.Throws<ArgumentException>(() => brush.SurroundColors = new Color[7]);
    }

    [Fact]
    public void A_clone_and_a_blend_read_back_are_the_brush_s_own_no_longer()
    {
        using var brush = new PathGradientBrush(Square);
        brush.SetBlendTriangularShape(0.5f);

        using var clone = (PathGradientBrush)brush.Clone();
        clone.Blend.Factors[1] = 0f;
        var read = brush.Blend;
        read.Factors[1] = 0f;

        Assert.Equal(1f, brush.Blend.Factors[1]);
    }

    [Fact]
    public void More_surround_colours_than_points_are_refused()
    {
        PointF[] triangle = [new(0, 0), new(10, 0), new(0, 10)];
        using var brush = new PathGradientBrush(triangle);

        Assert.Throws<ArgumentException>(() => brush.SurroundColors = [Color.Red, Color.Red, Color.Red, Color.Red]);
    }

    private static Bitmap Fill(Brush brush, int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.FillRectangle(brush, 0, 0, width, height);

        return bitmap;
    }

    private static void AssertNear(Color expected, Color actual, int tolerance)
    {
        Assert.True(
            Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance,
            $"Expected about {expected}, got {actual}.");
    }
}
