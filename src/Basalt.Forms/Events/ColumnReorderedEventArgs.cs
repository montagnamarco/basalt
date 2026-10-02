using System.ComponentModel;

namespace System.Windows.Forms;

public class ColumnReorderedEventArgs : CancelEventArgs
{
    public ColumnReorderedEventArgs(int oldDisplayIndex, int newDisplayIndex, ColumnHeader? header)
    {
        OldDisplayIndex = oldDisplayIndex;
        NewDisplayIndex = newDisplayIndex;
        Header = header;
    }

    public ColumnHeader? Header { get; }

    public int NewDisplayIndex { get; }

    public int OldDisplayIndex { get; }
}
