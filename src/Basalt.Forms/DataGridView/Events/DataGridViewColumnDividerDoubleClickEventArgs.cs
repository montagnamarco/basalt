namespace System.Windows.Forms;

/// <summary>A double click on the divider to the right of a column header: which column, and the mouse details a handler can mark Handled to skip the default auto-fit.</summary>
public class DataGridViewColumnDividerDoubleClickEventArgs : HandledMouseEventArgs
{
    public DataGridViewColumnDividerDoubleClickEventArgs(int columnIndex, HandledMouseEventArgs? e)
        : base(e?.Button ?? MouseButtons.Left, e?.Clicks ?? 2, e?.X ?? 0, e?.Y ?? 0, e?.Delta ?? 0, e?.Handled ?? false)
    {
        ColumnIndex = columnIndex;
    }

    public int ColumnIndex { get; }
}
