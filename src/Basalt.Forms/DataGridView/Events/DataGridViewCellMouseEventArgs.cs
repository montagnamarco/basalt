namespace System.Windows.Forms;

/// <summary>A mouse event over one cell: the button, click state and wheel delta from <paramref name="e"/>, plus which cell and where inside it.</summary>
public class DataGridViewCellMouseEventArgs : MouseEventArgs
{
    public DataGridViewCellMouseEventArgs(int columnIndex, int rowIndex, int localX, int localY, MouseEventArgs? e)
        : base(e?.Button ?? MouseButtons.None, e?.Clicks ?? 0, localX, localY, e?.Delta ?? 0)
    {
        ColumnIndex = columnIndex;
        RowIndex = rowIndex;
    }

    public int ColumnIndex { get; }

    public int RowIndex { get; }
}
