using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>Column headers as raised buttons, grid lines, selection highlight and alternating rows: proved by reading pixels.</summary>
public class DataGridViewPaintingTests
{
    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    /// <summary>
    /// Blank rows (no cell value, so no text glyph can land on a sampled pixel) under one column,
    /// wide enough that a column-interior sample point never falls on a cell border line either.
    /// </summary>
    private static System.Windows.Forms.DataGridView GridWithOneColumnAndRows(int rows)
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 150) };
        grid.Columns.Add("A", "Column A");
        grid.Columns[0].Width = 150;

        for (var i = 0; i < rows; i++) grid.Rows.Add();

        return grid;
    }

    [Fact]
    public void A_column_header_paints_as_a_raised_button_not_flat_fill()
    {
        var grid = GridWithOneColumnAndRows(1);

        var bitmap = Draw(grid);

        // ControlPaint.DrawButton always draws a highlight on the top/left edge and a shadow on
        // the bottom/right edge; a flat fill (the defect this guards) would make every pixel of
        // the header the same colour, including its very first and very last rows.
        var top = bitmap.GetPixel(2, 0).ToArgb();
        var bottom = bitmap.GetPixel(2, grid.ColumnHeadersHeight - 1).ToArgb();

        Assert.NotEqual(top, bottom);
    }

    [Fact]
    public void A_selected_cell_paints_with_the_style_s_SelectionBackColor()
    {
        var grid = GridWithOneColumnAndRows(1);
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        grid.DefaultCellStyle.SelectionBackColor = Color.DarkGreen;
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Rows[0].Cells[0].Selected = true;

        var bitmap = Draw(grid);

        var midY = grid.ColumnHeadersHeight + grid.Rows[0].Height / 2;
        Assert.Equal(Color.DarkGreen.ToArgb(), bitmap.GetPixel(grid.RowHeadersWidth + 5, midY).ToArgb());
    }

    [Fact]
    public void An_unselected_cell_does_not_paint_with_the_selection_color()
    {
        var grid = GridWithOneColumnAndRows(1);
        grid.DefaultCellStyle.SelectionBackColor = Color.DarkGreen;
        grid.DefaultCellStyle.BackColor = Color.White;

        var bitmap = Draw(grid);

        var midY = grid.ColumnHeadersHeight + grid.Rows[0].Height / 2;
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(grid.RowHeadersWidth + 5, midY).ToArgb());
    }

    [Fact]
    public void Alternating_rows_use_AlternatingRowsDefaultCellStyle_s_BackColor_on_odd_rows_only()
    {
        var grid = GridWithOneColumnAndRows(2);
        grid.DefaultCellStyle.BackColor = Color.White;
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.LightBlue;

        var bitmap = Draw(grid);

        var firstRowY = grid.ColumnHeadersHeight + grid.Rows[0].Height / 2;
        var secondRowY = grid.ColumnHeadersHeight + grid.Rows[0].Height + grid.Rows[1].Height / 2;

        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(grid.RowHeadersWidth + 5, firstRowY).ToArgb());
        Assert.Equal(Color.LightBlue.ToArgb(), bitmap.GetPixel(grid.RowHeadersWidth + 5, secondRowY).ToArgb());
    }

    [AvaloniaFact]
    public void The_current_cell_draws_a_focus_rectangle_only_while_the_grid_is_focused()
    {
        var form = new Form { ClientSize = new Size(220, 160) };
        var grid = GridWithOneColumnAndRows(1);
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.CurrentCell = grid.Rows[0].Cells[0];

        grid.Focus();
        var focusedBitmap = Draw(grid);

        // A focus rectangle alternates pixels along its border; the cell's top edge inside the
        // border should not be a uniform BackColor when focused, unlike the unfocused reading.
        var cellTop = grid.ColumnHeadersHeight;
        var cellLeft = grid.RowHeadersWidth + 1;
        var focusedTopRow = focusedBitmap.GetPixel(cellLeft, cellTop).ToArgb();

        form.Close();

        var unfocusedGrid = GridWithOneColumnAndRows(1);
        unfocusedGrid.CurrentCell = unfocusedGrid.Rows[0].Cells[0];
        var unfocusedBitmap = Draw(unfocusedGrid);
        var unfocusedTopRow = unfocusedBitmap.GetPixel(cellLeft, cellTop).ToArgb();

        Assert.NotEqual(focusedTopRow, unfocusedTopRow);
    }
}
