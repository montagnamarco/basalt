namespace System.Windows.Forms;

/// <summary>Carries a cell's value for virtual-mode CellValueNeeded/CellValuePushed handlers to read or write.</summary>
public class DataGridViewCellValueEventArgs : EventArgs
{
    public DataGridViewCellValueEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }

    public object? Value { get; set; }
}
