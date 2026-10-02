using System.Collections.Generic;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>CurrentCell and the selection sets: which cells, rows or columns SetSelectedCellCore and friends mark, by SelectionMode.</summary>
public partial class DataGridView
{
    public void SelectAll()
    {
        if (Columns.Count == 0 || Rows.Count == 0) return;

        _selectedRowIndexes.Clear();
        _selectedColumnIndexes.Clear();
        _selectedCells.Clear();

        switch (SelectionMode)
        {
            case DataGridViewSelectionMode.FullRowSelect or DataGridViewSelectionMode.RowHeaderSelect:
                for (var index = 0; index < Rows.Count; index++) _selectedRowIndexes.Add(index);
                break;

            case DataGridViewSelectionMode.FullColumnSelect or DataGridViewSelectionMode.ColumnHeaderSelect:
                foreach (DataGridViewColumn column in Columns) _selectedColumnIndexes.Add(column.Index);
                break;

            default:
                foreach (DataGridViewRow row in Rows)
                foreach (DataGridViewColumn column in Columns)
                    _selectedCells.Add(row.Cells[column.Index]);
                break;
        }

        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    protected virtual void SetSelectedCellCore(int columnIndex, int rowIndex, bool selected)
    {
        var cell = Rows[rowIndex].Cells[columnIndex];
        SetCellSelectedCore(cell, selected);
    }

    /// <summary>
    /// The one place a cell is actually added to or removed from the selection set: called both
    /// by SetSelectedCellCore above and directly by DataGridViewCell.Selected's setter, so setting
    /// a cell's own Selected property and going through the grid's SelectCell stay in agreement.
    /// </summary>
    internal void SetCellSelectedCore(DataGridViewCell cell, bool selected)
    {
        if (selected) _selectedCells.Add(cell);
        else _selectedCells.Remove(cell);
    }

    protected virtual void SetSelectedColumnCore(int columnIndex, bool selected)
    {
        if (selected) _selectedColumnIndexes.Add(columnIndex);
        else _selectedColumnIndexes.Remove(columnIndex);
    }

    protected virtual void SetSelectedRowCore(int rowIndex, bool selected)
    {
        if (selected) _selectedRowIndexes.Add(rowIndex);
        else _selectedRowIndexes.Remove(rowIndex);
    }

    protected virtual bool SetCurrentCellAddressCore(int columnIndex, int rowIndex, bool setAnchorCellAddress, bool validateCurrentCell, bool throughMouseClick)
    {
        if (columnIndex < 0 || rowIndex < 0 || columnIndex >= Columns.Count || rowIndex >= Rows.Count) return false;

        return SetCurrentCell(Rows[rowIndex].Cells[columnIndex], throughMouseClick);
    }

    /// <summary>Moves CurrentCell, ending any edit on the old one first and raising CurrentCellChanged when it really moved.</summary>
    /// <returns>False, leaving everything as it was, when the edit on the old cell could not be committed (CellValidating cancelled) or RowValidating cancels leaving the row.</returns>
    private bool SetCurrentCell(DataGridViewCell? value, bool throughMouseClick)
    {
        if (ReferenceEquals(_currentCell, value)) return true;

        if (_currentCell is not null && IsCurrentCellInEditMode && !EndEditCore(DataGridViewDataErrorContexts.CurrentCellChange)) return false;

        var previous = _currentCell;
        var rowChanging = previous?.OwningRow != value?.OwningRow;

        // Leaving a row (not just moving between its own cells) validates it first, exactly as
        // leaving a cell in edit mode validates the cell first: a cancelled RowValidating keeps
        // CurrentCell where it was, the same refusal EndEditCore above already gives a cancelled
        // CellValidating.
        if (rowChanging && previous?.OwningRow is { } previousRow)
        {
            var rowValidating = new DataGridViewCellCancelEventArgs(previous.ColumnIndex, previousRow.Index);
            OnRowValidating(rowValidating);
            if (rowValidating.Cancel) return false;
        }

        _currentCell = value;

        if (previous is not null)
        {
            InvalidateCellCore(previous);
            OnCellLeave(new DataGridViewCellEventArgs(previous.ColumnIndex, previous.RowIndex));

            if (rowChanging && previous.OwningRow is { } leftRow)
            {
                OnRowValidated(new DataGridViewCellEventArgs(previous.ColumnIndex, leftRow.Index));
                OnRowLeave(new DataGridViewCellEventArgs(previous.ColumnIndex, leftRow.Index));
            }
        }

        if (value is not null)
        {
            InvalidateCellCore(value);
            OnCellEnter(new DataGridViewCellEventArgs(value.ColumnIndex, value.RowIndex));
            if (rowChanging) OnRowEnter(new DataGridViewCellEventArgs(value.ColumnIndex, value.RowIndex));
        }

        OnCurrentCellChanged(EventArgs.Empty);

        // Keeps a bound CurrencyManager's Position pointed at the same row CurrentCell just moved to; a no-op while unbound.
        SyncCurrencyManagerPosition();

        return true;
    }

    /// <summary>
    /// Before a row or column is removed: an edit in it is committed first, and a commit that
    /// validation refuses stops the removal before anything changes, as WinForms refuses it.
    /// </summary>
    internal void EndEditBeforeRemoving(Func<DataGridViewCell, bool> isRemoved)
    {
        if (!IsCurrentCellInEditMode || _currentCell is not { } current || !isRemoved(current)) return;

        if (!EndEditCore(DataGridViewDataErrorContexts.CurrentCellChange))
        {
            throw new InvalidOperationException("Operation did not succeed because the program cannot commit or quit a cell value change.");
        }
    }

    /// <summary>
    /// Applies a click or key selection at one cell, honouring SelectionMode and whether the
    /// gesture extends (Shift) or toggles (Ctrl) the existing selection rather than replacing it.
    /// </summary>
    internal void SelectCell(int columnIndex, int rowIndex, bool extend, bool toggle)
    {
        if (columnIndex < 0 || rowIndex < 0 || columnIndex >= Columns.Count || rowIndex >= Rows.Count) return;

        var replacing = !extend && !toggle;
        if (replacing) ClearSelectionQuiet();

        switch (SelectionMode)
        {
            case DataGridViewSelectionMode.FullRowSelect or DataGridViewSelectionMode.RowHeaderSelect:
                ApplyRangeOrToggle(_selectedRowIndexes, rowIndex, extend, toggle, AnchorRow);
                break;

            case DataGridViewSelectionMode.FullColumnSelect or DataGridViewSelectionMode.ColumnHeaderSelect:
                ApplyRangeOrToggle(_selectedColumnIndexes, columnIndex, extend, toggle, AnchorColumn);
                break;

            default:
                ApplyCellRangeOrToggle(columnIndex, rowIndex, extend, toggle);
                break;
        }

        AnchorRow = rowIndex;
        AnchorColumn = columnIndex;

        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Where a Shift range started: the cell last chosen without Shift.</summary>
    private int AnchorRow { get; set; }

    private int AnchorColumn { get; set; }

    private void ClearSelectionQuiet()
    {
        _selectedCells.Clear();
        _selectedRowIndexes.Clear();
        _selectedColumnIndexes.Clear();
    }

    private void ApplyRangeOrToggle(HashSet<int> set, int index, bool extend, bool toggle, int anchor)
    {
        if (toggle)
        {
            if (!set.Remove(index)) set.Add(index);
            return;
        }

        if (extend && MultiSelect)
        {
            var low = Math.Min(anchor, index);
            var high = Math.Max(anchor, index);
            for (var candidate = low; candidate <= high; candidate++) set.Add(candidate);
            return;
        }

        set.Clear();
        set.Add(index);
    }

    private void ApplyCellRangeOrToggle(int columnIndex, int rowIndex, bool extend, bool toggle)
    {
        var cell = Rows[rowIndex].Cells[columnIndex];

        if (toggle)
        {
            if (!_selectedCells.Remove(cell)) _selectedCells.Add(cell);
            return;
        }

        if (extend && MultiSelect)
        {
            var columnLow = Math.Min(AnchorColumn, columnIndex);
            var columnHigh = Math.Max(AnchorColumn, columnIndex);
            var rowLow = Math.Min(AnchorRow, rowIndex);
            var rowHigh = Math.Max(AnchorRow, rowIndex);

            for (var r = rowLow; r <= rowHigh; r++)
            for (var c = columnLow; c <= columnHigh; c++)
                _selectedCells.Add(Rows[r].Cells[c]);

            return;
        }

        _selectedCells.Clear();
        _selectedCells.Add(cell);
    }
}
