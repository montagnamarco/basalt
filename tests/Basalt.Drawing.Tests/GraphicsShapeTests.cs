using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>The shape overloads: lines, rectangles, ellipses, arcs, pies, polygons and cardinal-spline curves.</summary>
public class GraphicsShapeTests
{
    private static readonly Color Ink = Color.FromArgb(255, 200, 30, 40);

    /// <summary>
    /// A rectangle expressed as a 4-point polygon must fill exactly the same
    /// pixels as FillRectangle: both go through AlignToPixels, and if a shape
    /// method forgot to call it, its output would land half a pixel away from
    /// FillRectangle's. Fractional coordinates are the case that actually
    /// tells the two apart: at whole coordinates, Skia's own path rasteriser
    /// already agrees with GDI+'s rule by coincidence (measured with and
    /// without AlignToPixels), but a fill starting at 2.3 only reaches the
    /// pixel centre at 3, exactly like FillRectangle(2.3f, ...).
    /// </summary>
    [Fact]
    public void FillPolygon_uses_the_same_pixel_rule_as_FillRectangle_for_the_same_rectangle()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillPolygon(brush, [new PointF(2.3f, 3.3f), new PointF(12.3f, 3.3f), new PointF(12.3f, 11.3f), new PointF(2.3f, 11.3f)]);
        }

        Assert.Equal((3, 4, 12, 11), InkBounds(bitmap));
        Assert.Equal(10 * 8, CountInk(bitmap));
    }

    /// <summary>
    /// A rectangle outline expressed as a 4-point closed polygon must stroke
    /// exactly the same pixels as DrawRectangle: both go through
    /// AlignToPixels, and without it a 1-pixel pen straddles two rows/columns
    /// instead of the crisp single one DrawRectangle produces (the same
    /// effect the existing DrawRectangle pixel-rule tests measure).
    /// </summary>
    [Fact]
    public void DrawPolygon_uses_the_same_pixel_rule_as_DrawRectangle_for_the_same_rectangle()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawPolygon(pen, [new PointF(2, 3), new PointF(12, 3), new PointF(12, 11), new PointF(2, 11)]);
        }

        Assert.Equal((2, 3, 12, 11), InkBounds(bitmap));
        Assert.Equal(2 * 11 + 2 * 7, CountInk(bitmap));
    }

    [Fact]
    public void FillMode_Winding_fills_through_a_same_direction_inner_ring_that_Alternate_leaves_as_a_hole()
    {
        PointF[] points =
        [
            new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0),
            new(2, 2), new(8, 2), new(8, 8), new(2, 8), new(2, 2),
        ];

        using var alternate = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(alternate))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillPolygon(brush, points, FillMode.Alternate);
        }

        using var winding = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(winding))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillPolygon(brush, points, FillMode.Winding);
        }

        // Between the two rings, both modes fill.
        Assert.Equal(Ink.ToArgb(), alternate.GetPixel(1, 1).ToArgb());
        Assert.Equal(Ink.ToArgb(), winding.GetPixel(1, 1).ToArgb());

        // Inside the inner ring: Alternate counts two crossings (a hole), Winding counts a winding number of two (still filled).
        Assert.Equal(0, alternate.GetPixel(5, 5).ToArgb());
        Assert.Equal(Ink.ToArgb(), winding.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void DrawLines_connects_every_consecutive_pair_of_points()
    {
        using var bitmap = new Bitmap(10, 10);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawLines(pen, [new Point(1, 1), new Point(8, 1), new Point(8, 8)]);
        }

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(4, 1).ToArgb());
        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(8, 4).ToArgb());
    }

    [Fact]
    public void FillEllipse_fills_its_centre_and_leaves_its_corners_untouched()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillEllipse(brush, 2, 2, 10, 10);
        }

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(7, 7).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(2, 2).ToArgb());
    }

    /// <summary>GDI+ measures arc angles in degrees, clockwise from the positive x axis (its y axis points down).</summary>
    [Fact]
    public void DrawArc_sweeps_clockwise_from_due_east()
    {
        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawArc(pen, 2, 2, 16, 16, 0, 90);
        }

        Assert.True(AnyInk(bitmap, 18, 10, 2), "No ink near due east, where a 0 degree arc should start.");
        Assert.True(AnyInk(bitmap, 10, 18, 2), "No ink near due south, where a 0-to-90 degree arc should end.");
        Assert.False(AnyInk(bitmap, 2, 10, 1), "Ink near due west, which a 0-to-90 degree sweep never reaches.");
    }

    [Fact]
    public void FillPie_fills_the_centre_of_the_oval_that_DrawArc_leaves_empty()
    {
        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillPie(brush, 2, 2, 16, 16, 0, 90);
        }

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
    }

    [Fact]
    public void DrawRectangles_and_FillRectangles_draw_every_rectangle_in_the_array()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillRectangles(brush, [new Rectangle(0, 0, 2, 2), new Rectangle(10, 10, 2, 2)]);
        }

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(0, 0).ToArgb());
        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void DrawBezier_starts_and_ends_near_its_endpoints()
    {
        using var bitmap = new Bitmap(20, 10);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawBezier(pen, new PointF(1, 5), new PointF(6, 0), new PointF(13, 9), new PointF(18, 5));
        }

        // Not the exact endpoint pixel: a diagonal, unantialiased, butt-capped
        // stroke does not always cover the exact pixel its path vertex falls
        // in (measured on the existing, unmodified DrawLine(1,1)-(10,10): it
        // covers its start pixel but not its end one). The right assertion
        // for a curve through these coordinates is "near", not "exactly on".
        Assert.True(AnyInk(bitmap, 1, 5, 1), "No ink near the Bezier's start point.");
        Assert.True(AnyInk(bitmap, 18, 5, 1), "No ink near the Bezier's end point.");
    }

    [Fact]
    public void DrawCurve_passes_near_every_point_it_is_given()
    {
        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawCurve(pen, [new Point(1, 10), new Point(8, 2), new Point(15, 10)]);
        }

        // See DrawBezier_starts_and_ends_near_its_endpoints for why "near" and not "exactly on".
        Assert.True(AnyInk(bitmap, 1, 10, 1), "No ink near the curve's first point.");
        Assert.True(AnyInk(bitmap, 8, 2, 1), "No ink near the curve's middle point.");
        Assert.True(AnyInk(bitmap, 15, 10, 1), "No ink near the curve's last point.");
    }

    [Fact]
    public void FillClosedCurve_fills_the_area_the_points_loop_around()
    {
        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillClosedCurve(brush, [new PointF(2, 10), new PointF(10, 2), new PointF(18, 10), new PointF(10, 18)]);
        }

        Assert.Equal(Ink.ToArgb(), bitmap.GetPixel(10, 10).ToArgb());
        Assert.Equal(0, bitmap.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void A_null_pen_is_rejected_by_name()
    {
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.Equal("pen", Assert.Throws<ArgumentNullException>(() => graphics.DrawLines(null!, [new Point(0, 0), new Point(1, 1)])).ParamName);
    }

    [Fact]
    public void A_null_brush_is_rejected_by_name()
    {
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.Equal("brush", Assert.Throws<ArgumentNullException>(() => graphics.FillEllipse(null!, 0, 0, 1, 1)).ParamName);
    }

    [Fact]
    public void Null_points_are_rejected_by_name()
    {
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);
        using var pen = new Pen(Ink);

        Assert.Equal("points", Assert.Throws<ArgumentNullException>(() => graphics.DrawPolygon(pen, (Point[])null!)).ParamName);
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

    private static bool AnyInk(Bitmap bitmap, int centerX, int centerY, int radius)
    {
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = centerX + dx;
                var y = centerY + dy;
                if (x < 0 || y < 0 || x >= bitmap.Width || y >= bitmap.Height) continue;
                if (bitmap.GetPixel(x, y).A != 0) return true;
            }
        }

        return false;
    }
}
