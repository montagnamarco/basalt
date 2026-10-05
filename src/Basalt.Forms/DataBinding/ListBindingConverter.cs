using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Serializes a binding's source, member and target property for a designer.</summary>
public class ListBindingConverter : TypeConverter
{
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(InstanceDescriptor) && value is Binding binding)
        {
            var constructor = typeof(Binding).GetConstructor([
                typeof(string), typeof(object), typeof(string), typeof(bool),
                typeof(DataSourceUpdateMode), typeof(object), typeof(string), typeof(IFormatProvider),
            ]);
            if (constructor is not null)
            {
                return new InstanceDescriptor(constructor,
                    new object?[] {
                        binding.PropertyName, binding.DataSource, binding.BindingMemberInfo.BindingMember,
                        binding.FormattingEnabled, binding.DataSourceUpdateMode, binding.NullValue,
                        binding.FormatString, binding.FormatInfo,
                    });
            }
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
    {
        ArgumentNullException.ThrowIfNull(propertyValues);

        return new Binding(
            (string)propertyValues[nameof(Binding.PropertyName)]!,
            propertyValues[nameof(Binding.DataSource)],
            (string?)propertyValues["DataMember"]);
    }

    public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;
}
