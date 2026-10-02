using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Tells a virtual-mode host the height and minimum height the grid computed for a row (after
/// AutoSizeRowsMode or the user resizing it), so the host can store it back in its own data;
/// Handled left true tells the grid the host already has the information and does not need it again.
/// </summary>
public class DataGridViewRowHeightInfoPushedEventArgs : HandledEventArgs
{
    internal DataGridViewRowHeightInfoPushedEventArgs(int rowIndex, int height, int minimumHeight)
    {
        RowIndex = rowIndex;
        Height = height;
        MinimumHeight = minimumHeight;
    }

    public int Height { get; }

    public int MinimumHeight { get; }

    public int RowIndex { get; }
}
