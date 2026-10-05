namespace System.Windows.Forms;

public partial class DataGridView
{
    /// <summary>Writes the current editor value without closing its editing control.</summary>
    public bool CommitEdit(DataGridViewDataErrorContexts context) => CommitCurrentCellValue(context);

    /// <summary>Reloads an active editor from the cell's current value, discarding an uncommitted edit.</summary>
    public bool RefreshEdit()
    {
        if (EditingControl is null || _currentCell is not { } cell) return true;

        _preparingEditingControl = true;
        try
        {
            cell.InitializeEditingControl(cell.RowIndex,
                cell.GetEditedFormattedValue(cell.RowIndex, DataGridViewDataErrorContexts.Formatting),
                cell.InheritedStyle);

            if (EditingControl is IDataGridViewEditingControl editor) editor.EditingControlValueChanged = false;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            OnDataError(displayErrorDialogIfNoHandler: false,
                new DataGridViewDataErrorEventArgs(exception, cell.ColumnIndex, cell.RowIndex, DataGridViewDataErrorContexts.Formatting));
            return false;
        }
        finally
        {
            _preparingEditingControl = false;
        }

        NotifyCurrentCellDirty(false);
        return true;
    }

    /// <summary>Notifies the grid that an externally managed cell value changed.</summary>
    public void UpdateCellValue(int columnIndex, int rowIndex)
    {
        ValidateDataCellAddress(columnIndex, rowIndex);
        var column = Columns[columnIndex];

        if (column.InheritedAutoSizeMode is not (DataGridViewAutoSizeColumnMode.None or
            DataGridViewAutoSizeColumnMode.Fill or DataGridViewAutoSizeColumnMode.ColumnHeader))
        {
            AutoFitColumnWidthCore(column, column.InheritedAutoSizeMode);
        }

        if (AutoSizeRowsMode != DataGridViewAutoSizeRowsMode.None) AutoFitRowHeightCore(Rows[rowIndex]);
        InvalidateCell(columnIndex, rowIndex);
    }

    /// <summary>Repaints a cell so virtual-mode error text is requested again.</summary>
    public void UpdateCellErrorText(int columnIndex, int rowIndex)
    {
        if (columnIndex < -1 || columnIndex >= Columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
        if (rowIndex < -1 || rowIndex >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndex));
        InvalidateCell(columnIndex, rowIndex);
    }

    /// <summary>Repaints a row so virtual-mode row error text is requested again.</summary>
    public void UpdateRowErrorText(int rowIndex)
    {
        ValidateRowIndex(rowIndex);
        InvalidateRow(rowIndex);
    }

    /// <summary>Repaints an inclusive range of rows with externally managed error text.</summary>
    public void UpdateRowErrorText(int rowIndexStart, int rowIndexEnd)
    {
        ValidateRowIndex(rowIndexStart);
        ValidateRowIndex(rowIndexEnd);
        if (rowIndexEnd < rowIndexStart) throw new ArgumentOutOfRangeException(nameof(rowIndexEnd));

        for (var rowIndex = rowIndexStart; rowIndex <= rowIndexEnd; rowIndex++) InvalidateRow(rowIndex);
    }

    private void ValidateDataCellAddress(int columnIndex, int rowIndex)
    {
        if (columnIndex < 0 || columnIndex >= Columns.Count) throw new ArgumentOutOfRangeException(nameof(columnIndex));
        ValidateRowIndex(rowIndex);
    }

    private void ValidateRowIndex(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(rowIndex));
    }
}
