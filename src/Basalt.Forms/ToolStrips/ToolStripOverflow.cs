namespace System.Windows.Forms;

/// <summary>
/// The drop-down a <see cref="ToolStripOverflowButton"/> opens: a vertical
/// list of whatever items its strip could not fit. It owns none of them: they
/// stay in the strip's <see cref="ToolStrip.Items"/>, and this drop-down only
/// shows, lays out and hit-tests them through a read-only <see cref="Items"/>.
/// </summary>
public class ToolStripOverflow : ToolStripDropDown
{
    public ToolStripOverflow(ToolStripItem parentItem)
    {
        ArgumentNullException.ThrowIfNull(parentItem);

        OwnerItem = parentItem;
    }

    /// <summary>The strip whose overflowed items this drop-down shows.</summary>
    private ToolStrip? OverflowingStrip => OwnerItem?.Owner;

    /// <summary>A read-only view of the strip's overflowed items, as in WinForms: adding here would steal them from the strip.</summary>
    public override ToolStripItemCollection Items => OverflowingStrip?.OverflowItems ?? ToolStripItemCollection.CreateView(this, []);

    protected internal override ToolStripItemCollection DisplayedItems => Items;

    /// <summary>The strip decided which items overflow; this drop-down lays out every one of them.</summary>
    internal override bool LaysOut(ToolStripItem item) => item.Available;
}
