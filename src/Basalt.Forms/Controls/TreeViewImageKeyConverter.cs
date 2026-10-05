using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

public class TreeViewImageKeyConverter : ImageKeyConverter
{
    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && (value is null || value is string { Length: 0 })) return "(default)";
        return base.ConvertTo(context, culture, value, destinationType);
    }
}
