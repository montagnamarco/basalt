namespace System.Windows.Forms;

public class ListViewItemMouseHoverEventArgs : EventArgs
{
    public ListViewItemMouseHoverEventArgs(ListViewItem? item)
    {
        Item = item;
    }

    public ListViewItem? Item { get; }
}
