namespace System.Windows.Forms;

/// <summary>A double click on the divider below a row header: which row, and the mouse details a handler can mark Handled to skip the default auto-fit.</summary>
public class DataGridViewRowDividerDoubleClickEventArgs : HandledMouseEventArgs
{
    public DataGridViewRowDividerDoubleClickEventArgs(int rowIndex, HandledMouseEventArgs e)
        : base(e.Button, e.Clicks, e.X, e.Y, e.Delta, e.Handled)
    {
        RowIndex = rowIndex;
    }

    public int RowIndex { get; }
}
