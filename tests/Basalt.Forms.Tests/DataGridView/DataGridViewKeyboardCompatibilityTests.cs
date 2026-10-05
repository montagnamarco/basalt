using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewKeyboardCompatibilityTests
{
    [Fact]
    public void Arrow_keys_follow_visible_display_order_and_skip_hidden_rows()
    {
        using var grid = CreateGrid();
        grid.Columns.Add("third", "Third");
        grid.Columns[2].DisplayIndex = 0;
        grid.Columns[0].Visible = false;
        grid.Rows.Add("e", "f", "g");
        grid.Rows[1].Visible = false;
        grid.CurrentCell = grid.Rows[0].Cells[2];
        Assert.True(grid.Key(Keys.Right));
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
        Assert.True(grid.Key(Keys.Down));
        Assert.Same(grid.Rows[2].Cells[1], grid.CurrentCell);
        Assert.True(grid.Key(Keys.Home));
        Assert.Same(grid.Rows[2].Cells[2], grid.CurrentCell);
    }

    [Fact]
    public void Delete_callback_can_remove_rows_without_a_second_removal()
    {
        using var grid = CreateGrid();
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.SelectAll();
        var callbacks = 0;
        grid.UserDeletingRow += (_, _) =>
        {
            callbacks++;
            grid.Rows.Clear();
        };
        Assert.True(grid.Key(Keys.Delete));
        Assert.Empty(grid.Rows.Cast<DataGridViewRow>());
        Assert.Equal(1, callbacks);
    }

    [Fact]
    public void Tab_moves_between_cells_and_respects_standard_tab()
    {
        using var grid = CreateGrid();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        Assert.True(grid.Key(Keys.Tab));
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
        Assert.True(grid.Key(Keys.Tab));
        Assert.Same(grid.Rows[1].Cells[0], grid.CurrentCell);
        Assert.True(grid.Key(Keys.Shift | Keys.Tab));
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
        grid.StandardTab = true;
        Assert.False(grid.Key(Keys.Tab));
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
    }

    [Fact]
    public void Delete_raises_cancellable_row_events_and_preserves_cancelled_rows()
    {
        using var grid = CreateGrid();
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.SelectAll();
        var retained = grid.Rows[0];
        var deleted = 0;
        grid.UserDeletingRow += (_, e) => e.Cancel = ReferenceEquals(e.Row, retained);
        grid.UserDeletedRow += (_, _) => deleted++;
        Assert.True(grid.Key(Keys.Delete));
        Assert.Single(grid.Rows.Cast<DataGridViewRow>());
        Assert.Same(retained, grid.Rows[0]);
        Assert.Equal(1, deleted);
    }

    [Fact]
    public void Keyboard_entry_point_dispatches_virtual_override()
    {
        using var grid = CreateGrid();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        grid.Press(Keys.Right);
        Assert.Equal(1, grid.DispatchCount);
        Assert.Same(grid.Rows[0].Cells[1], grid.CurrentCell);
    }

    [Fact]
    public void Space_dispatches_once_to_an_unhandled_custom_cell_for_each_entry_point()
    {
        using var grid = new KeyboardGrid();
        grid.Columns.Add(new DataGridViewColumn(new CountingKeyCell()));
        grid.Rows.Add();
        var cell = Assert.IsType<CountingKeyCell>(grid.Rows[0].Cells[0]);
        grid.CurrentCell = cell;
        grid.Press(Keys.Space);
        Assert.Equal(1, cell.SpaceCount);
        Assert.False(grid.Key(Keys.Space));
        Assert.Equal(2, cell.SpaceCount);
        grid.Press(Keys.Space);
        Assert.Equal(3, cell.SpaceCount);
    }

    private sealed class CountingKeyCell : DataGridViewTextBoxCell
    {
        public int SpaceCount { get; private set; }
        public override object Clone() => new CountingKeyCell();
        protected override void OnKeyDown(KeyEventArgs e, int rowIndex)
        {
            if (e.KeyCode == Keys.Space) SpaceCount++;
        }
    }

    private static KeyboardGrid CreateGrid()
    {
        var grid = new KeyboardGrid();
        grid.Columns.Add("first", "First");
        grid.Columns.Add("second", "Second");
        grid.Rows.Add("a", "b");
        grid.Rows.Add("c", "d");
        return grid;
    }

    private sealed class KeyboardGrid : System.Windows.Forms.DataGridView
    {
        public int DispatchCount { get; private set; }
        public bool Key(Keys key) => ProcessDataGridViewKey(new KeyEventArgs(key));
        public void Press(Keys key) => OnKeyDown(new KeyEventArgs(key));
        protected override bool ProcessDataGridViewKey(KeyEventArgs e)
        {
            DispatchCount++;
            return base.ProcessDataGridViewKey(e);
        }
    }
}
