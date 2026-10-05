using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Tests.DataGridView;

public class DataGridViewAccessibilityCompatibilityTests
{
    [Fact]
    public void Accessibility_tree_tracks_rows_cells_selection_and_screen_bounds()
    {
        using var grid = new ObservingGrid { Size = new Size(220, 110), AllowUserToAddRows = false, ColumnCount = 2 };
        grid.Rows.Add("first", "second");
        grid.Rows.Add("third", "fourth");

        var table = grid.AccessibilityObject;
        var firstRow = grid.Rows[0].AccessibilityObject;
        var secondCell = grid.Rows[0].Cells[1].AccessibilityObject;

        Assert.Equal(AccessibleRole.Table, table.Role);
        Assert.Equal(2, table.GetChildCount());
        Assert.Same(firstRow, table.GetChild(0));
        Assert.Same(table, firstRow.Parent);
        Assert.Equal(AccessibleRole.Row, firstRow.Role);
        Assert.Same(secondCell, firstRow.GetChild(2)); // row header precedes two data cells
        Assert.Same(firstRow, secondCell.Parent);
        Assert.Equal("second", secondCell.Value);
        Assert.Equal(AccessibleRole.Cell, secondCell.Role);
        Assert.Equal(grid.RectangleToScreen(grid.GetCellDisplayRectangle(1, 0, true)), secondCell.Bounds);
        Assert.Same(firstRow, grid.AccessibleById(1));

        grid.CurrentCell = grid.Rows[0].Cells[1];
        Assert.Same(secondCell, table.GetFocused());
        Assert.True(secondCell.State.HasFlag(AccessibleStates.Focused));
        Assert.Equal(new Point(1, 0), grid.LastAccessibilityNotification);

        secondCell.Select(AccessibleSelection.TakeSelection);
        Assert.True(secondCell.State.HasFlag(AccessibleStates.Selected));
        Assert.Same(secondCell, table.GetSelected());
        Assert.Same(secondCell, table.HitTest(secondCell.Bounds.X + 3, secondCell.Bounds.Y + 3));
    }

    [Fact]
    public void Hidden_rows_and_columns_are_excluded_from_children()
    {
        using var grid = new ObservingGrid { AllowUserToAddRows = false, ColumnCount = 2 };
        grid.Rows.Add("one", "two");
        grid.Rows.Add("three", "four");
        grid.Rows[0].Visible = false;
        grid.Columns[1].Visible = false;

        Assert.Equal(1, grid.AccessibilityObject.GetChildCount());
        Assert.Same(grid.Rows[1].AccessibilityObject, grid.AccessibilityObject.GetChild(0));
        Assert.Equal(2, grid.Rows[1].AccessibilityObject.GetChildCount()); // row header plus visible column
    }

    [Fact]
    public void Specialized_cells_expose_their_actions_and_checked_state()
    {
        var button = new DataGridViewButtonCell();
        var checkBox = new DataGridViewCheckBoxCell { Value = true };
        var link = new DataGridViewLinkCell();

        Assert.Equal(AccessibleRole.PushButton, button.AccessibilityObject.Role);
        Assert.Equal(AccessibleRole.CheckButton, checkBox.AccessibilityObject.Role);
        Assert.True(checkBox.AccessibilityObject.State.HasFlag(AccessibleStates.Checked));
        Assert.Equal(AccessibleRole.Link, link.AccessibilityObject.Role);
    }

    private sealed class ObservingGrid : System.Windows.Forms.DataGridView
    {
        public Point LastAccessibilityNotification { get; private set; } = new(-1, -1);

        public AccessibleObject? AccessibleById(int objectId) => GetAccessibilityObjectById(objectId);

        protected override void AccessibilityNotifyCurrentCellChanged(Point currentCellAddress)
        {
            LastAccessibilityNotification = currentCellAddress;
            base.AccessibilityNotifyCurrentCellChanged(currentCellAddress);
        }
    }
}
