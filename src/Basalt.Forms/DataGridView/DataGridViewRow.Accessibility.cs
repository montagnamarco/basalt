using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public partial class DataGridViewRow
{
    private AccessibleObject? _accessibilityObject;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AccessibleObject AccessibilityObject => _accessibilityObject ??= CreateAccessibilityInstance();

    protected virtual AccessibleObject CreateAccessibilityInstance() => new RowAccessibleObject(this);

    private sealed class RowAccessibleObject(DataGridViewRow row) : AccessibleObject
    {
        public override Rectangle Bounds
        {
            get
            {
                if (row.DataGridView is not { } grid || row.Index < 0) return Rectangle.Empty;
                var headerBounds = grid.GetCellDisplayRectangle(-1, row.Index, true);
                return grid.RectangleToScreen(new Rectangle(0, headerBounds.Y, grid.ClientSize.Width, headerBounds.Height));
            }
        }

        public override string? Name
        {
            get => $"Row {row.Index + 1}";
            set => base.Name = value;
        }

        public override AccessibleObject? Parent => row.DataGridView?.AccessibilityObject;

        public override AccessibleRole Role => AccessibleRole.Row;

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Selectable;
                if (!row.Displayed) state |= AccessibleStates.Offscreen;
                if (row.Selected) state |= AccessibleStates.Selected;
                if (row.ReadOnly) state |= AccessibleStates.ReadOnly;
                if (row.DataGridView?.CurrentCell?.OwningRow == row) state |= AccessibleStates.Focused;
                return state;
            }
        }

        public override int GetChildCount() => VisibleCellCount();

        public override AccessibleObject GetChild(int index)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            var grid = row.DataGridView;
            if (grid?.RowHeadersVisible == true)
            {
                if (index == 0) return row.HeaderCell.AccessibilityObject;
                index--;
            }

            if (grid is not null)
            {
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    if (!column.Visible) continue;
                    if (index-- == 0) return row.Cells[column.Index].AccessibilityObject;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }

        public override AccessibleObject GetFocused() => row.DataGridView?.CurrentCell is { } cell && cell.OwningRow == row
            ? cell.AccessibilityObject
            : null!;

        public override AccessibleObject GetSelected()
        {
            if (row.Selected) return this;
            foreach (DataGridViewCell cell in row.Cells)
            {
                if (cell.Selected) return cell.AccessibilityObject;
            }

            return null!;
        }

        public override AccessibleObject HitTest(int x, int y)
        {
            for (var index = 0; index < GetChildCount(); index++)
            {
                var child = GetChild(index);
                if (child.Bounds.Contains(x, y)) return child;
            }

            return null!;
        }

        public override void Select(AccessibleSelection flags)
        {
            if (row.DataGridView is not { } grid || row.Cells.Count == 0) return;
            if (flags.HasFlag(AccessibleSelection.TakeFocus)) grid.CurrentCell = row.Cells[0];
            if (flags.HasFlag(AccessibleSelection.TakeSelection)) row.Selected = true;
            if (flags.HasFlag(AccessibleSelection.RemoveSelection)) row.Selected = false;
        }

        private int VisibleCellCount()
        {
            if (row.DataGridView is not { } grid) return 0;
            var count = grid.RowHeadersVisible ? 1 : 0;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (column.Visible) count++;
            }

            return count;
        }
    }
}
