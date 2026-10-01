namespace System.Windows.Forms;

/// <summary>What <see cref="ListView.HitTest(Drawing.Point)"/> found at a point: which item, which sub-item, and where within it.</summary>
public class ListViewHitTestInfo
{
    public ListViewHitTestInfo(ListViewItem? hitItem, ListViewItem.ListViewSubItem? hitSubItem, ListViewHitTestLocations hitLocation)
    {
        Item = hitItem;
        SubItem = hitSubItem;
        Location = hitLocation;
    }

    public ListViewItem? Item { get; }

    public ListViewHitTestLocations Location { get; }

    public ListViewItem.ListViewSubItem? SubItem { get; }
}
