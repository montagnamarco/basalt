using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The points and types GDI+ writes into a path. Applications read them back,
/// count them and walk them, so the numbers are the contract, not only the
/// shape they draw.
/// </summary>
public class GraphicsPathTests
{
    private const byte Start = 0;
    private const byte Line = 1;
    private const byte Bezier = 3;
    private const byte Marker = 0x20;
    private const byte Close = 0x80;

    [Fact]
    public void A_rectangle_is_a_closed_figure_of_four_points()
    {
        using var path = new GraphicsPath();

        path.AddRectangle(new RectangleF(1, 2, 10, 5));

        Assert.Equal([new(1, 2), new(11, 2), new(11, 7), new(1, 7)], path.PathPoints);
        Assert.Equal([Start, Line, Line, Line | Close], path.PathTypes);
    }

    [Fact]
    public void A_rectangle_without_area_adds_nothing()
    {
        using var path = new GraphicsPath();

        path.AddRectangle(new RectangleF(1, 2, 0, 5));

        Assert.Equal(0, path.PointCount);
    }

    /// <summary>
    /// Lines join the open figure: the second line starts where the first
    /// ended, so that point is not repeated; a line elsewhere is joined to it.
    /// </summary>
    [Fact]
    public void Lines_continue_the_open_figure_without_repeating_the_shared_point()
    {
        using var path = new GraphicsPath();

        path.AddLine(0, 0, 10, 0);
        path.AddLine(10, 0, 10, 10);
        path.AddLine(20, 20, 30, 30);

        Assert.Equal([new(0, 0), new(10, 0), new(10, 10), new(20, 20), new(30, 30)], path.PathPoints);
        Assert.Equal([Start, Line, Line, Line, Line], path.PathTypes);
    }

    [Fact]
    public void StartFigure_makes_the_next_line_a_figure_of_its_own()
    {
        using var path = new GraphicsPath();

        path.AddLine(0, 0, 10, 0);
        path.StartFigure();
        path.AddLine(0, 5, 10, 5);

        Assert.Equal([Start, Line, Start, Line], path.PathTypes);
    }

    [Fact]
    public void An_ellipse_is_four_Bezier_segments_in_thirteen_points()
    {
        using var path = new GraphicsPath();

        path.AddEllipse(0, 0, 100, 50);

        var types = path.PathTypes;
        Assert.Equal(13, types.Length);
        Assert.Equal(Start, types[0]);
        Assert.All(types[1..12], type => Assert.Equal(Bezier, type));
        Assert.Equal(Bezier | Close, types[12]);

        // It starts and ends at the rightmost point and passes through the other three extremes.
        var points = path.PathPoints;
        Assert.Equal(new PointF(100, 25), points[0]);
        Assert.Equal(new PointF(100, 25), points[12]);
        Assert.Contains(new PointF(50, 50), points);
        Assert.Contains(new PointF(0, 25), points);
        Assert.Contains(new PointF(50, 0), points);
        Assert.Equal(new RectangleF(0, 0, 100, 50), path.GetBounds());
    }

    [Fact]
    public void An_arc_is_split_into_segments_of_at_most_ninety_degrees()
    {
        using var quarter = new GraphicsPath();
        using var half = new GraphicsPath();
        using var none = new GraphicsPath();

        quarter.AddArc(0, 0, 100, 100, 0, 90);
        half.AddArc(0, 0, 100, 100, 0, 180);
        none.AddArc(0, 0, 100, 100, 30, 0);

        Assert.Equal([Start, Bezier, Bezier, Bezier], quarter.PathTypes);
        Assert.Equal(7, half.PointCount);
        Assert.Equal(0, none.PointCount);

        // Angles run clockwise on the screen: from the right, down to the bottom.
        AssertNear(new PointF(100, 50), quarter.PathPoints[0]);
        AssertNear(new PointF(50, 100), quarter.PathPoints[3]);
    }

    /// <summary>
    /// The angle of an arc is measured on the ellipse: 45 degrees on a wide
    /// ellipse is where a ray at 45 degrees from the centre meets it, not
    /// where 45 degrees of the circle lands once squashed.
    /// </summary>
    [Fact]
    public void Arc_angles_are_measured_on_the_ellipse_itself()
    {
        using var path = new GraphicsPath();

        path.AddArc(0, 0, 200, 100, 0, 45);

        // On x²/100² + y²/50² = 1 with x = y: x = 1 / √(1/100² + 1/50²).
        var t = 1 / MathF.Sqrt(1 / 10000f + 1 / 2500f);
        AssertNear(new PointF(100 + t, 50 + t), path.GetLastPoint());
    }

    /// <summary>
    /// Four quarter arcs of the corner ellipse joined by the straight sides,
    /// in one closed figure; drawn, it leaves the corners empty.
    /// </summary>
    [Fact]
    public void A_rounded_rectangle_is_four_corner_arcs_in_one_closed_figure()
    {
        using var path = new GraphicsPath();

        path.AddRoundedRectangle(new RectangleF(0, 0, 40, 20), new SizeF(10, 10));

        var types = path.PathTypes;
        Assert.Equal(Start, types[0]);
        Assert.Equal(1, types.Count(type => (type & 7) == Start));
        Assert.Equal(12, types.Count(type => (type & 7) == Bezier));
        Assert.NotEqual(0, types[^1] & Close);
        Assert.Equal(new RectangleF(0, 0, 40, 20), path.GetBounds());
        Assert.False(path.IsVisible(0.5f, 0.5f));
        Assert.True(path.IsVisible(20, 0.5f));

        using var bitmap = new Bitmap(40, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Color.Black))
        {
            graphics.FillRoundedRectangle(brush, new RectangleF(0, 0, 40, 20), new SizeF(10, 10));
        }

        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(255, bitmap.GetPixel(20, 0).A);
        Assert.Equal(255, bitmap.GetPixel(0, 10).A);
    }

    [Fact]
    public void A_pie_goes_from_the_centre_to_the_arc_and_closes()
    {
        using var path = new GraphicsPath();

        path.AddPie(0, 0, 100, 100, 0, 90);

        Assert.Equal([Start, Line, Bezier, Bezier, Bezier | Close], path.PathTypes);
        Assert.Equal(new PointF(50, 50), path.PathPoints[0]);
    }

    [Fact]
    public void A_polygon_needs_three_points_and_closes()
    {
        using var path = new GraphicsPath();

        path.AddPolygon(new[] { new PointF(0, 0), new PointF(10, 0), new PointF(5, 8) });

        Assert.Equal([Start, Line, Line | Close], path.PathTypes);
        Assert.Throws<ArgumentException>(() => path.AddPolygon(new[] { new PointF(0, 0), new PointF(1, 1) }));
    }

    [Fact]
    public void A_curve_through_two_points_is_a_straight_Bezier()
    {
        using var path = new GraphicsPath();

        path.AddCurve(new[] { new PointF(0, 0), new PointF(9, 0) });

        Assert.Equal([Start, Bezier, Bezier, Bezier], path.PathTypes);
        Assert.All(path.PathPoints, point => Assert.Equal(0, point.Y));
    }

    [Fact]
    public void A_closed_curve_has_a_segment_per_point_and_closes()
    {
        using var path = new GraphicsPath();

        path.AddClosedCurve(new[] { new PointF(0, 0), new PointF(10, 0), new PointF(5, 8) });

        Assert.Equal(1 + 3 * 3, path.PointCount);
        Assert.Equal(Bezier | Close, path.PathTypes[^1]);
        Assert.Equal(new PointF(0, 0), path.PathPoints[^1]);
    }

    [Fact]
    public void Closing_figures_and_markers_ride_on_the_last_point()
    {
        using var path = new GraphicsPath();

        path.AddLine(0, 0, 10, 0);
        path.SetMarkers();
        path.StartFigure();
        path.AddLine(0, 5, 10, 5);
        path.CloseAllFigures();

        Assert.Equal([Start, Line | Marker | Close, Start, Line | Close], path.PathTypes);

        path.ClearMarkers();

        Assert.Equal([Start, Line | Close, Start, Line | Close], path.PathTypes);
    }

    [Fact]
    public void AddPath_with_connect_joins_its_first_figure_to_the_open_one()
    {
        using var path = new GraphicsPath();
        using var other = new GraphicsPath();
        other.AddLine(20, 0, 30, 0);

        path.AddLine(0, 0, 10, 0);
        path.AddPath(other, connect: true);

        Assert.Equal([Start, Line, Line, Line], path.PathTypes);

        path.AddPath(other, connect: false);

        Assert.Equal(Start, path.PathTypes[4]);
    }

    [Fact]
    public void Reverse_runs_each_figure_backwards_and_keeps_its_types_on_their_segments()
    {
        using var path = new GraphicsPath();
        path.AddLine(0, 0, 10, 0);
        path.AddBezier(10, 0, 12, 2, 14, 4, 16, 8);

        path.Reverse();

        Assert.Equal([new(16, 8), new(14, 4), new(12, 2), new(10, 0), new(0, 0)], path.PathPoints);
        Assert.Equal([Start, Bezier, Bezier, Bezier, Line], path.PathTypes);
    }

    [Fact]
    public void Reverse_keeps_a_closed_figure_closed()
    {
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(0, 0, 4, 3));

        path.Reverse();

        Assert.Equal([new(0, 3), new(4, 3), new(4, 0), new(0, 0)], path.PathPoints);
        Assert.Equal([Start, Line, Line, Line | Close], path.PathTypes);
    }

    [Fact]
    public void Flatten_replaces_curves_with_lines_close_to_them()
    {
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, 100, 100);

        path.Flatten();

        var types = path.PathTypes;
        Assert.True(types.Length > 13);
        Assert.Equal(Start, types[0]);
        Assert.All(types[1..^1], type => Assert.Equal(Line, type));
        Assert.Equal(Line | Close, types[^1]);

        // Every point is on the circle, within the default flatness of a quarter pixel.
        Assert.All(path.PathPoints, point =>
            Assert.InRange(MathF.Sqrt((point.X - 50) * (point.X - 50) + (point.Y - 50) * (point.Y - 50)), 49.75f, 50.25f));
    }

    [Fact]
    public void Widen_turns_a_line_into_the_area_the_pen_covers()
    {
        using var path = new GraphicsPath();
        using var pen = new Pen(Color.Black, 4);
        path.AddLine(0, 5, 20, 5);

        path.Widen(pen);

        Assert.Equal(new RectangleF(0, 3, 20, 4), path.GetBounds());
        Assert.True(path.IsVisible(10, 4));
        Assert.False(path.IsVisible(10, 8));
    }

    [Fact]
    public void The_fill_mode_decides_whether_a_nested_figure_is_a_hole()
    {
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(0, 0, 20, 20));
        path.AddRectangle(new RectangleF(5, 5, 10, 10));

        Assert.False(path.IsVisible(10, 10));
        Assert.True(path.IsVisible(2, 2));

        path.FillMode = FillMode.Winding;

        Assert.True(path.IsVisible(10, 10));
    }

    [Fact]
    public void The_outline_is_where_the_pen_would_paint()
    {
        using var path = new GraphicsPath();
        using var pen = new Pen(Color.Black, 2);
        path.AddRectangle(new RectangleF(0, 0, 20, 20));

        Assert.True(path.IsOutlineVisible(0.5f, 10, pen));
        Assert.False(path.IsOutlineVisible(10, 10, pen));
    }

    [Fact]
    public void Transform_moves_every_point()
    {
        using var path = new GraphicsPath();
        using var matrix = new Matrix();
        path.AddLine(0, 0, 10, 0);
        matrix.Translate(5, 7);

        path.Transform(matrix);

        Assert.Equal([new(5, 7), new(15, 7)], path.PathPoints);
    }

    [Fact]
    public void Warp_maps_the_source_rectangle_onto_the_destination_corners()
    {
        using var parallelogram = new GraphicsPath();
        using var perspective = new GraphicsPath();
        var source = new RectangleF(0, 0, 10, 10);
        parallelogram.AddRectangle(source);
        perspective.AddRectangle(source);

        parallelogram.Warp(new[] { new PointF(0, 0), new PointF(20, 0), new PointF(5, 10) }, source);
        perspective.Warp(new[] { new PointF(0, 0), new PointF(20, 0), new PointF(0, 10), new PointF(10, 10) }, source);

        AssertNear(new PointF(25, 10), parallelogram.PathPoints[2]);
        AssertNear(new PointF(10, 10), perspective.PathPoints[2]);
        AssertNear(new PointF(0, 10), perspective.PathPoints[3]);
    }

    [Fact]
    public void Points_and_types_must_come_in_pairs()
    {
        Assert.Throws<ArgumentException>(() => new GraphicsPath(new[] { new PointF(0, 0) }, new byte[] { Start, Line }));

        using var empty = new GraphicsPath();

        Assert.Throws<ArgumentException>(() => empty.GetLastPoint());
    }

    [Fact]
    public void The_iterator_walks_figures_runs_and_markers()
    {
        using var path = new GraphicsPath();
        path.AddLine(0, 0, 10, 0);
        path.AddBezier(10, 0, 12, 2, 14, 4, 16, 8);
        path.SetMarkers();
        path.AddRectangle(new RectangleF(0, 20, 5, 5));

        using var iterator = new GraphicsPathIterator(path);

        Assert.Equal(9, iterator.Count);
        Assert.Equal(2, iterator.SubpathCount);
        Assert.True(iterator.HasCurve());

        Assert.Equal(5, iterator.NextSubpath(out var first, out var last, out var closed));
        Assert.Equal((0, 4, false), (first, last, closed));

        Assert.Equal(2, iterator.NextPathType(out var type, out first, out last));
        Assert.Equal((Line, 0, 1), (type, first, last));
        Assert.Equal(4, iterator.NextPathType(out type, out first, out last));
        Assert.Equal((Bezier, 1, 4), (type, first, last));
        Assert.Equal(0, iterator.NextPathType(out _, out _, out _));

        Assert.Equal(4, iterator.NextSubpath(out first, out last, out closed));
        Assert.Equal((5, 8, true), (first, last, closed));
        Assert.Equal(0, iterator.NextSubpath(out _, out _, out _));

        Assert.Equal(5, iterator.NextMarker(out first, out last));
        Assert.Equal((0, 4), (first, last));
        Assert.Equal(4, iterator.NextMarker(out first, out last));
        Assert.Equal((5, 8), (first, last));

        var points = new PointF[3];
        var types = new byte[3];
        Assert.Equal(3, iterator.CopyData(ref points, ref types, 5, 7));
        Assert.Equal(new PointF(0, 20), points[0]);
        Assert.Equal(Start, types[0]);
    }

    private static void AssertNear(PointF expected, PointF actual)
    {
        Assert.InRange(actual.X, expected.X - 0.01f, expected.X + 0.01f);
        Assert.InRange(actual.Y, expected.Y - 0.01f, expected.Y + 0.01f);
    }
}
