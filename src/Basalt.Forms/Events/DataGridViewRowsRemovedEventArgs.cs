namespace System.Windows.Forms;

public class DataGridViewRowsRemovedEventArgs : EventArgs
{
    public DataGridViewRowsRemovedEventArgs(int rowIndex, int rowCount)
    {
        RowIndex = rowIndex;
        RowCount = rowCount;
    }

    public int RowCount { get; }

    public int RowIndex { get; }
}
