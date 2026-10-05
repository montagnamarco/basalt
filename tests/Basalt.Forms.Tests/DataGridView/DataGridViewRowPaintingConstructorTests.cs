using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewRowPaintingConstructorTests
{
    [Fact]
    public void Public_prepaint_constructor_can_paint_row_cells()
    {
        using var grid = NewGrid();
        using var bitmap = new Bitmap(180, 100);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        var rowBounds = grid.GetCellDisplayRectangle(0, 0, false);
        var args = new DataGridViewRowPrePaintEventArgs(grid, graphics, rowBounds, rowBounds, 0,
            DataGridViewElementStates.Visible, "row error", new DataGridViewCellStyle(), true, true);

        args.PaintCells(rowBounds, DataGridViewPaintParts.Background);

        Assert.Equal("row error", args.ErrorText);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(rowBounds.X + 5, rowBounds.Y + 5).ToArgb());
    }

    [Fact]
    public void Public_postpaint_constructor_can_paint_row_cells()
    {
        using var grid = NewGrid();
        using var bitmap = new Bitmap(180, 100);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        var rowBounds = grid.GetCellDisplayRectangle(0, 0, false);
        var args = new DataGridViewRowPostPaintEventArgs(grid, graphics, rowBounds, rowBounds, 0,
            DataGridViewElementStates.Visible, "row error", new DataGridViewCellStyle(), true, true);

        args.PaintCells(rowBounds, DataGridViewPaintParts.Background);

        Assert.Equal("row error", args.ErrorText);
        Assert.Equal(Color.Red.ToArgb(), bitmap.GetPixel(rowBounds.X + 5, rowBounds.Y + 5).ToArgb());
    }

    private static System.Windows.Forms.DataGridView NewGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(180, 100), AllowUserToAddRows = false, ColumnCount = 1 };
        grid.DefaultCellStyle.BackColor = Color.Red;
        grid.Rows.Add("value");
        return grid;
    }
}
