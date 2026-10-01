using Avalonia.Headless.XUnit;
using System.ComponentModel;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>Columns, rows, cells, values, styles and selection: the model a DataGridView holds without ever painting.</summary>
public class DataGridViewModelTests
{
    [Fact]
    public void Adding_a_column_gives_it_an_index_and_a_header_text()
    {
        var grid = new System.Windows.Forms.DataGridView();

        var index = grid.Columns.Add("Name", "Full name");

        Assert.Equal(0, index);
        Assert.Equal("Name", grid.Columns[0].Name);
        Assert.Equal("Full name", grid.Columns[0].HeaderText);
        Assert.Equal(0, grid.Columns[0].Index);
    }

    [Fact]
    public void Removing_a_column_reindexes_the_ones_after_it()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Columns.Add("C", "C");

        grid.Columns.RemoveAt(0);

        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("B", grid.Columns[0].Name);
        Assert.Equal(0, grid.Columns[0].Index);
        Assert.Equal("C", grid.Columns[1].Name);
        Assert.Equal(1, grid.Columns[1].Index);
    }

    [Fact]
    public void Removing_a_column_removes_its_cell_from_every_row_and_keeps_values_under_their_headers()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Columns.Add("C", "C");
        grid.Rows.Add("one", "two", "three");

        grid.Columns.RemoveAt(1);

        Assert.Equal(grid.Columns.Count, grid.Rows[0].Cells.Count);
        Assert.Equal("one", grid.Rows[0].Cells[0].Value);
        Assert.Equal("three", grid.Rows[0].Cells[1].Value);
    }

    [Fact]
    public void Removing_a_column_by_name_also_shrinks_every_row_s_Cells()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add("one", "two");

        grid.Columns.Remove("A");

        Assert.Equal(1, grid.Rows[0].Cells.Count);
        Assert.Equal("two", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void Clearing_the_columns_empties_every_row_s_Cells_too()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add("one", "two");

        grid.Columns.Clear();

        Assert.Equal(0, grid.Rows[0].Cells.Count);
    }

    [Fact]
    public void Inserting_a_column_adds_a_cell_to_every_row_at_that_index_without_disturbing_the_others()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("C", "C");
        grid.Rows.Add("one", "three");

        grid.Columns.Insert(1, new DataGridViewTextBoxColumn { Name = "B", HeaderText = "B" });

        Assert.Equal(3, grid.Rows[0].Cells.Count);
        Assert.Equal("one", grid.Rows[0].Cells[0].Value);
        Assert.Null(grid.Rows[0].Cells[1].Value);
        Assert.Equal("three", grid.Rows[0].Cells[2].Value);
    }

    [Fact]
    public void Adding_a_second_column_after_a_row_already_has_a_value_keeps_that_value()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");

        grid.Columns.Add("B", "B");

        Assert.Equal(2, grid.Rows[0].Cells.Count);
        Assert.Equal("one", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void A_column_s_DisplayIndex_can_differ_from_its_Index_once_reordered()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");

        grid.Columns[1].DisplayIndex = 0;

        Assert.Equal(0, grid.Columns[1].DisplayIndex);
        Assert.Equal(1, grid.Columns[0].DisplayIndex);
        // Index itself never changes: only the visual order does.
        Assert.Equal(1, grid.Columns[1].Index);
    }

    [Fact]
    public void Adding_a_row_creates_one_cell_per_column_automatically()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");

        var index = grid.Rows.Add();

        Assert.Equal(2, grid.Rows[index].Cells.Count);
        Assert.Equal(0, grid.Rows[0].Index);
    }

    [Fact]
    public void Adding_a_row_with_values_fills_each_cell_in_column_order()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");

        grid.Rows.Add("one", "two");

        Assert.Equal("one", grid.Rows[0].Cells[0].Value);
        Assert.Equal("two", grid.Rows[0].Cells[1].Value);
    }

    [Fact]
    public void Removing_a_row_reindexes_the_ones_after_it_and_raises_RowsRemoved()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.Rows.Add();
        var removed = new List<(int Index, int Count)>();
        grid.RowsRemoved += (_, e) => removed.Add((e.RowIndex, e.RowCount));

        grid.Rows.RemoveAt(0);

        Assert.Equal(2, grid.Rows.Count);
        Assert.Equal(0, grid.Rows[0].Index);
        Assert.Equal([(0, 1)], removed);
    }

    [Fact]
    public void Setting_a_cell_s_Value_raises_CellValueChanged_with_its_column_and_row()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        var raised = new List<(int Column, int Row)>();
        grid.CellValueChanged += (_, e) => raised.Add((e.ColumnIndex, e.RowIndex));

        grid.Rows[0].Cells[0].Value = 42;

        Assert.Equal([(0, 0)], raised);
        Assert.Equal(42, grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void Setting_a_cell_s_Value_to_the_same_value_does_not_raise_CellValueChanged()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows[0].Cells[0].Value = "same";
        var raisedCount = 0;
        grid.CellValueChanged += (_, _) => raisedCount++;

        grid.Rows[0].Cells[0].Value = "same";

        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public void The_indexers_by_column_index_and_by_column_name_reach_the_same_cell()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("Name", "Name");
        grid.Rows.Add();

        grid[0, 0].Value = "Ada";

        Assert.Equal("Ada", grid["Name", 0].Value);
        Assert.Same(grid[0, 0], grid["Name", 0]);
    }

    [Fact]
    public void Setting_CurrentCell_raises_CurrentCellChanged_and_updates_CurrentRow()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        var raisedCount = 0;
        grid.CurrentCellChanged += (_, _) => raisedCount++;

        grid.CurrentCell = grid.Rows[1].Cells[0];

        Assert.Equal(1, raisedCount);
        Assert.Same(grid.Rows[1], grid.CurrentRow);
        Assert.Equal(new System.Drawing.Point(0, 1), grid.CurrentCellAddress);
    }

    [Fact]
    public void Moving_CurrentCell_raises_CellLeave_on_the_old_cell_and_CellEnter_on_the_new_one()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        var left = new List<int>();
        var entered = new List<int>();
        grid.CellLeave += (_, e) => left.Add(e.RowIndex);
        grid.CellEnter += (_, e) => entered.Add(e.RowIndex);

        grid.CurrentCell = grid.Rows[1].Cells[0];

        Assert.Equal([0], left);
        Assert.Equal([1], entered);
    }

    [Fact]
    public void Removing_the_current_row_moves_CurrentCell_to_the_same_column_in_the_next_row()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add((object)1);
        grid.Rows.Add((object)2);
        grid.Rows.Add((object)3);
        grid.CurrentCell = grid.Rows[1].Cells[0];
        var left = new List<int>();
        var entered = new List<int>();
        var changed = 0;
        grid.CellLeave += (_, e) => left.Add(e.RowIndex);
        grid.CellEnter += (_, e) => entered.Add(e.RowIndex);
        grid.CurrentCellChanged += (_, _) => changed++;

        grid.Rows.RemoveAt(1);

        // Row 2 shifted up into row 1's place, so the current cell moves there.
        Assert.Same(grid.Rows[1], grid.CurrentCell!.OwningRow);
        Assert.Equal(3, grid.CurrentCell.Value);
        Assert.Equal([1], left);
        Assert.Equal([1], entered);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Removing_the_current_row_when_it_was_the_last_moves_CurrentCell_to_the_new_last_row()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add((object)1);
        grid.Rows.Add((object)2);
        grid.CurrentCell = grid.Rows[1].Cells[0];

        grid.Rows.RemoveAt(1);

        Assert.Same(grid.Rows[0], grid.CurrentCell!.OwningRow);
        Assert.Equal(1, grid.CurrentCell.Value);
    }

    [Fact]
    public void Removing_the_only_row_clears_CurrentCell()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.CurrentCell = grid.Rows[0].Cells[0];

        grid.Rows.RemoveAt(0);

        Assert.Null(grid.CurrentCell);
    }

    [Fact]
    public void Removing_a_row_that_is_not_the_current_row_leaves_CurrentCell_untouched()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.Rows.Add();
        var current = grid.Rows[2].Cells[0];
        grid.CurrentCell = current;
        var changed = 0;
        grid.CurrentCellChanged += (_, _) => changed++;

        grid.Rows.RemoveAt(0);

        Assert.Same(current, grid.CurrentCell);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Removing_the_current_cell_s_column_moves_CurrentCell_to_the_next_visible_column()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Columns.Add("C", "C");
        grid.Rows.Add("one", "two", "three");
        grid.CurrentCell = grid.Rows[0].Cells[1];
        var changed = 0;
        grid.CurrentCellChanged += (_, _) => changed++;

        grid.Columns.RemoveAt(1);

        Assert.Equal("three", grid.CurrentCell!.Value);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Removing_the_last_column_while_it_is_current_moves_CurrentCell_to_the_previous_one()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add("one", "two");
        grid.CurrentCell = grid.Rows[0].Cells[1];

        grid.Columns.RemoveAt(1);

        Assert.Equal("one", grid.CurrentCell!.Value);
    }

    [Fact]
    public void Removing_the_only_column_while_it_is_current_clears_CurrentCell()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");
        grid.CurrentCell = grid.Rows[0].Cells[0];

        grid.Columns.RemoveAt(0);

        Assert.Null(grid.CurrentCell);
    }

    [Fact]
    public void CellBeginEdit_cancelling_prevents_the_editing_control_from_appearing()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.CellBeginEdit += (_, e) => e.Cancel = true;

        var began = grid.BeginEdit(true);

        Assert.False(began);
        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Null(grid.EditingControl);
    }

    [Fact]
    public void CellValidating_cancelling_keeps_the_editor_open_and_does_not_commit_the_value()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "changed";
        grid.CellValidating += (_, e) => e.Cancel = true;

        var ended = grid.EndEdit();

        Assert.False(ended);
        Assert.True(grid.IsCurrentCellInEditMode);
        Assert.Equal("one", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void Sort_commits_a_pending_edit_before_reordering_the_rows()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("Text", "Text");
        grid.Rows.Add("bb");
        grid.Rows.Add("c");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl!).Text = "a";

        grid.Sort(grid.Columns[0], ListSortDirection.Ascending);

        Assert.False(grid.IsCurrentCellInEditMode);
        Assert.Equal("a", grid.Rows[0].Cells[0].Value);
        Assert.Equal("c", grid.Rows[1].Cells[0].Value);
    }

    [Fact]
    public void Sort_throws_when_the_pending_edit_cannot_be_committed()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("Text", "Text");
        grid.Rows.Add("bb");
        grid.Rows.Add("c");
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(true);
        grid.CellValidating += (_, e) => e.Cancel = true;

        Assert.Throws<InvalidOperationException>(() => grid.Sort(grid.Columns[0], ListSortDirection.Ascending));
        // The rows must stay in their original order: the sort itself never ran.
        Assert.Equal("bb", grid.Rows[0].Cells[0].Value);
        Assert.Equal("c", grid.Rows[1].Cells[0].Value);
    }

    [Fact]
    public void SelectAll_selects_every_cell_in_CellSelect_mode()
    {
        var grid = new System.Windows.Forms.DataGridView { SelectionMode = DataGridViewSelectionMode.CellSelect };
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add();
        grid.Rows.Add();

        grid.SelectAll();

        Assert.Equal(4, grid.SelectedCells.Count);
        Assert.True(grid.AreAllCellsSelected(true));
    }

    [Fact]
    public void ClearSelection_empties_every_selection_collection_and_raises_SelectionChanged()
    {
        var grid = new System.Windows.Forms.DataGridView { SelectionMode = DataGridViewSelectionMode.CellSelect };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.SelectAll();
        var raised = 0;
        grid.SelectionChanged += (_, _) => raised++;

        grid.ClearSelection();

        Assert.Empty(grid.SelectedCells);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void A_column_s_InheritedAutoSizeMode_falls_back_to_the_grid_s_AutoSizeColumnsMode()
    {
        var grid = new System.Windows.Forms.DataGridView { AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells };
        grid.Columns.Add("A", "A");

        Assert.Equal(DataGridViewAutoSizeColumnMode.AllCells, grid.Columns[0].InheritedAutoSizeMode);

        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.ColumnHeader;

        // A column's own mode wins over the grid's.
        Assert.Equal(DataGridViewAutoSizeColumnMode.ColumnHeader, grid.Columns[0].InheritedAutoSizeMode);
    }

    [Fact]
    public void Sort_orders_rows_by_one_column_s_value()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("Age", "Age");
        // DataGridViewRowCollection also overloads Add(int) ("add this many blank rows"), exactly
        // as Microsoft's does, so a bare int literal must be boxed to reach Add(params object[]).
        grid.Rows.Add((object)30);
        grid.Rows.Add((object)10);
        grid.Rows.Add((object)20);

        grid.Sort(grid.Columns[0], ListSortDirection.Ascending);

        Assert.Equal(10, grid.Rows[0].Cells[0].Value);
        Assert.Equal(20, grid.Rows[1].Cells[0].Value);
        Assert.Equal(30, grid.Rows[2].Cells[0].Value);
        Assert.Same(grid.Columns[0], grid.SortedColumn);
        Assert.Equal(SortOrder.Ascending, grid.SortOrder);
    }

    [Fact]
    public void SortCompare_can_override_the_comparison_used_by_Sort()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("Text", "Text");
        grid.Rows.Add("bb");
        grid.Rows.Add("a");
        grid.SortCompare += (_, e) =>
        {
            // Reverse of the natural order, to prove the handler's result is actually used.
            e.SortResult = -string.Compare((string?)e.CellValue1, (string?)e.CellValue2, StringComparison.Ordinal);
            e.Handled = true;
        };

        grid.Sort(grid.Columns[0], ListSortDirection.Ascending);

        Assert.Equal("bb", grid.Rows[0].Cells[0].Value);
        Assert.Equal("a", grid.Rows[1].Cells[0].Value);
    }

    [AvaloniaFact]
    public void A_row_whose_edit_validation_refuses_cannot_be_removed_and_the_grid_is_untouched()
    {
        var (form, grid) = ShowGridForEdit();
        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.BeginEdit(false);
        grid.CellValidating += (_, e) => e.Cancel = true;

        Assert.Throws<InvalidOperationException>(() => grid.Rows.RemoveAt(1));

        Assert.Equal(3, grid.Rows.Count);
        Assert.Same(grid.Rows[1].Cells[0], grid.CurrentCell);
        Assert.True(grid.IsCurrentCellInEditMode);
        form.Close();
    }

    [AvaloniaFact]
    public void Moving_off_a_cell_whose_edit_validation_refuses_leaves_the_current_cell_where_it_is()
    {
        var (form, grid) = ShowGridForEdit();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.BeginEdit(false);
        grid.CellValidating += (_, e) => e.Cancel = true;

        Assert.Throws<InvalidOperationException>(() => grid.CurrentCell = grid.Rows[2].Cells[0]);

        Assert.Same(grid.Rows[0].Cells[0], grid.CurrentCell);
        Assert.True(grid.IsCurrentCellInEditMode);
        form.Close();
    }

    [Fact]
    public void Clearing_the_rows_clears_CurrentCell_and_raises_RowsRemoved()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");
        grid.Rows.Add("two");
        grid.CurrentCell = grid.Rows[1].Cells[0];
        var removed = 0;
        var currentChanges = 0;
        grid.RowsRemoved += (_, e) => removed += e.RowCount;
        grid.CurrentCellChanged += (_, _) => currentChanges++;

        grid.Rows.Clear();

        Assert.Null(grid.CurrentCell);
        Assert.Equal(2, removed);
        Assert.Equal(1, currentChanges);
    }

    private static (Form Form, System.Windows.Forms.DataGridView Grid) ShowGridForEdit()
    {
        var grid = new System.Windows.Forms.DataGridView { Bounds = new System.Drawing.Rectangle(0, 0, 300, 150) };
        grid.Columns.Add("A", "A");
        grid.Rows.Add("one");
        grid.Rows.Add("two");
        grid.Rows.Add("three");
        var form = new Form { ClientSize = new System.Drawing.Size(320, 170) };
        form.Controls.Add(grid);
        form.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        grid.Focus();

        return (form, grid);
    }
}
