using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

public abstract partial class DataGridViewCell
{
    private AccessibleObject? _accessibilityObject;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AccessibleObject AccessibilityObject => _accessibilityObject ??= CreateAccessibilityInstance();

    protected virtual AccessibleObject CreateAccessibilityInstance() => new CellAccessibleObject(this);

    private sealed class CellAccessibleObject(DataGridViewCell cell) : AccessibleObject
    {
        public override Rectangle Bounds
        {
            get
            {
                var grid = cell.DataGridView;
                if (grid is null) return Rectangle.Empty;

                var rowIndex = cell.RowIndex;
                var columnIndex = cell.ColumnIndex;
                if (cell is DataGridViewColumnHeaderCell) rowIndex = -1;
                if (cell is DataGridViewRowHeaderCell or DataGridViewTopLeftHeaderCell) columnIndex = -1;
                if (rowIndex < -1 || columnIndex < -1) return Rectangle.Empty;

                return grid.RectangleToScreen(grid.GetCellDisplayRectangle(columnIndex, rowIndex, true));
            }
        }

        public override string? Name
        {
            get => cell is DataGridViewColumnHeaderCell { OwningColumn: { } column }
                ? column.HeaderText
                : cell.FormattedValue?.ToString() ?? cell.Value?.ToString();
            set => base.Name = value;
        }

        public override AccessibleObject? Parent => cell.OwningRow?.AccessibilityObject ?? cell.DataGridView?.AccessibilityObject;

        public override AccessibleRole Role => cell switch
        {
            DataGridViewRowHeaderCell => AccessibleRole.RowHeader,
            DataGridViewColumnHeaderCell => AccessibleRole.ColumnHeader,
            DataGridViewButtonCell => AccessibleRole.PushButton,
            DataGridViewCheckBoxCell => AccessibleRole.CheckButton,
            DataGridViewComboBoxCell => AccessibleRole.ComboBox,
            DataGridViewLinkCell => AccessibleRole.Link,
            DataGridViewImageCell => AccessibleRole.Graphic,
            _ => AccessibleRole.Cell,
        };

        public override AccessibleStates State
        {
            get
            {
                var state = AccessibleStates.Focusable | AccessibleStates.Selectable;
                if (!cell.Displayed) state |= AccessibleStates.Offscreen;
                if (cell.Selected) state |= AccessibleStates.Selected;
                if (cell.ReadOnly) state |= AccessibleStates.ReadOnly;
                if (ReferenceEquals(cell.DataGridView?.CurrentCell, cell)) state |= AccessibleStates.Focused;
                if (cell is DataGridViewCheckBoxCell && cell.Value is true or CheckState.Checked)
                    state |= AccessibleStates.Checked;
                return state;
            }
        }

        public override string? Value
        {
            get => cell.FormattedValue?.ToString() ?? cell.Value?.ToString();
            set => cell.Value = value;
        }

        public override void Select(AccessibleSelection flags)
        {
            if (cell.DataGridView is not { } grid || cell.RowIndex < 0 || cell.ColumnIndex < 0) return;
            if (flags.HasFlag(AccessibleSelection.TakeFocus)) grid.CurrentCell = cell;
            if (flags.HasFlag(AccessibleSelection.TakeSelection)) cell.Selected = true;
            if (flags.HasFlag(AccessibleSelection.RemoveSelection)) cell.Selected = false;
        }
    }
}
