namespace System.Windows.Forms;

public class ColumnClickEventArgs : EventArgs
{
    public ColumnClickEventArgs(int column)
    {
        Column = column;
    }

    public int Column { get; }
}
