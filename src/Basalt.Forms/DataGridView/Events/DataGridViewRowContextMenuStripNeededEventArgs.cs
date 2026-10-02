namespace System.Windows.Forms;

/// <summary>Lets a handler supply a row's ContextMenuStrip on demand, instead of the grid only ever using the one set directly on the row.</summary>
public class DataGridViewRowContextMenuStripNeededEventArgs : EventArgs
{
    public DataGridViewRowContextMenuStripNeededEventArgs(int rowIndex)
    {
        RowIndex = rowIndex;
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }

    public int RowIndex { get; }
}
