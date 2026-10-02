using System.ComponentModel;
using System.Windows.Forms.Design;

namespace System.Windows.Forms.PropertyGridInternal;

/// <summary>The one tab PropertyGrid ships with: every browsable property of the component, straight from TypeDescriptor.</summary>
internal sealed class PropertiesTab : PropertyTab
{
    public override string HelpKeyword => "net.ComponentModel.PropertyGrid";

    public override string TabName => "Properties";

    public override PropertyDescriptor? GetDefaultProperty(object obj) => TypeDescriptor.GetDefaultProperty(obj);

    public override PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(component, attributes);
}
