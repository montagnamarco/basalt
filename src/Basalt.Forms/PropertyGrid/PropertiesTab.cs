using System.ComponentModel;
using System.Windows.Forms.Design;

namespace System.Windows.Forms.PropertyGridInternal;

/// <summary>The one tab PropertyGrid ships with: every browsable property of the component, straight from TypeDescriptor.</summary>
public class PropertiesTab : PropertyTab
{
    public PropertiesTab()
    {
    }

    public override string HelpKeyword => "net.ComponentModel.PropertyGrid";

    public override string TabName => "Properties";

    public override PropertyDescriptor? GetDefaultProperty(object component) => TypeDescriptor.GetDefaultProperty(component);

    public override PropertyDescriptorCollection GetProperties(object component, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(component, attributes);

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object component, Attribute[]? attributes) =>
        GetProperties(component, attributes);
}
