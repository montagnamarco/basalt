namespace System.Windows.Forms;

/// <summary>Raised after a ListViewItem's Checked state actually changed.</summary>
public class ItemCheckedEventArgs : EventArgs
{
    public ItemCheckedEventArgs(ListViewItem item)
    {
        Item = item;
    }

    public ListViewItem Item { get; }
}
