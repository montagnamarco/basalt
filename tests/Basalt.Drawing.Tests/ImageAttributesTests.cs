using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace Basalt.Drawing.Tests;

/// <summary>
/// ImageAttributes.ToColorFilter: the internal SkiaSharp filter that stands
/// in for what GDI+ does to every pixel of a drawn image. Each test draws a
/// 1x1 source bitmap through an SKPaint carrying the filter and reads the
/// one pixel of the destination back.
/// </summary>
public class ImageAttributesTests
{
    [Fact]
    public void A_colour_matrix_is_the_row_vector_R_G_B_A_1_times_the_matrix()
    {
        // outR = inG, outG = inB, outB = 0, outA = inA: an asymmetric map
        // that a row/column swap inside the implementation would not survive.
        var matrix = new ColorMatrix();
        matrix[0, 0] = 0;
        matrix[1, 0] = 1;
        matrix[1, 1] = 0;
        matrix[2, 1] = 1;
        matrix[2, 2] = 0;

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);

        var result = Render(attributes, Color.FromArgb(255, 10, 200, 80));

        Assert.Equal((200, 80, 0, 255), result);
    }

    [Fact]
    public void SetColorKey_makes_every_channel_at_the_boundary_transparent()
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorKey(Color.FromArgb(255, 100, 100, 100), Color.FromArgb(255, 150, 150, 150));

        var atHighBoundary = Render(attributes, Color.FromArgb(255, 150, 150, 150));
        var justAboveBoundary = Render(attributes, Color.FromArgb(255, 151, 150, 150));

        Assert.Equal(0, atHighBoundary.A);
        Assert.Equal(255, justAboveBoundary.A);
    }

    [Fact]
    public void ColorMatrixFlag_SkipGrays_leaves_a_pixel_with_equal_R_G_B_untouched()
    {
        var halve = new ColorMatrix();
        halve[0, 0] = 0.5f;
        halve[1, 1] = 0.5f;
        halve[2, 2] = 0.5f;

        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(halve, ColorMatrixFlag.SkipGrays);

        var gray = Render(attributes, Color.FromArgb(255, 100, 100, 100));
        var notGray = Render(attributes, Color.FromArgb(255, 200, 50, 50));

        Assert.Equal((100, 100, 100, 255), gray);
        Assert.Equal((100, 25, 25, 255), notGray);
    }

    /// <summary>
    /// All or nothing, as GDI+ documents: a matrix set for Default and a gamma
    /// set for Bitmap give bitmaps the gamma alone, not the matrix too.
    /// </summary>
    [Fact]
    public void A_category_with_a_setting_of_its_own_takes_nothing_from_Default()
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix([[0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));
        attributes.SetGamma(1f, ColorAdjustType.Bitmap);

        var throughBitmap = Render(attributes, ColorAdjustType.Bitmap, Color.FromArgb(255, 200, 100, 50));
        var throughBrush = Render(attributes, ColorAdjustType.Brush, Color.FromArgb(255, 200, 100, 50));

        // A gamma of 1 changes nothing; the matrix, which blacks everything out, is not used.
        Assert.Equal((200, 100, 50, 255), throughBitmap);

        // The brush has nothing of its own, so it gets Default's matrix.
        Assert.Equal((0, 0, 0, 255), throughBrush);
    }

    /// <summary>
    /// The key compares the colour itself, whatever its alpha: a
    /// half-transparent pixel of the key colour is keyed out too.
    /// </summary>
    [Fact]
    public void A_colour_key_catches_its_colour_at_any_alpha()
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorKey(Color.FromArgb(200, 100, 50), Color.FromArgb(200, 100, 50));

        var keyed = Render(attributes, ColorAdjustType.Default, Color.FromArgb(128, 200, 100, 50));

        Assert.Equal(0, keyed.Item4);
    }

    [Fact]
    public void Clearing_a_setting_never_set_leaves_the_category_using_Default()
    {
        using var attributes = new ImageAttributes();
        attributes.SetThreshold(0.5f);

        attributes.ClearColorKey(ColorAdjustType.Pen);

        Assert.Equal((255, 255, 255, 255), Render(attributes, ColorAdjustType.Pen, Color.FromArgb(255, 200, 200, 200)));
    }

    [Fact]
    public void A_colour_map_changed_after_SetRemapTable_does_not_change_the_adjustment()
    {
        using var attributes = new ImageAttributes();
        var map = new ColorMap { OldColor = Color.FromArgb(255, 1, 2, 3), NewColor = Color.FromArgb(255, 9, 9, 9) };
        attributes.SetRemapTable([map]);

        map.NewColor = Color.FromArgb(255, 7, 7, 7);

        Assert.Equal((9, 9, 9, 255), Render(attributes, ColorAdjustType.Default, Color.FromArgb(255, 1, 2, 3)));
    }

    /// <summary>
    /// The matrix works on the colour itself: halving red on a half-transparent
    /// pixel halves its red, it does not also multiply by its alpha.
    /// </summary>
    [Fact]
    public void A_colour_matrix_sees_the_straight_colour_of_a_transparent_pixel()
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix([[0.5f, 0, 0, 0, 0], [0, 1, 0, 0, 0], [0, 0, 1, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));

        var result = Render(attributes, ColorAdjustType.Default, Color.FromArgb(128, 200, 100, 50));

        Assert.InRange(result.Item1, 99, 101);
        Assert.InRange(result.Item2, 99, 101);
        Assert.Equal(128, result.Item4);
    }

    [Fact]
    public void A_category_with_nothing_of_its_own_set_falls_back_to_Default()
    {
        using var attributes = new ImageAttributes();
        attributes.SetGamma(2.2f);

        var throughDefault = Render(attributes, ColorAdjustType.Default, Color.FromArgb(255, 128, 128, 128));
        var throughPen = Render(attributes, ColorAdjustType.Pen, Color.FromArgb(255, 128, 128, 128));

        Assert.Equal(throughDefault, throughPen);
        Assert.NotEqual((128, 128, 128, 255), throughPen);
    }

    [Fact]
    public void SetNoOp_disables_adjustment_for_that_category_even_though_Default_has_one()
    {
        using var attributes = new ImageAttributes();
        attributes.SetGamma(2.2f);
        attributes.SetNoOp(ColorAdjustType.Pen);

        var throughPen = Render(attributes, ColorAdjustType.Pen, Color.FromArgb(255, 128, 128, 128));

        Assert.Equal((128, 128, 128, 255), throughPen);
    }

    [Fact]
    public void SetThreshold_rounds_each_of_R_G_B_to_black_or_white_and_leaves_alpha_alone()
    {
        using var attributes = new ImageAttributes();
        attributes.SetThreshold(0.5f);

        var result = Render(attributes, Color.FromArgb(200, 100, 140, 127));

        Assert.Equal((0, 255, 0, 200), result);
    }

    [Fact]
    public void SetRemapTable_replaces_only_the_exact_colour_it_names()
    {
        using var attributes = new ImageAttributes();
        attributes.SetRemapTable([new ColorMap { OldColor = Color.FromArgb(255, 10, 20, 30), NewColor = Color.FromArgb(255, 200, 100, 50) }]);

        var remapped = Render(attributes, Color.FromArgb(255, 10, 20, 30));
        var untouched = Render(attributes, Color.FromArgb(255, 10, 20, 31));

        Assert.Equal((200, 100, 50, 255), remapped);
        Assert.Equal((10, 20, 31, 255), untouched);
    }

    [Fact]
    public void SetBrushRemapTable_is_equivalent_to_SetRemapTable_for_the_Brush_category()
    {
        using var attributes = new ImageAttributes();
        attributes.SetBrushRemapTable([new ColorMap { OldColor = Color.FromArgb(255, 1, 2, 3), NewColor = Color.FromArgb(255, 9, 8, 7) }]);

        var throughBrush = Render(attributes, ColorAdjustType.Brush, Color.FromArgb(255, 1, 2, 3));
        var throughDefault = Render(attributes, ColorAdjustType.Default, Color.FromArgb(255, 1, 2, 3));

        Assert.Equal((9, 8, 7, 255), throughBrush);
        Assert.Equal((1, 2, 3, 255), throughDefault);
    }

    [Fact]
    public void ToColorFilter_is_null_when_nothing_was_set()
    {
        using var attributes = new ImageAttributes();

        Assert.Null(attributes.ToColorFilter(ColorAdjustType.Default));
    }

    [Fact]
    public void GetAdjustedPalette_applies_the_same_adjustments_to_every_palette_entry()
    {
        using var attributes = new ImageAttributes();
        attributes.SetRemapTable([new ColorMap { OldColor = Color.Black, NewColor = Color.Red }]);

        var palette = new ColorPalette([Color.Black, Color.White]);
        attributes.GetAdjustedPalette(palette, ColorAdjustType.Default);

        Assert.Equal(Color.Red.ToArgb(), palette.Entries[0].ToArgb());
        Assert.Equal(Color.White.ToArgb(), palette.Entries[1].ToArgb());
    }

    [Fact]
    public void SetOutputChannel_is_not_implemented()
    {
        using var attributes = new ImageAttributes();

        Assert.Throws<NotImplementedException>(() => attributes.SetOutputChannel(ColorChannelFlag.ColorChannelC));
    }

    [Fact]
    public void A_colour_matrix_set_as_AltGrays_is_not_implemented()
    {
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix(), ColorMatrixFlag.AltGrays);

        Assert.Throws<NotImplementedException>(() => attributes.ToColorFilter(ColorAdjustType.Default));
    }

    [Fact]
    public void ColorMatrix_defaults_to_the_identity_and_the_indexer_matches_the_named_properties()
    {
        var matrix = new ColorMatrix();

        Assert.Equal(1f, matrix[0, 0]);
        Assert.Equal(0f, matrix[0, 1]);

        matrix.Matrix23 = 0.75f;
        Assert.Equal(0.75f, matrix[2, 3]);

        matrix[4, 1] = 0.25f;
        Assert.Equal(0.25f, matrix.Matrix41);
    }

    private static (int R, int G, int B, int A) Render(ImageAttributes attributes, Color source) =>
        Render(attributes, ColorAdjustType.Default, source);

    private static (int R, int G, int B, int A) Render(ImageAttributes attributes, ColorAdjustType type, Color source)
    {
        using var filter = attributes.ToColorFilter(type);

        using var sourceBitmap = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        sourceBitmap.SetPixel(0, 0, new SKColor(source.R, source.G, source.B, source.A));

        using var destination = new SKBitmap(1, 1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using (var canvas = new SKCanvas(destination))
        using (var paint = new SKPaint { ColorFilter = filter })
        {
            canvas.DrawBitmap(sourceBitmap, 0, 0, paint);
        }

        var pixel = destination.GetPixel(0, 0);
        return (pixel.Red, pixel.Green, pixel.Blue, pixel.Alpha);
    }
}
