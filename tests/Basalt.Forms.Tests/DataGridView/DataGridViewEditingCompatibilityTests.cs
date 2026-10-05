using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewEditingCompatibilityTests
{
    [Fact]
    public void Begin_edit_stops_when_the_callback_removes_its_cell()
    {
        using var grid = OneCellGrid();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.CellBeginEdit += (_, _) => grid.Rows.Clear();
        Assert.False(grid.BeginEdit(true));
        Assert.Null(grid.EditingControl);
        Assert.False(grid.EditingPanel.Visible);
    }

    [AvaloniaFact]
    public void Validation_cancellation_of_editing_does_not_write_a_stale_cell()
    {
        var grid = OneCellGrid();
        using var form = ShowWith(grid);
        var cell = grid.Rows[0].Cells[0];
        grid.CurrentCell = cell;
        Assert.True(grid.BeginEdit(true));
        Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl).Text = "draft";
        grid.CellValidating += (_, _) => grid.CancelEdit();
        Assert.False(grid.CommitEdit(DataGridViewDataErrorContexts.Commit));
        Assert.Equal("initial", cell.Value);
        Assert.Null(grid.EditingControl);
        Assert.False(grid.EditingPanel.Visible);
    }

    [AvaloniaFact]
    public void Showing_callback_cancelling_editing_does_not_reattach_the_editor()
    {
        var grid = OneCellGrid();
        using var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.EditingControlShowing += (_, _) => grid.CancelEdit();
        Assert.False(grid.BeginEdit(true));
        Assert.Null(grid.EditingControl);
        Assert.False(grid.EditingPanel.Visible);
    }

    [AvaloniaFact]
    public void CommitEdit_writes_the_value_without_closing_the_editor()
    {
        var grid = OneCellGrid();
        using var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.True(grid.BeginEdit(true));
        var editor = Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        editor.Text = "changed";
        Assert.Same(grid.EditingPanel, editor.Parent);
        Assert.Same(grid, grid.EditingPanel.Parent);
        Assert.Equal(Point.Empty, editor.Location);
        var valueChanges = 0;
        grid.CellValueChanged += (_, _) => valueChanges++;

        Assert.True(grid.CommitEdit(DataGridViewDataErrorContexts.Commit));

        Assert.Same(editor, grid.EditingControl);
        Assert.True(grid.IsCurrentCellInEditMode);
        Assert.False(grid.IsCurrentCellDirty);
        Assert.Equal("changed", grid.CurrentCell!.Value);
        Assert.Equal(1, valueChanges);
    }

    [AvaloniaFact]
    public void CommitEdit_respects_validation_and_keeps_the_uncommitted_value_in_the_editor()
    {
        var grid = OneCellGrid();
        using var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var editor = Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        editor.Text = "invalid";
        grid.CellValidating += (_, e) => e.Cancel = true;

        Assert.False(grid.CommitEdit(DataGridViewDataErrorContexts.Commit));
        Assert.Equal("initial", grid.CurrentCell!.Value);
        Assert.Equal("invalid", editor.Text);
        Assert.True(grid.IsCurrentCellInEditMode);
        Assert.True(grid.IsCurrentCellDirty);
    }

    [AvaloniaFact]
    public void RefreshEdit_reloads_an_externally_changed_value_and_clears_editor_dirty_state()
    {
        var grid = OneCellGrid();
        using var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var editor = Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        editor.Text = "draft";
        grid.CurrentCell.Value = "server";

        Assert.True(grid.RefreshEdit());

        Assert.Same(editor, grid.EditingControl);
        Assert.Equal("server", editor.Text);
        Assert.False(editor.EditingControlValueChanged);
        Assert.False(grid.IsCurrentCellDirty);
        Assert.True(grid.IsCurrentCellInEditMode);
    }

    [Fact]
    public void UpdateCellValue_repaints_a_virtual_value_changed_outside_the_grid()
    {
        var grid = OneCellGrid();
        grid.VirtualMode = true;
        var storedValue = "Before";
        grid.CellValueNeeded += (_, e) => e.Value = storedValue;
        using var before = Draw(grid);
        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;
        var initialWidth = grid.Columns[0].Width;

        storedValue = "After an external change that makes this column wider";
        grid.UpdateCellValue(0, 0);
        using var after = Draw(grid);

        var bounds = Rectangle.Intersect(grid.GetCellDisplayRectangle(0, 0, false),
            new Rectangle(0, 0, before.Width, before.Height));
        Assert.True(PixelsDiffer(before, after, bounds));
        Assert.True(grid.Columns[0].Width > initialWidth);
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.UpdateCellValue(1, 0));
    }

    [Fact]
    public void Error_text_update_methods_repaint_virtual_cell_and_row_errors()
    {
        var grid = OneCellGrid();
        grid.VirtualMode = true;
        var cellError = "first cell error";
        var rowError = "first row error";
        grid.CellErrorTextNeeded += (_, e) => e.ErrorText = cellError;
        grid.RowErrorTextNeeded += (_, e) => e.ErrorText = rowError;
        var seenCellErrors = new List<string?>();
        var seenRowErrors = new List<string?>();
        grid.CellPainting += (_, e) =>
        {
            if (e.RowIndex == 0 && e.ColumnIndex == 0) seenCellErrors.Add(e.ErrorText);
        };
        grid.RowPrePaint += (_, e) => seenRowErrors.Add(e.ErrorText);
        using var before = Draw(grid);

        cellError = "updated cell error";
        rowError = "updated row error";
        grid.UpdateCellErrorText(0, 0);
        grid.UpdateRowErrorText(0, 0);
        using var after = Draw(grid);

        Assert.Contains("updated cell error", seenCellErrors);
        Assert.Contains("updated row error", seenRowErrors);

        rowError = "single row update";
        grid.UpdateRowErrorText(0);
        using var singleRowUpdate = Draw(grid);
        Assert.Contains("single row update", seenRowErrors);

        Assert.Throws<ArgumentOutOfRangeException>(() => grid.UpdateCellErrorText(2, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid.UpdateRowErrorText(0, 1));
    }

    private static System.Windows.Forms.DataGridView OneCellGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(200, 100) };
        grid.Columns.Add("Value", "Value");
        grid.Rows.Add("initial");
        return grid;
    }

    private static Form ShowWith(System.Windows.Forms.DataGridView grid)
    {
        var form = new Form { ClientSize = new Size(220, 120) };
        form.Controls.Add(grid);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        grid.Focus();
        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(Point.Empty, control.Size));
        return bitmap;
    }

    private static bool PixelsDiffer(Bitmap first, Bitmap second, Rectangle bounds)
    {
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            if (first.GetPixel(x, y).ToArgb() != second.GetPixel(x, y).ToArgb()) return true;
        }

        return false;
    }
}
