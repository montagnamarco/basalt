using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>
/// The clip is a region fixed to the surface, not a rectangle that follows the
/// caller's coordinates. A control relies on both halves of that: it excludes
/// its children before painting its background, and it moves the origin after
/// the clip was set.
/// </summary>
public class GraphicsClipTests
{
    private static readonly Color Ink = Color.FromArgb(255, 200, 30, 40);

    [Fact]
    public void ExcludeClip_cuts_a_hole_that_a_fill_leaves_untouched()
    {
        using var bitmap = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.ExcludeClip(new Rectangle(4, 4, 4, 4));
            graphics.FillRectangle(brush, 0, 0, 12, 12);
        }

        Assert.Equal(12 * 12 - 4 * 4, CountInk(bitmap));
        Assert.Equal(0, bitmap.GetPixel(4, 4).A);
        Assert.Equal(0, bitmap.GetPixel(7, 7).A);
        Assert.Equal(255, bitmap.GetPixel(3, 4).A);
        Assert.Equal(255, bitmap.GetPixel(8, 7).A);
    }

    [Fact]
    public void A_clip_stays_where_it_was_set_when_the_transform_changes_afterwards()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(2, 2, 4, 4));
            graphics.TranslateTransform(8, 8);
            graphics.FillRectangle(brush, -8, -8, 16, 16);

            // The same clip, read back in the moved coordinates.
            Assert.Equal(new RectangleF(-6, -6, 4, 4), graphics.ClipBounds);
        }

        Assert.Equal((2, 2, 5, 5), InkBounds(bitmap));
        Assert.Equal(16, CountInk(bitmap));
    }

    [Fact]
    public void A_clip_set_under_a_transform_lands_where_the_transform_puts_it()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.TranslateTransform(5, 6);
            graphics.SetClip(new Rectangle(0, 0, 3, 2));
            graphics.ResetTransform();
            graphics.FillRectangle(brush, 0, 0, 16, 16);
        }

        Assert.Equal((5, 6, 7, 7), InkBounds(bitmap));
    }

    [Fact]
    public void Xor_keeps_what_only_one_of_the_two_rectangles_covers()
    {
        using var bitmap = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(0, 0, 6, 6));
            graphics.SetClip(new Rectangle(3, 3, 6, 6), CombineMode.Xor);
            graphics.FillRectangle(brush, 0, 0, 12, 12);
        }

        Assert.Equal(36 + 36 - 2 * 9, CountInk(bitmap));
        Assert.Equal(0, bitmap.GetPixel(4, 4).A);
        Assert.Equal(255, bitmap.GetPixel(1, 1).A);
        Assert.Equal(255, bitmap.GetPixel(7, 7).A);
    }

    [Fact]
    public void Complement_keeps_the_part_of_the_new_rectangle_the_clip_did_not_cover()
    {
        using var bitmap = new Bitmap(12, 12);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(0, 0, 6, 6));
            graphics.SetClip(new Rectangle(3, 3, 6, 6), CombineMode.Complement);
            graphics.FillRectangle(brush, 0, 0, 12, 12);
        }

        Assert.Equal(36 - 9, CountInk(bitmap));
        Assert.Equal(0, bitmap.GetPixel(1, 1).A);
        Assert.Equal(0, bitmap.GetPixel(4, 4).A);
        Assert.Equal(255, bitmap.GetPixel(7, 7).A);
    }

    [Fact]
    public void Union_widens_the_clip_and_a_union_with_no_clip_stays_unclipped()
    {
        using var bitmap = new Bitmap(12, 12);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SetClip(new Rectangle(0, 0, 2, 2), CombineMode.Union);
        Assert.True(graphics.ClipBounds.Width > 1000);

        graphics.SetClip(new Rectangle(0, 0, 2, 2));
        graphics.SetClip(new Rectangle(6, 6, 2, 2), CombineMode.Union);

        Assert.Equal(new RectangleF(0, 0, 8, 8), graphics.ClipBounds);
        Assert.False(graphics.IsVisible(4, 4));
        Assert.True(graphics.IsVisible(7, 7));
    }

    [Fact]
    public void Restore_brings_back_the_clip_that_was_saved()
    {
        using var bitmap = new Bitmap(12, 12);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SetClip(new Rectangle(1, 1, 5, 5));
        var saved = graphics.Save();
        graphics.ExcludeClip(new Rectangle(1, 1, 5, 5));

        Assert.True(graphics.IsClipEmpty);

        graphics.Restore(saved);

        Assert.False(graphics.IsClipEmpty);
        Assert.Equal(new RectangleF(1, 1, 5, 5), graphics.ClipBounds);
    }

    [Fact]
    public void What_can_be_seen_is_the_clip_cut_down_to_the_surface()
    {
        using var bitmap = new Bitmap(10, 8);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.Equal(new RectangleF(0, 0, 10, 8), graphics.VisibleClipBounds);

        graphics.SetClip(new Rectangle(6, 5, 20, 20));

        Assert.Equal(new RectangleF(6, 5, 4, 3), graphics.VisibleClipBounds);
        Assert.True(graphics.IsVisible(new Rectangle(9, 7, 5, 5)));
        Assert.False(graphics.IsVisible(new Rectangle(10, 8, 5, 5)));
        Assert.False(graphics.IsVisible(new Rectangle(0, 0, 6, 5)));
    }

    /// <summary>
    /// Every primitive has to go through the clip and the transform. An
    /// outline of a polygon once did not, and drew over what was excluded.
    /// </summary>
    [Fact]
    public void A_polygon_outline_obeys_the_clip_and_the_transform()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Ink))
        {
            graphics.SetClip(new Rectangle(0, 0, 8, 16));
            graphics.TranslateTransform(2, 3);
            graphics.DrawPolygon(pen, new[] { new PointF(1, 1), new PointF(11, 1), new PointF(11, 6), new PointF(1, 6) });
        }

        // Moved by the transform, and cut at column 8 by the clip.
        Assert.Equal((3, 4, 7, 9), InkBounds(bitmap));
    }

    [Fact]
    public void Clear_leaves_what_the_clip_excludes()
    {
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.ExcludeClip(new Rectangle(2, 2, 4, 4));
            graphics.Clear(Ink);
        }

        Assert.Equal(64 - 16, CountInk(bitmap));
        Assert.Equal(0, bitmap.GetPixel(3, 3).A);
    }

    [Fact]
    public void Disposing_twice_and_with_states_never_restored_is_harmless()
    {
        using var bitmap = new Bitmap(8, 8);
        var graphics = Graphics.FromImage(bitmap);

        graphics.SetClip(new Rectangle(1, 1, 2, 2));
        graphics.Save();
        graphics.ExcludeClip(new Rectangle(1, 1, 1, 1));
        graphics.Save();

        graphics.Dispose();
        graphics.Dispose();

        // The image is free again for whatever needed it to itself.
        bitmap.RotateFlip(RotateFlipType.Rotate90FlipNone);
    }

    [Fact]
    public void A_matrix_that_flattens_the_plane_has_no_inverse()
    {
        using var flat = new Matrix(1, 2, 2, 4, 0, 0);

        Assert.False(flat.IsInvertible);
        Assert.Throws<ArgumentException>(() => flat.Invert());
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
}
