using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Shows a ListView column by its caption in designers.</summary>
public class ColumnHeaderConverter : ExpandableObjectConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is ColumnHeader column) return column.Text;
        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
