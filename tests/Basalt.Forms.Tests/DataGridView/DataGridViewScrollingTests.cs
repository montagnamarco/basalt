using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewScrollingTests
{
    [Fact]
    public void Custom_row_prepaint_cannot_overwrite_a_frozen_row_when_scrolling()
    {
        using var grid = new System.Windows.Forms.DataGridView
        {
            Size = new Size(140, 100), RowHeadersVisible = false, ColumnHeadersVisible = false,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 100 });
        grid.RowCount = 8;
        grid.Rows[0].Frozen = true;
        grid.Rows[0].Cells[0].Style.BackColor = Color.Blue;
        var customPaints = 0;
        grid.RowPrePaint += (_, e) =>
        {
            if (e.RowIndex == 0) return;
            using var brush = new SolidBrush(Color.Red);
            e.Graphics.FillRectangle(brush, e.RowBounds);
            e.Handled = true;
            customPaints++;
        };
        grid.PerformLayout();
        grid.FirstDisplayedScrollingRowIndex = 2;
        using var raster = Draw(grid);
        Assert.True(customPaints > 0);
        Assert.Equal(Color.Blue.ToArgb(), raster.GetPixel(20, 10).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), raster.GetPixel(20, 30).ToArgb());
    }

    [Fact]
    public void Frozen_rows_and_columns_keep_their_pixels_while_other_cells_scroll()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(140, 100), RowHeadersVisible = false, ColumnHeadersVisible = false };
        for (var index = 0; index < 4; index++) grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 60 });
        grid.RowCount = 8;
        grid.Columns[0].Frozen = true;
        grid.Rows[0].Frozen = true;
        grid.Rows[0].Cells[0].Style.BackColor = Color.Red;
        grid.Rows[2].Cells[0].Style.BackColor = Color.Red;
        grid.Rows[0].Cells[2].Style.BackColor = Color.Red;
        grid.Rows[2].Cells[2].Style.BackColor = Color.Blue;
        grid.PerformLayout();
        grid.HorizontalScrollingOffset = 80;
        grid.FirstDisplayedScrollingRowIndex = 2;
        using var raster = Draw(grid);
        Assert.Equal(Color.Red.ToArgb(), raster.GetPixel(20, 10).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), raster.GetPixel(20, 30).ToArgb());
        Assert.Equal(Color.Red.ToArgb(), raster.GetPixel(70, 10).ToArgb());
        Assert.Equal(Color.Blue.ToArgb(), raster.GetPixel(70, 30).ToArgb());
        Assert.Equal(0, grid.HitTest(20, 10).ColumnIndex);
        Assert.Equal(0, grid.HitTest(20, 10).RowIndex);
        Assert.Equal(2, grid.HitTest(70, 30).ColumnIndex);
        Assert.Equal(2, grid.HitTest(70, 30).RowIndex);
    }

    [Fact]
    public void Scrolling_changes_hit_testing_and_the_pixels_of_visible_cells()
    {
        using var grid = new System.Windows.Forms.DataGridView { Size = new Size(120, 100), RowHeadersVisible = false, ColumnHeadersVisible = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 100 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Width = 100 });
        grid.RowCount = 8;
        grid.Rows[0].Cells[0].Style.BackColor = Color.Red;
        grid.Rows[3].Cells[1].Style.BackColor = Color.Blue;
        grid.PerformLayout();
        using var before = Draw(grid);
        Assert.Equal(Color.Red.ToArgb(), before.GetPixel(20, 10).ToArgb());
        var changes = 0;
        grid.Scroll += (_, _) => changes++;
        grid.HorizontalScrollingOffset = 80;
        grid.FirstDisplayedScrollingRowIndex = 3;
        var hit = grid.HitTest(40, 10);
        Assert.Equal(1, hit.ColumnIndex);
        Assert.Equal(3, hit.RowIndex);
        Assert.Equal(3, grid.FirstDisplayedScrollingRowIndex);
        Assert.Equal(80, grid.FirstDisplayedScrollingColumnHiddenWidth);
        using var after = Draw(grid);
        Assert.Equal(Color.Blue.ToArgb(), after.GetPixel(40, 10).ToArgb());
        Assert.Equal(2, changes);
    }

    private static Bitmap Draw(System.Windows.Forms.DataGridView grid)
    {
        var bitmap = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bitmap, grid.ClientRectangle);
        return bitmap;
    }
}
