using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// The IDataGridViewEditingControl pipeline: a DataGridViewTextBoxEditingControl (not a plain
/// TextBox) edits a text cell, the very same instance is reused for the next cell edited with it,
/// and EditingControlShowing is raised with it before it becomes visible.
/// </summary>
public class DataGridViewEditingTests
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

    [AvaloniaFact]
    public void BeginEdit_on_a_text_cell_shows_a_DataGridViewTextBoxEditingControl_with_the_cell_s_value()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        grid.BeginEdit(true);

        var editingControl = Assert.IsType<DataGridViewTextBoxEditingControl>(grid.EditingControl);
        Assert.Equal("1", editingControl.Text);
        Assert.Same(grid, editingControl.EditingControlDataGridView);

        form.Close();
    }

    [AvaloniaFact]
    public void The_same_editing_control_instance_is_reused_for_the_next_text_cell_edited()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        var firstControl = grid.EditingControl;
        grid.EndEdit();

        grid.CurrentCell = grid.Rows[0].Cells[1];
        grid.BeginEdit(true);

        Assert.Same(firstControl, grid.EditingControl);

        form.Close();
    }

    [AvaloniaFact]
    public void EditingControlShowing_is_raised_with_the_control_and_a_changed_CellStyle_still_reaches_it()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];

        Control? shownControl = null;
        var customBackColor = Color.FromArgb(10, 20, 30);
        grid.EditingControlShowing += (_, e) =>
        {
            shownControl = e.Control;
            e.CellStyle.BackColor = customBackColor;
        };

        grid.BeginEdit(true);

        Assert.Same(grid.EditingControl, shownControl);
        Assert.Equal(customBackColor, grid.EditingControl!.BackColor);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_in_the_editing_control_raises_CurrentCellDirtyStateChanged_through_NotifyCurrentCellDirty()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);

        var dirtyRaised = false;
        grid.CurrentCellDirtyStateChanged += (_, _) => dirtyRaised = true;
        ((TextBox)grid.EditingControl!).Text = "changed";

        Assert.True(dirtyRaised);
        Assert.True(grid.IsCurrentCellDirty);

        form.Close();
    }

    [AvaloniaFact]
    public void Enter_commits_through_GetEditingControlFormattedValue_and_ends_edit_mode()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "42";

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);

        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal("42", grid.Rows[0].Cells[0].Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Opening_a_cell_with_a_value_for_editing_does_not_make_it_dirty()
    {
        var grid = TwoByTwoGrid();
        var form = ShowWith(grid);
        grid.CurrentCell = grid.Rows[0].Cells[0];
        var dirtyChanges = 0;
        grid.CurrentCellDirtyStateChanged += (_, _) => dirtyChanges++;

        grid.BeginEdit(true);

        Assert.Equal(0, dirtyChanges);
        Assert.False(grid.IsCurrentCellDirty);

        form.Close();
    }
}
