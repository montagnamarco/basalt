namespace System.Windows.Forms;

/// <summary>Carries which cell an event is about, by its column and row index.</summary>
public class DataGridViewCellEventArgs : EventArgs
{
    public DataGridViewCellEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}
