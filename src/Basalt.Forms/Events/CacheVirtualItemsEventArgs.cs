namespace System.Windows.Forms;

public class CacheVirtualItemsEventArgs : EventArgs
{
    public CacheVirtualItemsEventArgs(int startIndex, int endIndex)
    {
        StartIndex = startIndex;
        EndIndex = endIndex;
    }

    public int EndIndex { get; }

    public int StartIndex { get; }
}
