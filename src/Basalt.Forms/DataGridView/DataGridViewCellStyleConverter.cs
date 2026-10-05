using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Lets the designer construct a cell style, then serialize its editable properties.</summary>
public class DataGridViewCellStyleConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(InstanceDescriptor) && value is DataGridViewCellStyle)
        {
            var constructor = typeof(DataGridViewCellStyle).GetConstructor(Type.EmptyTypes)!;
            // The default constructor does not encode property values. Marking the descriptor
            // incomplete makes the designer serialize those properties after construction.
            return new InstanceDescriptor(constructor, Array.Empty<object>(), isComplete: false);
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
