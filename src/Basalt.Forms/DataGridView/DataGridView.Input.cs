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
    /// <summary>The cell a Left-button press last went down on, so mouse-up only raises a click when it lands back on that same cell.</summary>
    private DataGridViewCell? _mouseDownCell;

    /// <summary>The press that started this click was the second of a double click (the count comes with the press, not the release).</summary>
    private bool _mouseDownWasDoubleClick;

    /// <summary>The cell currently under the pointer, so entering/leaving it is reported exactly once per crossing.</summary>
    private DataGridViewCell? _hoverCell;

    /// <summary>The column/row index _hoverCell was last seen at, needed by CellMouseLeave once the pointer has already moved off it.</summary>
    private int _hoverColumnIndex = -1;

    private int _hoverRowIndex = -1;

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

        if (e.Clicks >= 2)
        {
            HandleColumnDividerDoubleClick(e.X, e.Y);
            HandleRowDividerDoubleClick(e.X, e.Y);
        }

        var hit = HitTest(e.X, e.Y);
        var shift = (ModifierKeys & Keys.Shift) == Keys.Shift;
        var control = (ModifierKeys & Keys.Control) == Keys.Control;

        _mouseDownCell = CellAt(hit);
        _mouseDownWasDoubleClick = e.Clicks >= 2;

        switch (hit.Type)
        {
            case DataGridViewHitTestType.Cell:
                SetCurrentCellAddressCore(hit.ColumnIndex, hit.RowIndex, true, true, true);
                SelectCell(hit.ColumnIndex, hit.RowIndex, shift, control);
                break;

            case DataGridViewHitTestType.RowHeader:
                SelectWholeRow(hit.RowIndex, shift, control);
                if (Columns.Count > 0) SetCurrentCellAddressCore(CurrentCellAddress.X >= 0 ? CurrentCellAddress.X : 0, hit.RowIndex, true, true, true);
                break;

            case DataGridViewHitTestType.ColumnHeader:
                SelectWholeColumn(hit.ColumnIndex, shift, control);
                break;
        }

        // Every hit cell (data cell or header) gets its own virtual; only a data cell also raises
        // the public CellMouseDown, matching what the grid raised before this fix.
        if (_mouseDownCell is { } cell)
        {
            var args = new DataGridViewCellMouseEventArgs(hit.ColumnIndex, hit.RowIndex, e.X - hit.ColumnX, e.Y - hit.RowY, e);
            cell.OnMouseDownCore(args);
            if (hit.Type == DataGridViewHitTestType.Cell) OnCellMouseDown(args);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left)
        {
            _mouseDownCell = null;
            return;
        }

        var hit = HitTest(e.X, e.Y);
        var cell = CellAt(hit);

        if (cell is not null)
        {
            var args = new DataGridViewCellMouseEventArgs(hit.ColumnIndex, hit.RowIndex, e.X - hit.ColumnX, e.Y - hit.RowY, e);
            cell.OnMouseUpCore(args);
            if (hit.Type == DataGridViewHitTestType.Cell) OnCellMouseUp(args);

            // CellMouseClick (and the column/row header equivalents) and CellClick/CellContentClick
            // all belong here, on mouse-up, and only when the button comes back up over the very
            // cell/header it went down on - never on mouse-down (that fired CellMouseDown immediately,
            // before the user had even released the button) and never when a drag carried the
            // release onto a different cell.
            if (ReferenceEquals(cell, _mouseDownCell))
            {
                var isDoubleClick = args.Clicks >= 2 || _mouseDownWasDoubleClick;
                RaiseMouseClickCore(hit, args, isDoubleClick);
                if (hit.Type == DataGridViewHitTestType.Cell) RaiseClick(cell, hit, args, e);
            }
        }

        // A drag that started on one cell (pressed state) and let go over another, or over no
        // cell at all, still owes the original cell its own mouse-up: otherwise a button or link
        // cell that only resets "pressed" there would be stuck looking pressed forever.
        if (_mouseDownCell is { } downCell && !ReferenceEquals(downCell, cell))
        {
            downCell.OnMouseUpCore(new DataGridViewCellMouseEventArgs(downCell.ColumnIndex, downCell.RowIndex, -1, -1, e));
        }

        _mouseDownCell = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var hit = HitTest(e.X, e.Y);
        var cell = CellAt(hit);

        if (!ReferenceEquals(cell, _hoverCell))
        {
            if (_hoverCell is not null)
            {
                _hoverCell.OnMouseLeaveCore(_hoverCell.RowIndex);
                RaiseCellMouseLeaveCore(_hoverColumnIndex, _hoverRowIndex);
            }

            _hoverCell = cell;
            _hoverColumnIndex = hit.ColumnIndex;
            _hoverRowIndex = hit.RowIndex;

            if (_hoverCell is not null)
            {
                _hoverCell.OnMouseEnterCore(_hoverCell.RowIndex);
                RaiseCellMouseEnterCore(hit.ColumnIndex, hit.RowIndex);
            }
        }

        if (cell is not null)
        {
            var args = new DataGridViewCellMouseEventArgs(hit.ColumnIndex, hit.RowIndex, e.X - hit.ColumnX, e.Y - hit.RowY, e);
            cell.OnMouseMoveCore(args);
            RaiseCellMouseMoveCore(args);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hoverCell is not null)
        {
            _hoverCell.OnMouseLeaveCore(_hoverCell.RowIndex);
            RaiseCellMouseLeaveCore(_hoverColumnIndex, _hoverRowIndex);
        }

        _hoverCell = null;
        _hoverColumnIndex = -1;
        _hoverRowIndex = -1;
    }

    /// <summary>
    /// Raises Click/ContentClick (or, on the second click of a double-click, DoubleClick/
    /// ContentDoubleClick) for the cell the mouse just came back up on: the cell's own virtual
    /// first, then the grid's public event, then - only when the release point is still inside
    /// GetContentBounds - the content-click pair the same way.
    /// </summary>
    private void RaiseClick(DataGridViewCell cell, HitTestInfo hit, DataGridViewCellMouseEventArgs mouseArgs, MouseEventArgs e)
    {
        var cellArgs = new DataGridViewCellEventArgs(hit.ColumnIndex, hit.RowIndex);
        var isDoubleClick = mouseArgs.Clicks >= 2 || _mouseDownWasDoubleClick;

        // As WinForms: every release is a click (the second one of a double click too), and a
        // double click adds the double-click events after it - so a check box toggles twice.
        cell.OnClickCore(cellArgs);
        OnCellClick(cellArgs);

        var onContent = cell.GetContentBounds(hit.RowIndex).Contains(e.X, e.Y);
        if (onContent)
        {
            cell.OnContentClickCore(cellArgs);
            OnCellContentClick(cellArgs);
        }

        if (!isDoubleClick) return;

        cell.OnDoubleClickCore(cellArgs);
        OnCellDoubleClick(cellArgs);

        if (onContent)
        {
            cell.OnContentDoubleClickCore(cellArgs);
            OnCellContentDoubleClick(cellArgs);
        }
    }

    /// <summary>The cell (data cell, row header or column header) a hit-test result points to, or null away from all of them.</summary>
    private DataGridViewCell? CellAt(HitTestInfo hit) => hit.Type switch
    {
        DataGridViewHitTestType.Cell => Rows[hit.RowIndex].Cells[hit.ColumnIndex],
        DataGridViewHitTestType.RowHeader => Rows[hit.RowIndex].HeaderCell,
        DataGridViewHitTestType.ColumnHeader => Columns[hit.ColumnIndex].HeaderCell,
        _ => null,
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        _currentCell?.OnKeyDownCore(e, _currentCell.RowIndex);
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

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled) return;

        _currentCell?.OnKeyUpCore(e, _currentCell.RowIndex);
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
