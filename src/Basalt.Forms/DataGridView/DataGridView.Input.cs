using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Mouse and keyboard: a click focuses the grid and selects per SelectionMode, the arrow keys and
/// Home/End/PageUp/PageDown/Ctrl+Home move the current cell, F2 or typing a character begins
/// editing (per EditMode), and Enter/Escape commit or cancel it. Tab navigation between cells and
/// column-resize dragging are left out this round.
/// </summary>
public partial class DataGridView
{
    /// <summary>Claims the keys a grid moves its current cell with, so dialog/tab navigation does not take them first.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        var code = keyData & Keys.KeyCode;

        return code switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown => true,
            Keys.Enter or Keys.Escape or Keys.F2 => true,
            _ => base.IsInputKey(keyData),
        };
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (CanSelect) Focus();
        if (e.Button != MouseButtons.Left) return;

        var hit = HitTest(e.X, e.Y);
        var shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
        var control = (ModifierKeys & Keys.Control) == Keys.Control;

        switch (hit.Type)
        {
            case DataGridViewHitTestType.Cell:
                SetCurrentCellAddressCore(hit.ColumnIndex, hit.RowIndex, true, true, true);
                SelectCell(hit.ColumnIndex, hit.RowIndex, shift, control);
                OnCellMouseDown(new DataGridViewCellMouseEventArgs(hit.ColumnIndex, hit.RowIndex, e.X - hit.ColumnX, e.Y - hit.RowY, e));
                OnCellClick(new DataGridViewCellEventArgs(hit.ColumnIndex, hit.RowIndex));
                OnCellContentClick(new DataGridViewCellEventArgs(hit.ColumnIndex, hit.RowIndex));
                break;

            case DataGridViewHitTestType.RowHeader:
                SelectWholeRow(hit.RowIndex, shift, control);
                if (Columns.Count > 0) SetCurrentCellAddressCore(CurrentCellAddress.X >= 0 ? CurrentCellAddress.X : 0, hit.RowIndex, true, true, true);
                break;

            case DataGridViewHitTestType.ColumnHeader:
                SelectWholeColumn(hit.ColumnIndex, shift, control);
                break;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left) return;

        var hit = HitTest(e.X, e.Y);
        if (hit.Type == DataGridViewHitTestType.Cell)
        {
            OnCellMouseUp(new DataGridViewCellMouseEventArgs(hit.ColumnIndex, hit.RowIndex, e.X - hit.ColumnX, e.Y - hit.RowY, e));
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        switch (e.KeyCode)
        {
            case Keys.Up:
                MoveCurrentCell(0, -1, e.Shift);
                break;

            case Keys.Down:
                MoveCurrentCell(0, 1, e.Shift);
                break;

            case Keys.Left:
                MoveCurrentCell(-1, 0, e.Shift);
                break;

            case Keys.Right:
                MoveCurrentCell(1, 0, e.Shift);
                break;

            case Keys.Home when e.Control:
                MoveCurrentCellTo(0, 0, e.Shift);
                break;

            case Keys.Home:
                MoveCurrentCellTo(0, CurrentCellAddress.Y, e.Shift);
                break;

            case Keys.End:
                MoveCurrentCellTo(Columns.Count - 1, CurrentCellAddress.Y, e.Shift);
                break;

            case Keys.PageUp:
                MoveCurrentCell(0, -10, e.Shift);
                break;

            case Keys.PageDown:
                MoveCurrentCell(0, 10, e.Shift);
                break;

            case Keys.F2:
                BeginEdit(selectAll: false);
                break;

            case Keys.Enter:
                if (IsCurrentCellInEditMode) EndEdit();
                else MoveCurrentCell(0, 1, extend: false);
                break;

            case Keys.Escape:
                if (IsCurrentCellInEditMode) CancelEdit();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (e.Handled) return;
        if (EditMode == DataGridViewEditMode.EditProgrammatically || EditMode == DataGridViewEditMode.EditOnEnter) return;
        if (_currentCell is not { } cell || IsCurrentCellInEditMode) return;
        if (char.IsControl(e.KeyChar)) return;

        if (BeginEditCore(selectAll: true) && EditingControl is TextBox textBox)
        {
            textBox.Text = e.KeyChar.ToString();
            textBox.SelectionStart = 1;
        }

        e.Handled = true;
    }

    /// <summary>Row-header click: selects the row, forced regardless of SelectionMode, as Microsoft's own row header does.</summary>
    private void SelectWholeRow(int rowIndex, bool extend, bool toggle)
    {
        var replacing = !extend && !toggle;
        if (replacing) ClearSelectionQuiet();

        ApplyRangeOrToggle(_selectedRowIndexes, rowIndex, extend, toggle, AnchorRow);
        AnchorRow = rowIndex;

        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Column-header click: selects the column, and sorts it first when SortMode is Automatic.</summary>
    private void SelectWholeColumn(int columnIndex, bool extend, bool toggle)
    {
        if (columnIndex < 0) return;

        var column = Columns[columnIndex];
        if (column.SortMode == DataGridViewColumnSortMode.Automatic)
        {
            var direction = SortedColumn == column && SortOrder == SortOrder.Ascending
                ? ComponentModel.ListSortDirection.Descending
                : ComponentModel.ListSortDirection.Ascending;

            Sort(column, direction);
            column.HeaderCell.SortGlyphDirection = direction == ComponentModel.ListSortDirection.Ascending ? SortOrder.Ascending : SortOrder.Descending;
        }

        var replacing = !extend && !toggle;
        if (replacing) ClearSelectionQuiet();

        ApplyRangeOrToggle(_selectedColumnIndexes, columnIndex, extend, toggle, AnchorColumn);
        AnchorColumn = columnIndex;

        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    private void MoveCurrentCell(int deltaColumns, int deltaRows, bool extend)
    {
        var x = CurrentCellAddress.X < 0 ? 0 : Math.Clamp(CurrentCellAddress.X + deltaColumns, 0, Math.Max(Columns.Count - 1, 0));
        var y = CurrentCellAddress.Y < 0 ? 0 : Math.Clamp(CurrentCellAddress.Y + deltaRows, 0, Math.Max(Rows.Count - 1, 0));

        MoveCurrentCellTo(x, y, extend);
    }

    private void MoveCurrentCellTo(int columnIndex, int rowIndex, bool extend)
    {
        if (!SetCurrentCellAddressCore(columnIndex, rowIndex, true, true, false)) return;

        SelectCell(columnIndex, rowIndex, extend, toggle: false);
    }
}
