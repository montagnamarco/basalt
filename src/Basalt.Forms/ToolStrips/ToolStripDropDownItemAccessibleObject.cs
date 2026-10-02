namespace System.Windows.Forms;

/// <summary>
/// What <see cref="ToolStripDropDownItem.CreateAccessibilityInstance"/> answers:
/// a plain item plus the submenu as accessible children, and a default action
/// that opens it instead of clicking (a menu item with a submenu is not a
/// button). Top-level, not nested under ToolStripDropDownItem, because that is
/// how Microsoft's own reference assembly shapes it too.
/// </summary>
public class ToolStripDropDownItemAccessibleObject : ToolStripItem.ToolStripItemAccessibleObject
{
    public ToolStripDropDownItemAccessibleObject(ToolStripDropDownItem ownerItem)
        : base(ownerItem)
    {
    }

    private ToolStripDropDownItem Owner => (ToolStripDropDownItem)OwnerItem;

    public override AccessibleRole Role => AccessibleRole.MenuItem;

    public override void DoDefaultAction()
    {
        if (Owner.HasDropDownItems) Owner.ShowDropDown();
        else base.DoDefaultAction();
    }

    public override AccessibleObject GetChild(int index)
    {
        var items = VisibleDropDownItems();

        if (index < 0 || index >= items.Count) throw new ArgumentOutOfRangeException(nameof(index));

        return items[index].AccessibilityObject;
    }

    public override int GetChildCount() => Owner.HasDropDown ? VisibleDropDownItems().Count : -1;

    private List<ToolStripItem> VisibleDropDownItems()
    {
        var items = new List<ToolStripItem>();

        if (!Owner.HasDropDown) return items;

        foreach (ToolStripItem item in Owner.DropDownItems)
        {
            if (item.Available) items.Add(item);
        }

        return items;
    }
}
