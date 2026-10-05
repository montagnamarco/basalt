using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The On* virtual raisers for the property-changed and cell/column/row events declared in
/// DataGridView.cs, and the internal *Core hooks DataGridViewElement/Band/Column/Row/Cell call
/// to bubble their own notifications up into these without a public dependency the other way.
/// </summary>
public partial class DataGridView
{
    protected virtual void OnAllowUserToAddRowsChanged(EventArgs e) => ((EventHandler?)Events[s_allowUserToAddRowsChangedEvent])?.Invoke(this, e);

    protected virtual void OnAllowUserToDeleteRowsChanged(EventArgs e) => ((EventHandler?)Events[s_allowUserToDeleteRowsChangedEvent])?.Invoke(this, e);

    protected virtual void OnAllowUserToResizeColumnsChanged(EventArgs e) => ((EventHandler?)Events[s_allowUserToResizeColumnsChangedEvent])?.Invoke(this, e);

    protected virtual void OnAllowUserToResizeRowsChanged(EventArgs e) => ((EventHandler?)Events[s_allowUserToResizeRowsChangedEvent])?.Invoke(this, e);

    protected virtual void OnAlternatingRowsDefaultCellStyleChanged(EventArgs e) => ((EventHandler?)Events[s_alternatingRowsDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnBackgroundColorChanged(EventArgs e) => ((EventHandler?)Events[s_backgroundColorChangedEvent])?.Invoke(this, e);

    protected virtual void OnBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_borderStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellBeginEdit(DataGridViewCellCancelEventArgs e) => ((DataGridViewCellCancelEventHandler?)Events[s_cellBeginEditEvent])?.Invoke(this, e);

    protected virtual void OnCellBorderStyleChanged(EventArgs e) => ((EventHandler?)Events[s_cellBorderStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnCellClick(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellClickEvent])?.Invoke(this, e);

    protected virtual void OnCellContentClick(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellContentClickEvent])?.Invoke(this, e);

    protected virtual void OnCellContentDoubleClick(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellContentDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnCellDoubleClick(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellDoubleClickEvent])?.Invoke(this, e);

    protected virtual void OnCellEndEdit(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellEndEditEvent])?.Invoke(this, e);

    protected virtual void OnCellEnter(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellEnterEvent])?.Invoke(this, e);

    protected virtual void OnCellFormatting(DataGridViewCellFormattingEventArgs e) => ((DataGridViewCellFormattingEventHandler?)Events[s_cellFormattingEvent])?.Invoke(this, e);

    protected virtual void OnCellLeave(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellLeaveEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseDown(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_cellMouseDownEvent])?.Invoke(this, e);

    protected virtual void OnCellMouseUp(DataGridViewCellMouseEventArgs e) => ((DataGridViewCellMouseEventHandler?)Events[s_cellMouseUpEvent])?.Invoke(this, e);

    protected virtual void OnCellParsing(DataGridViewCellParsingEventArgs e) => ((DataGridViewCellParsingEventHandler?)Events[s_cellParsingEvent])?.Invoke(this, e);

    protected virtual void OnCellValidated(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellValidatedEvent])?.Invoke(this, e);

    protected virtual void OnCellValidating(DataGridViewCellValidatingEventArgs e) => ((DataGridViewCellValidatingEventHandler?)Events[s_cellValidatingEvent])?.Invoke(this, e);

    protected virtual void OnCellValueChanged(DataGridViewCellEventArgs e) => ((DataGridViewCellEventHandler?)Events[s_cellValueChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnAdded(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnAddedEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeadersDefaultCellStyleChanged(EventArgs e) => ((EventHandler?)Events[s_columnHeadersDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnHeadersHeightChanged(EventArgs e) => ((EventHandler?)Events[s_columnHeadersHeightChangedEvent])?.Invoke(this, e);

    protected virtual void OnColumnRemoved(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnRemovedEvent])?.Invoke(this, e);

    protected virtual void OnColumnWidthChanged(DataGridViewColumnEventArgs e) => ((DataGridViewColumnEventHandler?)Events[s_columnWidthChangedEvent])?.Invoke(this, e);

    protected virtual void OnCurrentCellChanged(EventArgs e) => ((EventHandler?)Events[s_currentCellChangedEvent])?.Invoke(this, e);

    protected virtual void OnDefaultCellStyleChanged(EventArgs e) => ((EventHandler?)Events[s_defaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnEditModeChanged(EventArgs e) => ((EventHandler?)Events[s_editModeChangedEvent])?.Invoke(this, e);

    protected virtual void OnGridColorChanged(EventArgs e) => ((EventHandler?)Events[s_gridColorChangedEvent])?.Invoke(this, e);

    protected virtual void OnMultiSelectChanged(EventArgs e) => ((EventHandler?)Events[s_multiSelectChangedEvent])?.Invoke(this, e);

    protected virtual void OnReadOnlyChanged(EventArgs e) => ((EventHandler?)Events[s_readOnlyChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeadersDefaultCellStyleChanged(EventArgs e) => ((EventHandler?)Events[s_rowHeadersDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeadersWidthChanged(EventArgs e) => ((EventHandler?)Events[s_rowHeadersWidthChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowHeightChanged(DataGridViewRowEventArgs e) => ((DataGridViewRowEventHandler?)Events[s_rowHeightChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowValidating(DataGridViewCellCancelEventArgs e) => ((DataGridViewCellCancelEventHandler?)Events[s_rowValidatingEvent])?.Invoke(this, e);

    protected virtual void OnRowsAdded(DataGridViewRowsAddedEventArgs e) => ((DataGridViewRowsAddedEventHandler?)Events[s_rowsAddedEvent])?.Invoke(this, e);

    protected virtual void OnRowsDefaultCellStyleChanged(EventArgs e) => ((EventHandler?)Events[s_rowsDefaultCellStyleChangedEvent])?.Invoke(this, e);

    protected virtual void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e) => ((DataGridViewRowsRemovedEventHandler?)Events[s_rowsRemovedEvent])?.Invoke(this, e);

    protected virtual void OnSelectionChanged(EventArgs e) => ((EventHandler?)Events[s_selectionChangedEvent])?.Invoke(this, e);

    protected virtual void OnSortCompare(DataGridViewSortCompareEventArgs e) => ((DataGridViewSortCompareEventHandler?)Events[s_sortCompareEvent])?.Invoke(this, e);

    protected virtual void OnSorted(EventArgs e) => ((EventHandler?)Events[s_sortedEvent])?.Invoke(this, e);

    /// <summary>A cell was clicked anywhere (header or data); content clicks additionally raise CellContentClick.</summary>
    internal void RaiseCellClickCore(DataGridViewCellEventArgs e) => OnCellClick(e);

    internal void RaiseCellContentClickCore(DataGridViewCellEventArgs e) => OnCellContentClick(e);

    internal void RaiseCellContentDoubleClickCore(DataGridViewCellEventArgs e) => OnCellContentDoubleClick(e);

    internal void RaiseCellValueChangedCore(DataGridViewCellEventArgs e) => OnCellValueChanged(e);

    internal void InvalidateCellCore(DataGridViewCell cell) => InvalidateCell(cell);

    /// <summary>
    /// Gives every existing row a cell for the new column, at the column's own index, without
    /// disturbing the cells (and their values) the row already had for the other columns. The
    /// previous implementation called row.CreateCells(this) whenever a row looked short, which
    /// rebuilt every cell in the row from scratch and silently discarded whatever values the
    /// other cells already held.
    /// </summary>
    internal void OnColumnAddedCore(DataGridViewColumn column)
    {
        foreach (DataGridViewRow row in Rows)
        {
            if (row.Cells.Count < Columns.Count)
            {
                var index = Math.Min(column.Index, row.Cells.Count);
                var cell = (DataGridViewCell)(column.CellTemplate?.Clone() ?? new DataGridViewTextBoxCell());
                row.Cells.Insert(index, cell);
            }
        }

        // A column added after the grid is already bound starts out empty: fill it from the bound items.
        PopulateNewColumnValues(column);

        OnColumnAdded(new DataGridViewColumnEventArgs(column));
    }

    /// <summary>
    /// Removes the column's own cell from every row (previously left behind, leaving each row
    /// with one cell too many and every cell after the removed column answering to the wrong
    /// column), and moves CurrentCell off the removed column first when it was the current one.
    /// </summary>
    internal void OnColumnRemovedCore(DataGridViewColumn column)
    {
        var removedIndex = column.Index;

        if (_currentCell is { } current && ReferenceEquals(current.OwningColumn, column))
        {
            var next = NextColumnAfterRemoval(column);
            var target = next is not null ? current.OwningRow?.Cells[next.Index] : null;
            SetCurrentCell(target, throughMouseClick: false);
        }

        foreach (DataGridViewRow row in Rows)
        {
            if (removedIndex < row.Cells.Count) row.Cells.RemoveAt(removedIndex);
        }

        OnColumnRemoved(new DataGridViewColumnEventArgs(column));
    }

    /// <summary>The visible column that will sit where the current cell's column used to be once it is removed: the next one, or the previous one when it was the last.</summary>
    private DataGridViewColumn? NextColumnAfterRemoval(DataGridViewColumn removed)
    {
        DataGridViewColumn? next = null;
        DataGridViewColumn? previous = null;

        foreach (DataGridViewColumn candidate in Columns)
        {
            if (ReferenceEquals(candidate, removed) || !candidate.Visible) continue;

            if (candidate.Index > removed.Index && (next is null || candidate.Index < next.Index)) next = candidate;
            if (candidate.Index < removed.Index && (previous is null || candidate.Index > previous.Index)) previous = candidate;
        }

        return next ?? previous;
    }

    internal void OnColumnWidthChangedCore(DataGridViewColumn column)
    {
        OnColumnWidthChanged(new DataGridViewColumnEventArgs(column));
        PositionEditingControlCore();
        Invalidate();
    }

    internal void OnRowHeightChangedCore(DataGridViewRow row)
    {
        OnRowHeightChanged(new DataGridViewRowEventArgs(row));
        PositionEditingControlCore();
        Invalidate();
    }

    internal void OnRowSelectedChangedCore(DataGridViewRow row)
    {
        if (row.Selected) _selectedRowIndexes.Add(row.Index);
        else _selectedRowIndexes.Remove(row.Index);

        OnSelectionChanged(EventArgs.Empty);
        Invalidate();
    }

    internal void OnRowsAddedCore(int rowIndex, int rowCount)
    {
        OnRowsAdded(new DataGridViewRowsAddedEventArgs(rowIndex, rowCount));
        Invalidate();
    }

    /// <summary>
    /// When the removed row held CurrentCell, moves it to the same column in the row that took
    /// its place (the next row, shifted up by the removal) or the new last row when it was the
    /// last one, and clears it once the grid has no rows left - always through SetCurrentCell,
    /// so CellLeave/CurrentCellChanged fire and a pending edit is ended first. A row further down
    /// that merely got reindexed keeps the very same CurrentCell reference, so nothing fires for it.
    /// </summary>
    internal void OnRowsRemovedCore(int rowIndex, int rowCount)
    {
        if (_currentCell is { } current && current.OwningRow is { DataGridView: null })
        {
            var columnIndex = current.ColumnIndex;
            DataGridViewCell? next = null;

            if (Rows.Count > 0 && columnIndex >= 0 && columnIndex < Columns.Count)
            {
                var targetRowIndex = Math.Min(rowIndex, Rows.Count - 1);
                next = Rows[targetRowIndex].Cells[columnIndex];
            }

            SetCurrentCell(next, throughMouseClick: false);
        }

        OnRowsRemoved(new DataGridViewRowsRemovedEventArgs(rowIndex, rowCount));
        Invalidate();
    }

    /// <summary>
    /// Runs CellFormatting for a cell's raw value: a handler can supply the formatted value
    /// directly (and set FormattingApplied), otherwise null tells the cell to format it itself.
    /// </summary>
    internal object? GetFormattedValueCore(DataGridViewCell cell, object? value, int rowIndex, DataGridViewCellStyle cellStyle, DataGridViewDataErrorContexts context)
    {
        var args = new DataGridViewCellFormattingEventArgs(cell.ColumnIndex, rowIndex, value, cell.FormattedValueType, cellStyle);
        OnCellFormatting(args);

        return args.FormattingApplied ? args.Value : null;
    }

    /// <summary>Runs CellParsing for a cell's edited text: a handler can supply the parsed value directly (and set ParsingApplied).</summary>
    internal object? ParseFormattedValueCore(DataGridViewCell cell, object? formattedValue, int rowIndex, DataGridViewCellStyle cellStyle)
    {
        var args = new DataGridViewCellParsingEventArgs(rowIndex, cell.ColumnIndex, formattedValue, cell.ValueType, cellStyle);
        OnCellParsing(args);

        return args.ParsingApplied ? args.Value : null;
    }

    internal Rectangle GetCellContentBoundsCore(DataGridViewCell cell, int rowIndex)
    {
        using var bitmap = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(bitmap);
        return cell.GetContentBoundsCore(graphics, cell.GetInheritedStyle(null, rowIndex, includeColors: true), rowIndex);
    }
}
