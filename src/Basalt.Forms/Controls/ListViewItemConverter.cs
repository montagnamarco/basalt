using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Shows a list item by its label in designers while exposing its editable properties.</summary>
public class ListViewItemConverter : ExpandableObjectConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is ListViewItem item) return item.Text;
        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
