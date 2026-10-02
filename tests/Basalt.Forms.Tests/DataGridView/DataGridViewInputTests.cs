using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>Click selection, arrow-key navigation and F2/Enter/Escape editing with the plain TextBox editing control.</summary>
public class DataGridViewInputTests
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

    private static System.Windows.Forms.DataGridView TwoByTwoGrid()
    {
        var grid = new System.Windows.Forms.DataGridView { Size = new Size(260, 160) };
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add((object)1, (object)2);
        grid.Rows.Add((object)3, (object)4);

        return grid;
    }

    private static void Click(Form form, Control control, Point clientPoint)
    {
        var point = new Avalonia.Point(control.Left + clientPoint.X, control.Top + clientPoint.Y);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Clicking_a_cell_in_CellSelect_mode_makes_it_the_current_and_only_selected_cell()
    {
        var grid = TwoByTwoGrid();
        grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
        var form = ShowWith(grid);

        var bounds = grid.GetCellDisplayRectangle(1, 0, cutOverflow: false);
        Click(form, grid, new Point(bounds.X + 3, bounds.Y + 3));

        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
        Assert.Single(grid.SelectedCells);
        Assert.Same(grid.Rows[0].Cells[1], grid.SelectedCells[0]);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_row_header_in_RowHeaderSelect_mode_selects_the_whole_row()
    {
        var grid = TwoByTwoGrid();
        grid.SelectionMode = DataGridViewSelectionMode.RowHeaderSelect;
        var form = ShowWith(grid);

        var headerBounds = grid.GetCellDisplayRectangle(-1, 1, cutOverflow: false);
        Click(form, grid, new Point(headerBounds.X + 3, headerBounds.Y + 3));

        Assert.Single(grid.SelectedRows);
        Assert.Same(grid.Rows[1], grid.SelectedRows[0]);

        form.Close();
    }

    [AvaloniaFact]
    public void The_right_arrow_key_moves_the_current_cell_to_the_next_column()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);

        form.Close();
    }

    [AvaloniaFact]
    public void The_down_arrow_key_moves_the_current_cell_to_the_next_row()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);

        Assert.Same(grid.Rows[1].Cells[0], grid.CurrentCell);

        form.Close();
    }

    [AvaloniaFact]
    public void Home_and_End_move_the_current_cell_to_the_row_s_first_and_last_column()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[1];

        form.Host!.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Same(grid.Rows[0].Cells[0], grid.CurrentCell);

        form.Host!.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);

        form.Close();
    }

    [AvaloniaFact]
    public void F2_begins_editing_the_current_cell_with_a_text_box_showing_its_value()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        form.Host!.KeyPressQwerty(PhysicalKey.F2, RawInputModifiers.None);

        Assert.True(grid.IsCurrentCellInEditMode);
        Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        Assert.Equal("1", ((TextBox)grid.EditingControl!).Text);

        form.Close();
    }

    [AvaloniaFact]
    public void Escape_cancels_an_edit_without_changing_the_cell_s_value()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "changed";

        form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal(1, grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Widening_the_edited_cell_s_column_repositions_the_editing_control_over_it()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var textBox = (TextBox)grid.EditingControl!;

        grid.Columns[0].Width += 40;

        // Height is the TextBox's own PreferredHeight (it is FixedHeight/AutoSize by itself,
        // unrelated to this cell-following fix), so only Location and Width are checked here.
        var expected = grid.GetCellDisplayRectangle(0, 0, cutOverflow: false);
        Assert.Equal(expected.Location, textBox.Location);
        Assert.Equal(expected.Width, textBox.Width);

        form.Close();
    }

    [AvaloniaFact]
    public void Widening_a_column_before_the_edited_cell_moves_the_editing_control_along_with_it()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[1];
        grid.BeginEdit(true);
        var textBox = (TextBox)grid.EditingControl!;
        var before = textBox.Location;

        grid.Columns[0].Width += 40;

        var expected = grid.GetCellDisplayRectangle(1, 0, cutOverflow: false);
        Assert.NotEqual(before, textBox.Location);
        Assert.Equal(expected.Location, textBox.Location);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_an_edit_and_raises_CellValueChanged_with_the_new_value()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "99";
        var changed = new List<object?>();
        grid.CellValueChanged += (_, e) => changed.Add(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value);

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal("99", grid.Rows[0].Cells[0].Value);
        Assert.Equal(["99"], changed);

        form.Close();
    }
}
