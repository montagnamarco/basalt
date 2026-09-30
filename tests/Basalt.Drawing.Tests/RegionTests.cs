using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Regions as sets of points: what each combination keeps, and how they reach
/// the pixels when they fill or clip.
/// </summary>
public class RegionTests
{
    private static readonly Color Ink = Color.FromArgb(255, 200, 30, 40);

    [Fact]
    public void A_new_region_is_infinite_and_reports_GDI_plus_bounds()
    {
        using var region = new Region();
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.True(region.IsInfinite(graphics));
        Assert.False(region.IsEmpty(graphics));
        Assert.Equal(new RectangleF(-4194304, -4194304, 8388608, 8388608), region.GetBounds(graphics));
    }

    [Theory]
    [InlineData("Intersect", 25)]
    [InlineData("Union", 175)]
    [InlineData("Xor", 150)]
    [InlineData("Exclude", 75)]
    [InlineData("Complement", 75)]
    public void Each_combination_keeps_the_area_it_names(string operation, int expectedArea)
    {
        using var region = new Region(new Rectangle(0, 0, 10, 10));
        var other = new Rectangle(5, 5, 10, 10);

        switch (operation)
        {
            case "Intersect": region.Intersect(other); break;
            case "Union": region.Union(other); break;
            case "Xor": region.Xor(other); break;
            case "Exclude": region.Exclude(other); break;
            case "Complement": region.Complement(other); break;
        }

        Assert.Equal(expectedArea, Area(region));
    }

    [Fact]
    public void Exclude_keeps_what_is_left_of_the_region_and_Complement_what_is_left_of_the_other()
    {
        using var excluded = new Region(new Rectangle(0, 0, 10, 10));
        using var complemented = new Region(new Rectangle(0, 0, 10, 10));

        excluded.Exclude(new Rectangle(5, 5, 10, 10));
        complemented.Complement(new Rectangle(5, 5, 10, 10));

        Assert.True(excluded.IsVisible(2, 2));
        Assert.False(excluded.IsVisible(12, 12));
        Assert.False(complemented.IsVisible(2, 2));
        Assert.True(complemented.IsVisible(12, 12));
    }

    [Fact]
    public void Combining_with_the_infinite_region_follows_the_rules_of_sets()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var infinite = new Region();

        using var intersected = new Region();
        intersected.Intersect(new Rectangle(1, 2, 3, 4));
        Assert.Equal(new RectangleF(1, 2, 3, 4), intersected.GetBounds(graphics));

        using var united = new Region(new Rectangle(1, 2, 3, 4));
        united.Union(infinite);
        Assert.True(united.IsInfinite(graphics));

        using var excluded = new Region(new Rectangle(1, 2, 3, 4));
        excluded.Exclude(infinite);
        Assert.True(excluded.IsEmpty(graphics));

        using var holed = new Region();
        holed.Exclude(new Rectangle(0, 0, 10, 10));
        Assert.False(holed.IsVisible(5, 5));
        Assert.True(holed.IsVisible(50, 50));
    }

    [Fact]
    public void A_region_from_a_path_uses_the_path_s_fill_mode()
    {
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(0, 0, 20, 20));
        path.AddRectangle(new RectangleF(5, 5, 10, 10));

        using var region = new Region(path);

        Assert.False(region.IsVisible(10, 10));
        Assert.True(region.IsVisible(2, 2));
    }

    [Fact]
    public void Translate_and_transform_move_the_region()
    {
        using var region = new Region(new Rectangle(0, 0, 4, 4));
        using var matrix = new Matrix();
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        matrix.Scale(2, 3);

        region.Translate(1, 1);
        region.Transform(matrix);

        Assert.Equal(new RectangleF(2, 3, 8, 12), region.GetBounds(graphics));
    }

    [Fact]
    public void Equals_compares_the_areas_not_how_they_were_built()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var whole = new Region(new Rectangle(0, 0, 10, 10));
        using var halves = new Region(new Rectangle(0, 0, 5, 10));
        halves.Union(new Rectangle(5, 0, 5, 10));

        Assert.True(whole.Equals(halves, graphics));

        halves.Exclude(new Rectangle(9, 9, 1, 1));

        Assert.False(whole.Equals(halves, graphics));
    }

    [Fact]
    public void Scans_are_whole_pixel_bands_covering_the_region()
    {
        using var region = new Region(new Rectangle(0, 0, 10, 10));
        using var identity = new Matrix();
        region.Exclude(new Rectangle(3, 3, 4, 4));

        var scans = region.GetRegionScans(identity);

        Assert.Equal(100 - 16, scans.Sum(scan => (int)(scan.Width * scan.Height)));
        Assert.Equal(4, scans.Length);
    }

    [Fact]
    public void Region_data_reads_back_as_the_same_region()
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        using var region = new Region(new Rectangle(0, 0, 10, 10));
        region.Exclude(new Rectangle(3, 3, 4, 4));

        var data = region.GetRegionData()!;
        using var read = new Region(data);

        Assert.True(read.Equals(region, graphics));

        using var infinite = new Region(new Region().GetRegionData()!);
        Assert.True(infinite.IsInfinite(graphics));
    }

    [Fact]
    public void FillRegion_paints_exactly_the_region()
    {
        using var bitmap = new Bitmap(12, 12);
        using var region = new Region(new Rectangle(0, 0, 10, 10));
        region.Exclude(new Rectangle(3, 3, 4, 4));

        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillRegion(brush, region);
        }

        Assert.Equal(100 - 16, CountInk(bitmap));
        Assert.Equal(0, bitmap.GetPixel(4, 4).A);
    }

    [Fact]
    public void A_path_or_a_region_can_clip_and_the_clip_reads_back_as_a_region()
    {
        using var bitmap = new Bitmap(20, 20);
        using var path = new GraphicsPath();
        path.AddEllipse(0, 0, 20, 20);

        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(path);
            graphics.FillRectangle(brush, 0, 0, 20, 20);

            using var clip = graphics.Clip;
            Assert.Equal(new RectangleF(0, 0, 20, 20), clip.GetBounds(graphics));
            Assert.False(clip.IsVisible(1, 1));
        }

        // The corners are outside the circle.
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.Equal(255, bitmap.GetPixel(10, 10).A);
    }

    [Fact]
    public void The_clip_region_is_given_in_the_caller_s_coordinates()
    {
        using var bitmap = new Bitmap(20, 20);
        using var graphics = Graphics.FromImage(bitmap);
        using var region = new Region(new Rectangle(0, 0, 4, 4));

        graphics.TranslateTransform(10, 10);
        graphics.Clip = region;

        using var clip = graphics.Clip;
        Assert.Equal(new RectangleF(0, 0, 4, 4), clip.GetBounds(graphics));

        graphics.ResetTransform();
        Assert.True(graphics.IsVisible(11, 11));
        Assert.False(graphics.IsVisible(1, 1));
    }

    [Fact]
    public void DrawPath_and_FillPath_follow_the_pixel_rule_of_rectangles()
    {
        using var drawn = new Bitmap(16, 16);
        using var filled = new Bitmap(16, 16);
        using var path = new GraphicsPath();
        path.AddRectangle(new RectangleF(2.3f, 3.3f, 10, 8));

        using (var graphics = Graphics.FromImage(filled))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.FillPath(brush, path);
        }

        using (var graphics = Graphics.FromImage(drawn))
        using (var pen = new Pen(Ink))
        {
            graphics.DrawPath(pen, path);
        }

        // Pixels whose centres, on whole coordinates, lie inside.
        Assert.Equal(10 * 8, CountInk(filled));
        Assert.Equal(0, filled.GetPixel(2, 3).A);
        Assert.Equal(255, filled.GetPixel(3, 4).A);
        Assert.True(CountInk(drawn) > 0);
    }

    private static int Area(Region region)
    {
        using var identity = new Matrix();

        return region.GetRegionScans(identity).Sum(scan => (int)(scan.Width * scan.Height));
    }

    private static int CountInk(Bitmap bitmap)
    {
        var count = 0;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A != 0) count++;
            }
        }

        return count;
    }
}
