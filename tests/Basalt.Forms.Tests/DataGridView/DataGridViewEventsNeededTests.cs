using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

/// <summary>
/// The ContextMenuStrip/ErrorText/ToolTipText/style/state *Changed and *Needed events this round
/// adds: CellContextMenuStripNeeded/Changed, RowContextMenuStripNeeded/Changed,
/// ColumnContextMenuStripChanged, CellErrorTextChanged, RowErrorTextNeeded/Changed,
/// CellToolTipTextNeeded/Changed, CellStateChanged/ColumnStateChanged/RowStateChanged,
/// CellStyleChanged/CellStyleContentChanged, CellValueNeeded/CellValuePushed, RowEnter/RowLeave/
/// RowValidated, AutoSizeColumnModeChanged, ColumnDisplayIndexChanged and ColumnDividerWidthChanged.
/// </summary>
public class DataGridViewEventsNeededTests
{
    private static System.Windows.Forms.DataGridView OneByOneGrid()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();

        return grid;
    }

    [Fact]
    public void A_cell_with_no_ContextMenuStrip_of_its_own_asks_CellContextMenuStripNeeded()
    {
        var grid = OneByOneGrid();
        var strip = new ContextMenuStrip();
        grid.CellContextMenuStripNeeded += (_, e) => e.ContextMenuStrip = strip;

        Assert.Same(strip, grid.Rows[0].Cells[0].ContextMenuStrip);
    }

    [Fact]
    public void Setting_a_cell_s_ContextMenuStrip_directly_skips_CellContextMenuStripNeeded_and_raises_Changed()
    {
        var grid = OneByOneGrid();
        var needed = false;
        grid.CellContextMenuStripNeeded += (_, _) => needed = true;
        var changedCount = 0;
        grid.CellContextMenuStripChanged += (_, _) => changedCount++;

        var own = new ContextMenuStrip();
        grid.Rows[0].Cells[0].ContextMenuStrip = own;

        Assert.Same(own, grid.Rows[0].Cells[0].ContextMenuStrip);
        Assert.False(needed);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void A_row_with_no_ContextMenuStrip_of_its_own_asks_RowContextMenuStripNeeded()
    {
        var grid = OneByOneGrid();
        var strip = new ContextMenuStrip();
        grid.RowContextMenuStripNeeded += (_, e) => e.ContextMenuStrip = strip;

        Assert.Same(strip, grid.Rows[0].GetContextMenuStrip(0));
    }

    [Fact]
    public void Setting_a_row_s_ContextMenuStrip_directly_raises_RowContextMenuStripChanged()
    {
        var grid = OneByOneGrid();
        var changedCount = 0;
        grid.RowContextMenuStripChanged += (_, _) => changedCount++;

        grid.Rows[0].ContextMenuStrip = new ContextMenuStrip();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Setting_a_column_s_ContextMenuStrip_raises_ColumnContextMenuStripChanged()
    {
        var grid = OneByOneGrid();
        var changedCount = 0;
        grid.ColumnContextMenuStripChanged += (_, _) => changedCount++;

        grid.Columns[0].ContextMenuStrip = new ContextMenuStrip();

        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void A_cell_s_GetInheritedContextMenuStrip_falls_back_from_cell_to_row_to_column()
    {
        var grid = OneByOneGrid();
        var columnStrip = new ContextMenuStrip();
        grid.Columns[0].ContextMenuStrip = columnStrip;

        Assert.Same(columnStrip, grid.Rows[0].Cells[0].GetInheritedContextMenuStrip(0));

        var rowStrip = new ContextMenuStrip();
        grid.Rows[0].ContextMenuStrip = rowStrip;
        Assert.Same(rowStrip, grid.Rows[0].Cells[0].GetInheritedContextMenuStrip(0));

        var cellStrip = new ContextMenuStrip();
        grid.Rows[0].Cells[0].ContextMenuStrip = cellStrip;
        Assert.Same(cellStrip, grid.Rows[0].Cells[0].GetInheritedContextMenuStrip(0));
    }

    [Fact]
    public void Changing_a_cell_s_ErrorText_raises_CellErrorTextChanged_once()
    {
        var grid = OneByOneGrid();
        var count = 0;
        grid.CellErrorTextChanged += (_, _) => count++;

        grid.Rows[0].Cells[0].ErrorText = "bad";
        grid.Rows[0].Cells[0].ErrorText = "bad";

        Assert.Equal(1, count);
    }

    [Fact]
    public void Changing_a_row_s_ErrorText_raises_RowErrorTextChanged_once()
    {
        var grid = OneByOneGrid();
        var count = 0;
        grid.RowErrorTextChanged += (_, _) => count++;

        grid.Rows[0].ErrorText = "bad row";
        grid.Rows[0].ErrorText = "bad row";

        Assert.Equal(1, count);
        Assert.Equal("bad row", grid.Rows[0].ErrorText);
    }

    [Fact]
    public void RowErrorTextNeeded_is_only_asked_once_VirtualMode_is_on()
    {
        var grid = OneByOneGrid();
        grid.Rows[0].ErrorText = "stored";
        var asked = false;
        grid.RowErrorTextNeeded += (_, e) =>
        {
            asked = true;
            e.ErrorText = "from host";
        };

        Assert.Equal("stored", grid.Rows[0].ErrorText);
        Assert.False(asked);

        grid.VirtualMode = true;
        Assert.Equal("from host", grid.Rows[0].ErrorText);
        Assert.True(asked);
    }

    [Fact]
    public void A_cell_with_no_ToolTipText_of_its_own_asks_CellToolTipTextNeeded()
    {
        var grid = OneByOneGrid();
        grid.CellToolTipTextNeeded += (_, e) => e.ToolTipText = "from host";

        Assert.Equal("from host", grid.Rows[0].Cells[0].ToolTipText);
    }

    [Fact]
    public void Setting_a_cell_s_ToolTipText_raises_CellToolTipTextChanged_and_skips_the_Needed_event()
    {
        var grid = OneByOneGrid();
        var needed = false;
        grid.CellToolTipTextNeeded += (_, _) => needed = true;
        var changedCount = 0;
        grid.CellToolTipTextChanged += (_, _) => changedCount++;

        grid.Rows[0].Cells[0].ToolTipText = "mine";

        Assert.Equal("mine", grid.Rows[0].Cells[0].ToolTipText);
        Assert.False(needed);
        Assert.Equal(1, changedCount);
    }

    [Fact]
    public void Selecting_a_cell_raises_CellStateChanged_with_the_Selected_flag()
    {
        var grid = OneByOneGrid();
        DataGridViewElementStates? seenState = null;
        grid.CellStateChanged += (_, e) => seenState = e.StateChanged;

        grid.Rows[0].Cells[0].Selected = true;

        Assert.Equal(DataGridViewElementStates.Selected, seenState);
    }

    [Fact]
    public void Hiding_a_column_raises_ColumnStateChanged_with_the_Visible_flag()
    {
        var grid = OneByOneGrid();
        DataGridViewElementStates? seenState = null;
        grid.ColumnStateChanged += (_, e) => seenState = e.StateChanged;

        grid.Columns[0].Visible = false;

        Assert.Equal(DataGridViewElementStates.Visible, seenState);
    }

    [Fact]
    public void Freezing_a_row_raises_RowStateChanged_with_the_Frozen_flag()
    {
        var grid = OneByOneGrid();
        DataGridViewElementStates? seenState = null;
        grid.RowStateChanged += (_, e) => seenState = e.StateChanged;

        grid.Rows[0].Frozen = true;

        Assert.Equal(DataGridViewElementStates.Frozen, seenState);
    }

    [Fact]
    public void Replacing_a_cell_s_Style_raises_CellStyleChanged_and_CellStyleContentChanged_with_the_Cell_scope()
    {
        var grid = OneByOneGrid();
        var styleChanged = 0;
        grid.CellStyleChanged += (_, _) => styleChanged++;
        DataGridViewCellStyleScopes? scope = null;
        grid.CellStyleContentChanged += (_, e) => scope = e.CellStyleScope;

        grid.Rows[0].Cells[0].Style = new DataGridViewCellStyle();

        Assert.Equal(1, styleChanged);
        Assert.Equal(DataGridViewCellStyleScopes.Cell, scope);
    }

    [Fact]
    public void Replacing_the_grid_s_DefaultCellStyle_raises_CellStyleContentChanged_with_the_DataGridView_scope()
    {
        var grid = OneByOneGrid();
        DataGridViewCellStyleScopes? scope = null;
        grid.CellStyleContentChanged += (_, e) => scope = e.CellStyleScope;

        grid.DefaultCellStyle = new DataGridViewCellStyle();

        Assert.Equal(DataGridViewCellStyleScopes.DataGridView, scope);
    }

    [Fact]
    public void In_VirtualMode_reading_a_cell_s_Value_asks_CellValueNeeded_instead_of_the_cell_s_own_field()
    {
        var grid = new System.Windows.Forms.DataGridView { VirtualMode = true };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.CellValueNeeded += (_, e) => e.Value = "from host";

        Assert.Equal("from host", grid.Rows[0].Cells[0].Value);
    }

    [Fact]
    public void In_VirtualMode_writing_a_cell_s_Value_raises_CellValuePushed_instead_of_storing_it_on_the_cell()
    {
        var grid = new System.Windows.Forms.DataGridView { VirtualMode = true };
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        object? pushed = null;
        grid.CellValuePushed += (_, e) => pushed = e.Value;
        grid.CellValueNeeded += (_, e) => e.Value = null;

        grid.Rows[0].Cells[0].Value = "written";

        Assert.Equal("written", pushed);
    }

    [Fact]
    public void Moving_CurrentCell_to_a_different_row_raises_RowLeave_then_RowEnter_but_not_for_a_move_within_the_same_row()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Columns.Add("B", "B");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.CurrentCell = grid.Rows[0].Cells[0];

        var rowEnterCount = 0;
        var rowLeaveCount = 0;
        grid.RowEnter += (_, _) => rowEnterCount++;
        grid.RowLeave += (_, _) => rowLeaveCount++;

        grid.CurrentCell = grid.Rows[0].Cells[1];
        Assert.Equal(0, rowEnterCount);
        Assert.Equal(0, rowLeaveCount);

        grid.CurrentCell = grid.Rows[1].Cells[0];
        Assert.Equal(1, rowEnterCount);
        Assert.Equal(1, rowLeaveCount);
    }

    [Fact]
    public void Moving_CurrentCell_to_a_different_row_raises_RowValidated_for_the_row_being_left()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.CurrentCell = grid.Rows[0].Cells[0];

        int? validatedRowIndex = null;
        grid.RowValidated += (_, e) => validatedRowIndex = e.RowIndex;

        grid.CurrentCell = grid.Rows[1].Cells[0];

        Assert.Equal(0, validatedRowIndex);
    }

    [Fact]
    public void A_cancelled_RowValidating_keeps_CurrentCell_on_the_row_being_left()
    {
        var grid = new System.Windows.Forms.DataGridView();
        grid.Columns.Add("A", "A");
        grid.Rows.Add();
        grid.Rows.Add();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.RowValidating += (_, e) => e.Cancel = true;

        // Same refusal the setter already gives for a cancelled CellValidating: cancelling the move is reported as a failed commit, not a silent no-op.
        Assert.Throws<InvalidOperationException>(() => grid.CurrentCell = grid.Rows[1].Cells[0]);
        Assert.Same(grid.Rows[0].Cells[0], grid.CurrentCell);
    }

    [Fact]
    public void Setting_a_column_s_AutoSizeMode_raises_AutoSizeColumnModeChanged_with_the_previous_mode()
    {
        var grid = OneByOneGrid();
        DataGridViewAutoSizeColumnMode? previous = null;
        grid.AutoSizeColumnModeChanged += (_, e) => previous = e.PreviousMode;

        grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.AllCells;

        Assert.Equal(DataGridViewAutoSizeColumnMode.NotSet, previous);
    }

    [Fact]
    public void Reordering_a_column_s_DisplayIndex_raises_ColumnDisplayIndexChanged()
    {
        var grid = OneByOneGrid();
        grid.Columns.Add("B", "B");
        var count = 0;
        grid.ColumnDisplayIndexChanged += (_, _) => count++;

        grid.Columns[0].DisplayIndex = 1;

        Assert.True(count > 0);
    }

    [Fact]
    public void Changing_a_column_s_DividerWidth_raises_ColumnDividerWidthChanged()
    {
        var grid = OneByOneGrid();
        var count = 0;
        grid.ColumnDividerWidthChanged += (_, _) => count++;

        grid.Columns[0].DividerWidth = 4;

        Assert.Equal(1, count);
    }
}
