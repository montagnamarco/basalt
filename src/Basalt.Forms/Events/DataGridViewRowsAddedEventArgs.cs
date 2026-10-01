namespace System.Windows.Forms;

public class DataGridViewRowsAddedEventArgs : EventArgs
{
    public DataGridViewRowsAddedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowCount { get; }

    public int RowIndex { get; }
}
