namespace System.Windows.Forms;

public class ColumnWidthChangedEventArgs : EventArgs
{
    public ColumnWidthChangedEventArgs(int columnIndex)
    {
        ColumnIndex = columnIndex;
    }

    public int ColumnIndex { get; }
}
