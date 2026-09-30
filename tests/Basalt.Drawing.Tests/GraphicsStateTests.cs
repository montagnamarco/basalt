using System.Drawing;
using System.Drawing.Drawing2D;

namespace Basalt.Drawing.Tests;

/// <summary>The world transform, the Save/Restore and BeginContainer/EndContainer stack, the quality properties, and the rectangular clip.</summary>
public class GraphicsStateTests
{
    private static readonly Color Ink = Color.FromArgb(255, 200, 30, 40);

    [Fact]
    public void A_fresh_graphics_over_a_bitmap_has_the_identity_transform()
    {
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);

        Assert.True(graphics.Transform.IsIdentity);
    }

    [Fact]
    public void Transform_get_returns_an_independent_copy()
    {
        using var bitmap = new Bitmap(4, 4);
        using var graphics = Graphics.FromImage(bitmap);

        var first = graphics.Transform;
        first.Translate(9, 9);

        Assert.True(graphics.Transform.IsIdentity);
    }

    [Fact]
    public void TranslateTransform_shifts_where_a_fill_lands()
    {
        // Wide enough that the translated rectangle (right edge at x=16) is
        // not cut off by the canvas itself: a 16-wide bitmap would clip it
        // and look like a bug in the transform instead of a too-small canvas.
        using var bitmap = new Bitmap(20, 20);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.TranslateTransform(5, 4);
            graphics.FillRectangle(brush, 2, 3, 10, 8);
        }

        Assert.Equal((7, 7, 16, 14), InkBounds(bitmap));
        Assert.Equal(10 * 8, CountInk(bitmap));
    }

    [Fact]
    public void ResetTransform_returns_to_the_untransformed_coordinate_system()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.TranslateTransform(5, 4);
            graphics.ResetTransform();
            graphics.FillRectangle(brush, 2, 3, 10, 8);
        }

        Assert.Equal((2, 3, 11, 10), InkBounds(bitmap));
    }

    [Fact]
    public void Save_and_Restore_round_trip_the_transform_the_clip_and_the_smoothing_mode()
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(Ink);

        var state = graphics.Save();
        graphics.TranslateTransform(5, 5);
        graphics.SetClip(new Rectangle(0, 0, 1, 1));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        graphics.Restore(state);

        Assert.True(graphics.Transform.IsIdentity);
        Assert.True(graphics.IsClipEmpty == false && graphics.ClipBounds.Width > 1000);
        Assert.Equal(SmoothingMode.None, graphics.SmoothingMode);

        graphics.FillRectangle(brush, 2, 3, 10, 8);
        Assert.Equal((2, 3, 11, 10), InkBounds(bitmap));
    }

    /// <summary>
    /// The rule that makes Save/Restore a stack rather than a set of
    /// independent bookmarks: restoring an older state throws away every
    /// state saved after it, and restoring one of those discarded states
    /// afterwards does nothing.
    /// </summary>
    [Fact]
    public void Restore_discards_every_state_saved_after_the_one_restored()
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);

        var outer = graphics.Save();
        graphics.TranslateTransform(5, 5);

        var inner = graphics.Save();
        graphics.TranslateTransform(100, 100);

        graphics.Restore(outer);
        Assert.True(graphics.Transform.IsIdentity);

        // "inner" was discarded when "outer" was restored: restoring it now must do nothing.
        graphics.Restore(inner);
        Assert.True(graphics.Transform.IsIdentity);
    }

    [Fact]
    public void BeginContainer_and_EndContainer_behave_like_Save_and_Restore_for_the_transform()
    {
        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);

        var container = graphics.BeginContainer();
        graphics.TranslateTransform(5, 5);
        graphics.EndContainer(container);

        Assert.True(graphics.Transform.IsIdentity);
    }

    /// <summary>BeginContainer(dst, src, unit) remaps coordinates given in srcrect's numbers so they land in dstrect.</summary>
    [Fact]
    public void BeginContainer_with_rectangles_scales_drawing_into_the_destination_rectangle()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            // src (0,0,5,5) maps to dst (0,0,10,10): everything drawn at src scale is doubled.
            var container = graphics.BeginContainer(new Rectangle(0, 0, 10, 10), new Rectangle(0, 0, 5, 5), GraphicsUnit.Pixel);
            graphics.FillRectangle(brush, 1, 1, 2, 2);
            graphics.EndContainer(container);
        }

        // (1,1,2,2) scaled by 2 is (2,2,4,4): columns/rows 2..5 inclusive, a 4x4 block.
        Assert.Equal((2, 2, 5, 5), InkBounds(bitmap));
        Assert.Equal(4 * 4, CountInk(bitmap));
    }

    [Fact]
    public void CompositingMode_SourceCopy_replaces_the_destination_pixel_instead_of_blending()
    {
        var translucent = Color.FromArgb(128, 0, 0, 255);

        using var opaque = new Bitmap(4, 4);
        using (var graphics = Graphics.FromImage(opaque))
        using (var brush = new SolidBrush(translucent))
        {
            graphics.Clear(Color.FromArgb(255, 255, 0, 0));
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.FillRectangle(brush, 0, 0, 4, 4);
        }

        using var blended = new Bitmap(4, 4);
        using (var graphics = Graphics.FromImage(blended))
        using (var brush = new SolidBrush(translucent))
        {
            graphics.Clear(Color.FromArgb(255, 255, 0, 0));
            graphics.FillRectangle(brush, 0, 0, 4, 4);
        }

        // SourceCopy leaves the brush's own (still translucent) colour in the
        // pixel; the default SourceOver blends it with the red underneath, so
        // its alpha ends up opaque instead.
        Assert.Equal(translucent.A, opaque.GetPixel(1, 1).A);
        Assert.NotEqual(translucent.A, blended.GetPixel(1, 1).A);
    }

    [Fact]
    public void PageScale_multiplies_every_coordinate_before_it_reaches_the_device()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.PageScale = 2f;
            graphics.FillRectangle(brush, 1, 1, 3, 2);
        }

        // (1,1,3,2) scaled by 2 is (2,2,6,4): columns 2..7, rows 2..5 inclusive.
        Assert.Equal((2, 2, 7, 5), InkBounds(bitmap));
        Assert.Equal(6 * 4, CountInk(bitmap));
    }

    [Fact]
    public void SetClip_restricts_drawing_to_the_clip_rectangle()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(4, 4, 4, 4));
            graphics.FillRectangle(brush, 0, 0, 16, 16);
        }

        Assert.Equal((4, 4, 7, 7), InkBounds(bitmap));
        Assert.Equal(4 * 4, CountInk(bitmap));
    }

    [Fact]
    public void IntersectClip_narrows_the_clip_to_the_overlap()
    {
        using var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(0, 0, 8, 8));
            graphics.IntersectClip(new Rectangle(4, 4, 8, 8));
            graphics.FillRectangle(brush, 0, 0, 16, 16);
        }

        Assert.Equal((4, 4, 7, 7), InkBounds(bitmap));
    }

    [Fact]
    public void ResetClip_removes_a_previously_set_clip()
    {
        using var bitmap = new Bitmap(8, 8);
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Ink))
        {
            graphics.SetClip(new Rectangle(0, 0, 2, 2));
            graphics.ResetClip();
            graphics.FillRectangle(brush, 0, 0, 8, 8);
        }

        Assert.Equal(8 * 8, CountInk(bitmap));
    }

    [Fact]
    public void IsVisible_reports_whether_a_point_is_inside_the_clip_and_the_target()
    {
        using var bitmap = new Bitmap(8, 8);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.SetClip(new Rectangle(2, 2, 2, 2));

        Assert.True(graphics.IsVisible(new Point(2, 2)));
        Assert.False(graphics.IsVisible(new Point(0, 0)));
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
}
