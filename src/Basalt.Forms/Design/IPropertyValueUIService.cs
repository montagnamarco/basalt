using System.ComponentModel;

namespace System.Drawing.Design;

/// <summary>The service a PropertyGrid host can offer so a PropertyDescriptor's row grows extra clickable glyphs (PropertyValueUIItem); this family's PropertyGrid does not draw them (see its left-out list), but the service contract itself is implementable by anyone who hosts one.</summary>
public interface IPropertyValueUIService
{
    event EventHandler PropertyUIValueItemsChanged;

    void AddPropertyValueUIHandler(PropertyValueUIHandler newHandler);

    PropertyValueUIItem[] GetPropertyUIValueItems(ITypeDescriptorContext context, PropertyDescriptor propDesc);

    void NotifyPropertyValueUIItemsChanged();

    void RemovePropertyValueUIHandler(PropertyValueUIHandler newHandler);
}
