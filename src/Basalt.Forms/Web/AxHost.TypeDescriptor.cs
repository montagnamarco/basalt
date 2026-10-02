using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// On Windows, AxHost merges the ocx's own COM properties and events into
/// what the designer sees, through ICustomTypeDescriptor. There is no ocx
/// here, so there is nothing to merge in: every member below is exactly
/// what plain reflection over this type already gives, through the
/// TypeDescriptor static helpers every component gets.
/// </summary>
public abstract partial class AxHost
{
    AttributeCollection ICustomTypeDescriptor.GetAttributes() => TypeDescriptor.GetAttributes(this, true);

    string? ICustomTypeDescriptor.GetClassName() => TypeDescriptor.GetClassName(this, true);

    string? ICustomTypeDescriptor.GetComponentName() => TypeDescriptor.GetComponentName(this, true);

    TypeConverter ICustomTypeDescriptor.GetConverter() => TypeDescriptor.GetConverter(this, true);

    EventDescriptor? ICustomTypeDescriptor.GetDefaultEvent() => TypeDescriptor.GetDefaultEvent(this, true);

    PropertyDescriptor? ICustomTypeDescriptor.GetDefaultProperty() => TypeDescriptor.GetDefaultProperty(this, true);

    object? ICustomTypeDescriptor.GetEditor(Type editorBaseType) => TypeDescriptor.GetEditor(this, editorBaseType, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents() => TypeDescriptor.GetEvents(this, true);

    EventDescriptorCollection ICustomTypeDescriptor.GetEvents(Attribute[]? attributes) => TypeDescriptor.GetEvents(this, attributes, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties() => TypeDescriptor.GetProperties(this, true);

    PropertyDescriptorCollection ICustomTypeDescriptor.GetProperties(Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(this, attributes, true);

    object ICustomTypeDescriptor.GetPropertyOwner(PropertyDescriptor? pd) => this;
}
