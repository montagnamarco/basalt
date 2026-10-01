namespace System.Windows.Forms;

/// <summary>Raised whenever one item's Selected state changes, naming that item and its new state.</summary>
public class ListViewItemSelectionChangedEventArgs : EventArgs
{
    public ListViewItemSelectionChangedEventArgs(ListViewItem? item, int itemIndex, bool isSelected)
    {
        Item = item;
        ItemIndex = itemIndex;
        IsSelected = isSelected;
    }

    public bool IsSelected { get; }

    public ListViewItem? Item { get; }

    public int ItemIndex { get; }
}
