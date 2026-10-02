namespace System.Windows.Forms;

/// <summary>Lets a virtual-mode handler supply a row's height and minimum height on demand, before the grid lays it out.</summary>
public class DataGridViewRowHeightInfoNeededEventArgs : EventArgs
{
    internal DataGridViewRowHeightInfoNeededEventArgs(int rowIndex, int height, int minimumHeight)
    {
        RowIndex = rowIndex;
        Height = height;
        MinimumHeight = minimumHeight;
    }

    public int Height { get; set; }

    public int MinimumHeight { get; set; }

    public int RowIndex { get; }
}
