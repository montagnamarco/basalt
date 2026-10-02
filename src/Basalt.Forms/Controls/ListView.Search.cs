using System.Drawing;

namespace System.Windows.Forms;

/// <summary>FindItemWithText and FindNearestItem: a local scan over Items, or SearchForVirtualItem while VirtualMode is on.</summary>
public partial class ListView
{
    public ListViewItem FindItemWithText(string text) => FindItemWithText(text, true, 0)!;

    public ListViewItem FindItemWithText(string text, bool includeSubItemsInSearch, int startIndex) =>
        FindItemWithText(text, includeSubItemsInSearch, startIndex, true)!;

    public ListViewItem FindItemWithText(string text, bool includeSubItemsInSearch, int startIndex, bool isPrefixSearch)
    {
        if (_virtualMode) return FindVirtualItem(true, isPrefixSearch, includeSubItemsInSearch, text, Point.Empty, SearchDirectionHint.Down, startIndex)!;
        if (_items.Count == 0) return null!;

        for (var offset = 0; offset < _items.Count; offset++)
        {
            var index = (startIndex + offset) % _items.Count;
            if (ItemMatchesText(_items[index], text, includeSubItemsInSearch, isPrefixSearch)) return _items[index];
        }

        return null!;
    }

    private static bool ItemMatchesText(ListViewItem item, string text, bool includeSubItems, bool isPrefixSearch)
    {
        if (TextMatches(item.Text, text, isPrefixSearch)) return true;
        if (!includeSubItems) return false;

        for (var i = 1; i < item.SubItems.Count; i++)
        {
            if (TextMatches(item.SubItems[i].Text, text, isPrefixSearch)) return true;
        }

        return false;
    }

    private static bool TextMatches(string candidate, string text, bool isPrefixSearch) => isPrefixSearch
        ? candidate.StartsWith(text, StringComparison.CurrentCultureIgnoreCase)
        : string.Equals(candidate, text, StringComparison.CurrentCultureIgnoreCase);

    public ListViewItem FindNearestItem(SearchDirectionHint dir, Point point) => FindNearestItemCore(dir, point)!;

    public ListViewItem FindNearestItem(SearchDirectionHint searchDirection, int x, int y) => FindNearestItemCore(searchDirection, new Point(x, y))!;

    private ListViewItem? FindNearestItemCore(SearchDirectionHint dir, Point point)
    {
        if (_virtualMode) return FindVirtualItem(false, false, false, null, point, dir, -1);

        ListViewItem? nearest = null;
        var nearestDistance = double.MaxValue;

        for (var i = 0; i < _items.Count; i++)
        {
            var center = CenterOf(GetItemRect(i));
            if (!IsInDirection(dir, point, center)) continue;

            var distance = Distance(point, center);
            if (!(distance < nearestDistance)) continue;

            nearestDistance = distance;
            nearest = _items[i];
        }

        return nearest;
    }

    private static bool IsInDirection(SearchDirectionHint dir, Point from, Point to) => dir switch
    {
        SearchDirectionHint.Up => to.Y < from.Y,
        SearchDirectionHint.Down => to.Y > from.Y,
        SearchDirectionHint.Left => to.X < from.X,
        SearchDirectionHint.Right => to.X > from.X,
        _ => false,
    };

    private static Point CenterOf(Rectangle rect) => new(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);

    private static double Distance(Point a, Point b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;

        return Math.Sqrt(dx * dx + dy * dy);
    }
}
