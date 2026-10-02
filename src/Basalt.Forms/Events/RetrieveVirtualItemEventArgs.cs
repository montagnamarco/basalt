namespace System.Windows.Forms;

public class RetrieveVirtualItemEventArgs : EventArgs
{
    public RetrieveVirtualItemEventArgs(int itemIndex)
    {
        ItemIndex = itemIndex;
    }

    public ListViewItem? Item { get; set; }

    public int ItemIndex { get; }
}
