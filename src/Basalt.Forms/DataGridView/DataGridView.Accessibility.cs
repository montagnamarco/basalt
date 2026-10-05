using System.Drawing;

namespace System.Windows.Forms;

public partial class DataGridView
{
    protected override AccessibleObject CreateAccessibilityInstance() => new GridAccessibleObject(this);

    protected override AccessibleObject? GetAccessibilityObjectById(int objectId)
    {
        if (objectId is 0 or -4) return AccessibilityObject;
        if (objectId <= 0) return base.GetAccessibilityObjectById(objectId);

        var rowIndex = objectId - 1;
        return rowIndex < Rows.Count ? Rows[rowIndex].AccessibilityObject : null;
    }

    /// <summary>Reports the newly focused cell after selection changes, using the grid's accessibility tree.</summary>
    protected virtual void AccessibilityNotifyCurrentCellChanged(Point currentCellAddress)
    {
        if (currentCellAddress.X < 0 || currentCellAddress.Y < 0) return;

        // Control's platform bridge owns client notifications; the tree itself reads CurrentCell
        // live, so accessibility queries made after this callback see the new focused cell.
        AccessibilityNotifyClients(AccessibleEvents.Focus, -4, currentCellAddress.Y + 1);
        AccessibilityNotifyClients(AccessibleEvents.Selection, -4, currentCellAddress.Y + 1);
    }

    private sealed class GridAccessibleObject(DataGridView grid) : ControlAccessibleObject(grid)
    {
        public override AccessibleRole Role => AccessibleRole.Table;

        public override int GetChildCount()
        {
            var count = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Visible) count++;
            }

            return count;
        }

        public override AccessibleObject GetChild(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.Visible) continue;
                if (index-- == 0) return row.AccessibilityObject;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public override AccessibleObject GetFocused() => grid.CurrentCell?.AccessibilityObject ?? null!;

        public override AccessibleObject GetSelected()
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.Selected) return row.AccessibilityObject;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    if (cell.Selected) return cell.AccessibilityObject;
                }
            }

            return null!;
        }

        public override AccessibleObject HitTest(int x, int y)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!row.Visible || !row.AccessibilityObject.Bounds.Contains(x, y)) continue;
                return row.AccessibilityObject.HitTest(x, y) ?? row.AccessibilityObject;
            }

            return null!;
        }

        public override AccessibleObject Navigate(AccessibleNavigation navdir) => navdir switch
        {
            AccessibleNavigation.FirstChild when GetChildCount() > 0 => GetChild(0),
            AccessibleNavigation.LastChild when GetChildCount() > 0 => GetChild(GetChildCount() - 1),
            _ => base.Navigate(navdir),
        };
    }
}
