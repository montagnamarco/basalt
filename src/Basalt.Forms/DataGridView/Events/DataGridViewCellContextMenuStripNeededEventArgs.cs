namespace System.Windows.Forms;

/// <summary>Lets a handler supply a cell's ContextMenuStrip on demand, instead of the grid only ever using the one set directly on the cell/row/column.</summary>
public class DataGridViewCellContextMenuStripNeededEventArgs : DataGridViewCellEventArgs
{
    public DataGridViewCellContextMenuStripNeededEventArgs(int columnIndex, int rowIndex)
        : base(columnIndex, rowIndex)
    {
    }

    public ContextMenuStrip? ContextMenuStrip { get; set; }
}
