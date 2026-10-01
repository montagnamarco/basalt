using System.Drawing;

namespace System.Windows.Forms;

public class SearchForVirtualItemEventArgs : EventArgs
{
    public SearchForVirtualItemEventArgs(bool isTextSearch, bool isPrefixSearch, bool includeSubItemsInSearch, string? text, Point startingPoint, SearchDirectionHint direction, int startIndex)
    {
        IsTextSearch = isTextSearch;
        IsPrefixSearch = isPrefixSearch;
        IncludeSubItemsInSearch = includeSubItemsInSearch;
        Text = text;
        StartingPoint = startingPoint;
        Direction = direction;
        StartIndex = startIndex;
    }

    public SearchDirectionHint Direction { get; }

    public bool IncludeSubItemsInSearch { get; }

    public int Index { get; set; }

    public bool IsPrefixSearch { get; }

    public bool IsTextSearch { get; }

    public int StartIndex { get; }

    public Point StartingPoint { get; }

    public string? Text { get; }
}
