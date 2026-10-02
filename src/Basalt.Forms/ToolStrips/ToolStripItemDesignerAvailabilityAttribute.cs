namespace System.Windows.Forms.Design;

/// <summary>
/// Marks a <see cref="ToolStripItem"/> subclass with which kinds of
/// <see cref="ToolStrip"/> the designer's "Insert Standard Item" menu offers
/// it for; a custom item without this attribute is offered everywhere
/// (<see cref="ToolStripItemDesignerAvailability.All"/>, the same as
/// <see cref="Default"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ToolStripItemDesignerAvailabilityAttribute : Attribute
{
    /// <summary>The attribute an item carries implicitly when it has none of its own: available everywhere.</summary>
    public static readonly ToolStripItemDesignerAvailabilityAttribute Default = new();

    public ToolStripItemDesignerAvailabilityAttribute()
        : this(ToolStripItemDesignerAvailability.All)
    {
    }

    public ToolStripItemDesignerAvailabilityAttribute(ToolStripItemDesignerAvailability itemAdditionVisibility)
    {
        ItemAdditionVisibility = itemAdditionVisibility;
    }

    public ToolStripItemDesignerAvailability ItemAdditionVisibility { get; }

    public override bool Equals(object? obj) =>
        obj is ToolStripItemDesignerAvailabilityAttribute other && other.ItemAdditionVisibility == ItemAdditionVisibility;

    public override int GetHashCode() => ItemAdditionVisibility.GetHashCode();

    public override bool IsDefaultAttribute() => Equals(Default);
}
