using System.ComponentModel;

namespace System.Drawing.Design;

/// <summary>Invoked when a user clicks one of the little glyphs PropertyGrid draws next to a property that has PropertyValueUIItems (an IPropertyValueUIService add-on, not drawn by this family's PropertyGrid - see its left-out list).</summary>
public delegate void PropertyValueUIItemInvokeHandler(ITypeDescriptorContext context, PropertyDescriptor descriptor, PropertyValueUIItem invokedItem);

/// <summary>One such glyph: the image PropertyGrid would paint, the tooltip, and what runs when it is clicked.</summary>
public class PropertyValueUIItem
{
    public PropertyValueUIItem(Image uiItemImage, PropertyValueUIItemInvokeHandler handler, string tooltip)
    {
        ArgumentNullException.ThrowIfNull(uiItemImage);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(tooltip);

        Image = uiItemImage;
        InvokeHandler = handler;
        ToolTip = tooltip;
    }

    public virtual Image Image { get; }

    public virtual PropertyValueUIItemInvokeHandler InvokeHandler { get; }

    public virtual string ToolTip { get; }

    public virtual void Reset()
    {
    }
}
