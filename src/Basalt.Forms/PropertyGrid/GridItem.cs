using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// One node of the tree a PropertyGrid shows: the root (the selected object
/// itself), a category heading, or a property - including an expanded
/// property's own sub-properties. PropertyGrid never hands out the tree as a
/// whole; code walks it starting from SelectedGridItem, up through Parent and
/// down through GridItems, exactly as Microsoft's does.
/// </summary>
public abstract class GridItem
{
    protected GridItem()
    {
    }

    /// <summary>True when the item has children (a category, or a property an ExpandableObjectConverter can open).</summary>
    public virtual bool Expandable => GridItems.Count > 0;

    /// <summary>Whether an expandable item currently shows its children.</summary>
    public virtual bool Expanded { get; set; }

    public abstract GridItemType GridItemType { get; }

    public abstract GridItemCollection GridItems { get; }

    public abstract string? Label { get; }

    public abstract GridItem? Parent { get; }

    public abstract PropertyDescriptor? PropertyDescriptor { get; }

    [Bindable(true)]
    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    public abstract object? Value { get; }

    /// <summary>Makes this the grid's SelectedGridItem; false for an item that cannot be selected (the root).</summary>
    public abstract bool Select();
}
