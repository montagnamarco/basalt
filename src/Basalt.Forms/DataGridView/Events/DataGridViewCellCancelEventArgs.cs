using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>Carries which cell an event is about, and lets a handler cancel it.</summary>
public class DataGridViewCellCancelEventArgs : CancelEventArgs
{
    public DataGridViewCellCancelEventArgs(int columnIndex, int rowIndex)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}
