using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.PropertyGrid;

/// <summary>The classic look, read back from pixels: a category row spans the whole band in the margin's own colour, a property row's margin alone does.</summary>
public class PropertyGridPaintTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private class Sample
    {
        [Category("General")]
        public string Name { get; set; } = "Alpha";
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void A_category_row_fills_its_whole_band_with_the_margin_colour_a_property_row_s_margin_alone_does()
    {
        var grid = new System.Windows.Forms.PropertyGrid
        {
            Font = Font,
            SelectedObject = new Sample(),
            PropertySort = PropertySort.CategorizedAlphabetical,
            ToolbarVisible = false,
            HelpVisible = false,
            Bounds = new Rectangle(0, 0, 200, 150),
        };

        using var bitmap = Draw(grid);

        // Row 0: the "General" category heading, its whole band (margin, name and value together) in CategorySplitterColor.
        var categoryRow = grid.RowBounds(0);
        Assert.Equal(grid.CategorySplitterColor.ToArgb(), bitmap.GetPixel(categoryRow.Left + 2, categoryRow.Top + 3).ToArgb());
        Assert.Equal(grid.CategorySplitterColor.ToArgb(), bitmap.GetPixel(categoryRow.Right - 2, categoryRow.Top + 3).ToArgb());

        // Row 1: the "Name" property. Its margin sliver is SystemColors.Control...
        var margin = grid.MarginBounds(1);
        Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(margin.Left + margin.Width / 2, margin.Top + margin.Height / 2).ToArgb());

        // ...but past the margin its name cell is the grid's own ViewBackColor (Window), not the margin's colour.
        var name = grid.NameBounds(1);
        Assert.Equal(grid.ViewBackColor.ToArgb(), bitmap.GetPixel(name.Left + 3, name.Top + 3).ToArgb());
        Assert.NotEqual(SystemColors.Control.ToArgb(), bitmap.GetPixel(name.Left + 3, name.Top + 3).ToArgb());
    }
}
