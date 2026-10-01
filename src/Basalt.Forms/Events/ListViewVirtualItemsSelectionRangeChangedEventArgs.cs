namespace System.Windows.Forms;

public class ListViewVirtualItemsSelectionRangeChangedEventArgs : EventArgs
{
    public ListViewVirtualItemsSelectionRangeChangedEventArgs(int startIndex, int endIndex, bool isSelected)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
        IsSelected = isSelected;
    }

    public int EndIndex { get; }

    public bool IsSelected { get; }

    public int StartIndex { get; }
}
