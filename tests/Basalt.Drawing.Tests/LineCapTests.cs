using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The two ends of a line: each the cap the pen gives it, a shape from a path,
/// or an arrow head, without the line growing darker where they overlap.
/// </summary>
public class LineCapTests
{
    private static readonly Color Ink = Color.FromArgb(255, 20, 40, 160);

    [Fact]
    public void The_start_and_the_end_of_a_line_take_their_own_caps()
    {
        using var bitmap = new Bitmap(40, 20);
        using var pen = new Pen(Ink, 6) { StartCap = LineCap.Flat, EndCap = LineCap.Round };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 10, 10, 30, 10));

        // Flat: nothing before the start. Round: a half disc of radius 3 past the end.
        Assert.Equal(0, bitmap.GetPixel(8, 10).A);
        Assert.Equal(255, bitmap.GetPixel(32, 10).A);
        Assert.Equal(0, bitmap.GetPixel(34, 10).A);
    }

    [Fact]
    public void A_triangle_cap_reaches_half_a_pen_width_past_the_end_at_its_point()
    {
        using var bitmap = new Bitmap(40, 20);
        using var pen = new Pen(Ink, 8) { StartCap = LineCap.Triangle, EndCap = LineCap.Triangle };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 10, 10, 30, 10));

        Assert.Equal(255, bitmap.GetPixel(32, 10).A);
        Assert.Equal(0, bitmap.GetPixel(32, 7).A);
        Assert.Equal(0, bitmap.GetPixel(35, 10).A);
    }

    [Fact]
    public void An_arrow_anchor_is_wider_than_the_line_it_ends()
    {
        using var bitmap = new Bitmap(60, 30);
        using var pen = new Pen(Ink, 4) { EndCap = LineCap.ArrowAnchor };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 5, 15, 50, 15));

        // Two pen widths long and wide: its back is at x = 42, twice as wide as the line.
        Assert.True(ColumnInk(bitmap, 43) > ColumnInk(bitmap, 20), "The arrow head is no wider than the line.");
        Assert.Equal(255, bitmap.GetPixel(49, 15).A);
    }

    /// <summary>
    /// A filled arrow from AdjustableArrowCap: wider than the line near its
    /// back, coming to a point at the end, and with the line stopping at its
    /// back so it does not show through.
    /// </summary>
    [Fact]
    public void An_adjustable_arrow_cap_draws_a_filled_head_at_the_end()
    {
        using var bitmap = new Bitmap(60, 30);
        using var head = new AdjustableArrowCap(3, 3);
        using var pen = new Pen(Ink, 4) { CustomEndCap = head };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 5, 15, 50, 15));

        Assert.Equal(LineCap.Custom, pen.EndCap);
        Assert.True(ColumnInk(bitmap, 40) > ColumnInk(bitmap, 20), "The arrow head is no wider than the line.");
        Assert.True(ColumnInk(bitmap, 49) < ColumnInk(bitmap, 40), "The head does not narrow to a point.");
    }

    [Fact]
    public void A_cap_from_a_fill_path_is_placed_on_the_end_and_turned_with_the_line()
    {
        using var square = new GraphicsPath();
        square.AddRectangle(new RectangleF(-1, 0, 2, 2));
        using var cap = new CustomLineCap(square, null);
        using var bitmap = new Bitmap(30, 40);
        using var pen = new Pen(Ink, 2) { CustomEndCap = cap };

        // A vertical line going down: the cap, two pen widths long, continues downwards.
        Draw(bitmap, graphics => graphics.DrawLine(pen, 15, 5, 15, 25));

        Assert.Equal(255, bitmap.GetPixel(15, 27).A);
        Assert.Equal(0, bitmap.GetPixel(15, 31).A);
    }

    [Fact]
    public void A_translucent_pen_is_not_darker_where_its_caps_overlap_the_line()
    {
        using var bitmap = new Bitmap(40, 20);
        using var pen = new Pen(Color.FromArgb(128, 20, 40, 160), 6) { StartCap = LineCap.Round, EndCap = LineCap.Triangle };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 10, 10, 30, 10));

        var alphas = new HashSet<int>();
        for (var x = 0; x < 40; x++)
        {
            for (var y = 0; y < 20; y++)
            {
                var a = bitmap.GetPixel(x, y).A;
                if (a != 0) alphas.Add(a);
            }
        }

        Assert.Equal([128], alphas);
    }

    /// <summary>
    /// A line from a point to itself still shows its caps, whether Skia draws
    /// them (two equal round caps) or they are added as shapes (round and
    /// square).
    /// </summary>
    [Theory]
    [InlineData(LineCap.Round, LineCap.Round)]
    [InlineData(LineCap.Round, LineCap.Square)]
    public void A_line_of_no_length_shows_its_caps(LineCap start, LineCap end)
    {
        using var bitmap = new Bitmap(20, 20);
        using var pen = new Pen(Ink, 6) { StartCap = start, EndCap = end };

        Draw(bitmap, graphics => graphics.DrawLine(pen, 10, 10, 10, 10));

        Assert.Equal(255, bitmap.GetPixel(10, 10).A);
        Assert.Equal(0, bitmap.GetPixel(10, 16).A);
    }

    [Fact]
    public void A_custom_cap_takes_a_fill_path_or_a_stroke_path()
    {
        using var path = new GraphicsPath();
        path.AddLine(0, 0, 1, 1);

        Assert.Throws<NotImplementedException>(() => new CustomLineCap(path, path));
        Assert.Throws<ArgumentException>(() => new CustomLineCap(null, null));
    }

    [Fact]
    public void A_pen_without_a_custom_cap_has_none_to_give()
    {
        using var pen = new Pen(Ink);

        Assert.Throws<ArgumentException>(() => pen.CustomStartCap);
    }

    /// <summary>
    /// DrawArc measures its angles on the ellipse, as AddArc does: drawing an
    /// arc and drawing the same arc from a path give the same pixels.
    /// </summary>
    [Fact]
    public void DrawArc_and_an_arc_in_a_path_coincide()
    {
        using var direct = new Bitmap(60, 40);
        using var throughPath = new Bitmap(60, 40);
        using var pen = new Pen(Ink, 2);
        using var path = new GraphicsPath();
        path.AddArc(5, 5, 50, 25, 20, 120);

        Draw(direct, graphics => graphics.DrawArc(pen, 5, 5, 50, 25, 20, 120));
        Draw(throughPath, graphics => graphics.DrawPath(pen, path));

        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 60; x++)
            {
                Assert.Equal(throughPath.GetPixel(x, y).ToArgb(), direct.GetPixel(x, y).ToArgb());
            }
        }
    }

    private static void Draw(Bitmap bitmap, Action<Graphics> draw)
    {
        using var graphics = Graphics.FromImage(bitmap);
        draw(graphics);
    }

    private static int ColumnInk(Bitmap bitmap, int x) => Enumerable.Range(0, bitmap.Height).Count(y => bitmap.GetPixel(x, y).A != 0);
}
