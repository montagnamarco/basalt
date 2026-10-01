using System.ComponentModel;

namespace System.Windows.Forms;

public class ColumnWidthChangingEventArgs : CancelEventArgs
{
    public ColumnWidthChangingEventArgs(int columnIndex, int newWidth)
    {
        ColumnIndex = columnIndex;
        NewWidth = newWidth;
    }

    public ColumnWidthChangingEventArgs(int columnIndex, int newWidth, bool cancel)
        : base(cancel)
    {
        ColumnIndex = columnIndex;
        NewWidth = newWidth;
    }

    public int ColumnIndex { get; }

    public int NewWidth { get; set; }
}
