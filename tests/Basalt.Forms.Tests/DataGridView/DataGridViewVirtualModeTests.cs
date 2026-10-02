using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>The virtual-mode seam that does not need the cell/row classes themselves: CellErrorTextNeeded, IsCurrentRowDirty/RowDirtyStateNeeded and CancelRowEdit.</summary>
public class DataGridViewVirtualModeTests
{
    private static Form ShowWith(System.Windows.Forms.DataGridView grid)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return form;
    }

    private static System.Windows.Forms.DataGridView OneByOneGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100), VirtualMode = true };
        grid.Columns.Add("A", "A");
        grid.Rows.Add((object)1);

        return grid;
    }

    [Fact]
    public void CellErrorTextNeeded_supplies_the_error_text_a_CellPainting_handler_reads_in_virtual_mode()
    {
        var grid = OneByOneGrid();
        grid.CellErrorTextNeeded += (_, e) => e.ErrorText = "from the host";
        string? seen = null;
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) seen = e.ErrorText;
        };

        using var bitmap = new Bitmap(grid.Width, grid.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        grid.DrawToBitmap(bitmap, new Rectangle(0, 0, grid.Width, grid.Height));

        Assert.Equal("from the host", seen);
    }

    [Fact]
    public void Without_VirtualMode_CellErrorTextNeeded_never_fires_and_the_cell_s_own_ErrorText_is_used()
    {
        var grid = OneByOneGrid();
        grid.VirtualMode = false;
        grid.Rows[0].Cells[0].ErrorText = "stored on the cell";
        var fired = false;
        grid.CellErrorTextNeeded += (_, _) => fired = true;
        string? seen = null;
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0) seen = e.ErrorText;
        };

        using var bitmap = new Bitmap(grid.Width, grid.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        grid.DrawToBitmap(bitmap, new Rectangle(0, 0, grid.Width, grid.Height));

        Assert.False(fired);
        Assert.Equal("stored on the cell", seen);
    }

    [AvaloniaFact]
    public void IsCurrentRowDirty_in_virtual_mode_comes_from_RowDirtyStateNeeded_not_IsCurrentCellDirty()
    {
        var grid = OneByOneGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.RowDirtyStateNeeded += (_, e) => e.Response = true;

        Assert.True(grid.IsCurrentRowDirty);

        form.Close();
    }

    [AvaloniaFact]
    public void CancelRowEdit_can_refuse_to_let_Escape_cancel_a_dirty_row_in_virtual_mode()
    {
        var grid = OneByOneGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        grid.RowDirtyStateNeeded += (_, e) => e.Response = true;
        grid.CancelRowEdit += (_, e) => e.Response = false;

        form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.True(grid.IsCurrentCellInEditMode, "CancelRowEdit refusing the cancel must keep the editing control open");

        form.Close();
    }
}
