using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>CellPainting (and its Handled flag) and RowPrePaint/RowPostPaint: proved by which handlers fire and by reading pixels.</summary>
public class DataGridViewCellPaintingTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static System.Windows.Forms.DataGridView GridWithOneColumnAndRows(int rows)
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 150) };
        grid.Columns.Add("A", "Column A");
        grid.Columns[0].Width = 150;

        for (var i = 0; i < rows; i++) grid.Rows.Add();

        return grid;
    }

    [Fact]
    public void CellPainting_fires_once_per_data_cell_and_once_per_header_cell()
    {
        var grid = GridWithOneColumnAndRows(2);
        var dataCells = 0;
        var headerCells = 0;
        grid.CellPainting += (_, e) =>
        {
            // A data cell has both indices >= 0; a column header, row header or the top-left
            // corner each leave one or both of them at -1.
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) dataCells++;
            else headerCells++;
        };

        Draw(grid);

        Assert.Equal(2, dataCells);

        // One column header, one row header per row, and the top-left corner.
        Assert.Equal(4, headerCells);
    }

    [Fact]
    public void A_handler_that_sets_Handled_on_CellPainting_skips_the_default_painting()
    {
        var grid = GridWithOneColumnAndRows(1);
        grid.DefaultCellStyle.BackColor = Color.Red;
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            e.Handled = true;
        };

        var bitmap = Draw(grid);

        // The cell's own default Paint would have filled it red; a Handled handler that paints
        // nothing itself leaves the grid's own BackgroundColor showing through instead. Sampled
        // past RowHeadersWidth so the pixel actually lands on the data cell, not its row header.
        var pixel = bitmap.GetPixel(grid.RowHeadersWidth + 10, grid.ColumnHeadersHeight + 10);
        Assert.NotEqual(Color.Red.ToArgb(), pixel.ToArgb());
    }

    [Fact]
    public void PaintBackground_from_CellPainting_fills_only_the_background_not_the_content()
    {
        var grid = GridWithOneColumnAndRows(1);
        grid.Rows[0].Cells[0].Value = "text";
        grid.DefaultCellStyle.BackColor = Color.Blue;
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            e.PaintBackground(e.CellBounds, true);
            e.Handled = true;
        };

        var bitmap = Draw(grid);

        var pixel = bitmap.GetPixel(grid.RowHeadersWidth + 10, grid.ColumnHeadersHeight + 10);
        Assert.Equal(Color.Blue.ToArgb(), pixel.ToArgb());
    }

    [Fact]
    public void RowPrePaint_and_RowPostPaint_fire_once_per_row_in_order()
    {
        var grid = GridWithOneColumnAndRows(2);
        var order = new List<string>();
        grid.RowPrePaint += (_, e) => order.Add($"pre {e.RowIndex}");
        grid.RowPostPaint += (_, e) => order.Add($"post {e.RowIndex}");

        Draw(grid);

        Assert.Equal(["pre 0", "post 0", "pre 1", "post 1"], order);
    }

    [Fact]
    public void A_handler_that_sets_Handled_on_RowPrePaint_skips_the_grid_s_own_painting_of_that_row()
    {
        var grid = GridWithOneColumnAndRows(1);
        grid.DefaultCellStyle.BackColor = Color.Red;
        grid.RowPrePaint += (_, e) => e.Handled = true;

        var bitmap = Draw(grid);

        var pixel = bitmap.GetPixel(grid.RowHeadersWidth + 10, grid.ColumnHeadersHeight + 10);
        Assert.NotEqual(Color.Red.ToArgb(), pixel.ToArgb());
    }

    [Fact]
    public void RowPrePaint_knows_the_first_and_last_visible_row()
    {
        var grid = GridWithOneColumnAndRows(3);
        var firstFlags = new List<bool>();
        var lastFlags = new List<bool>();
        grid.RowPrePaint += (_, e) =>
        {
            firstFlags.Add(e.IsFirstDisplayedRow);
            lastFlags.Add(e.IsLastVisibleRow);
        };

        Draw(grid);

        Assert.Equal([true, false, false], firstFlags);
        Assert.Equal([false, false, true], lastFlags);
    }
}
