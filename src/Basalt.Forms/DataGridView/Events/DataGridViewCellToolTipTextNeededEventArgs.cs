namespace System.Windows.Forms;

/// <summary>Lets a handler supply a cell's tool tip text on demand, instead of the grid only ever using the one set directly on the cell.</summary>
public class DataGridViewCellToolTipTextNeededEventArgs : DataGridViewCellEventArgs
{
    internal DataGridViewCellToolTipTextNeededEventArgs(int columnIndex, int rowIndex, string toolTipText)
        : base(columnIndex, rowIndex)
    {
        ToolTipText = toolTipText;
    }

    public string? ToolTipText { get; set; }
}
