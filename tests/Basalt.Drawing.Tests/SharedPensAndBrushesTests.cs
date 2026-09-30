using System.Drawing;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Pens.Black and Brushes.Red are one object for everybody who asks. GDI+
/// refuses to change them, because a change would show up in code that never
/// asked for it.
/// </summary>
public class SharedPensAndBrushesTests
{
    [Fact]
    public void A_named_brush_has_the_colour_of_its_name_and_is_the_same_object_each_time()
    {
        var brush = Assert.IsType<SolidBrush>(Brushes.SteelBlue);

        Assert.Equal(Color.SteelBlue.ToArgb(), brush.Color.ToArgb());
        Assert.Same(Brushes.SteelBlue, Brushes.SteelBlue);
        Assert.NotSame(Brushes.SteelBlue, Brushes.Tomato);
    }

    [Fact]
    public void A_named_pen_is_one_pixel_wide()
    {
        Assert.Equal(Color.Tomato.ToArgb(), Pens.Tomato.Color.ToArgb());
        Assert.Equal(1f, Pens.Tomato.Width);
        Assert.Same(Pens.Tomato, Pens.Tomato);
    }

    [Fact]
    public void A_shared_pen_or_brush_cannot_be_changed_but_its_copy_can()
    {
        Assert.Throws<ArgumentException>(() => Pens.Black.Width = 3);
        Assert.Throws<ArgumentException>(() => Pens.Black.Color = Color.Red);
        Assert.Throws<ArgumentException>(() => ((SolidBrush)Brushes.Black).Color = Color.Red);
        Assert.Equal(1f, Pens.Black.Width);

        using var copy = (Pen)Pens.Black.Clone();
        copy.Width = 3;

        Assert.Equal(3f, copy.Width);
    }

    [Fact]
    public void A_system_brush_has_the_colour_SystemColors_gives()
    {
        var brush = Assert.IsType<SolidBrush>(SystemBrushes.Control);

        Assert.Equal(SystemColors.Control.ToArgb(), brush.Color.ToArgb());
        Assert.Equal(SystemColors.Highlight.ToArgb(), SystemPens.Highlight.Color.ToArgb());
        Assert.Same(SystemBrushes.Control, SystemBrushes.FromSystemColor(SystemColors.Control));
    }

    [Fact]
    public void A_colour_that_is_not_of_the_system_has_no_system_brush()
    {
        Assert.Throws<ArgumentException>(() => SystemBrushes.FromSystemColor(Color.Red));
        Assert.Throws<ArgumentException>(() => SystemPens.FromSystemColor(Color.FromArgb(1, 2, 3)));
    }

    [Fact]
    public void Setting_the_colour_of_a_pen_made_from_a_brush_is_what_it_draws_with()
    {
        using var brush = new SolidBrush(Color.Red);
        using var pen = new Pen(brush);

        pen.Color = Color.Blue;

        Assert.Equal(Color.Blue.ToArgb(), ((SolidBrush)pen.Brush).Color.ToArgb());
    }
}
