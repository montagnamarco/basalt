namespace System.Windows.Forms;

public class ListViewGroupEventArgs : EventArgs
{
    public ListViewGroupEventArgs(int groupIndex)
    {
        GroupIndex = groupIndex;
    }

    public int GroupIndex { get; }
}
