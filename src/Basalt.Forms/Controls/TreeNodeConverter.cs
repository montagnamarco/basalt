using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Lets a designer show a <see cref="TreeNode"/> as its Text in a property grid.</summary>
public class TreeNodeConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is TreeNode node) return node.Text;

        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
