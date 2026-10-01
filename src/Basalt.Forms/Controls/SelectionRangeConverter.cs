using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// Converts a <see cref="SelectionRange"/> to the two-date constructor call
/// the designer writes, and exposes Start/End so a property grid can edit
/// them individually.
/// </summary>
public class SelectionRangeConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) => base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) => base.ConvertFrom(context, culture, value)!;

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(InstanceDescriptor) && value is SelectionRange range)
        {
            var constructor = typeof(SelectionRange).GetConstructor([typeof(DateTime), typeof(DateTime)]);
            if (constructor is not null) return new InstanceDescriptor(constructor, new object[] { range.Start, range.End });
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    /// <summary>Rebuilds a range from the Start and End the property grid edited.</summary>
    public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
    {
        ArgumentNullException.ThrowIfNull(propertyValues);

        return new SelectionRange(
            (DateTime)propertyValues[nameof(SelectionRange.Start)]!,
            (DateTime)propertyValues[nameof(SelectionRange.End)]!);
    }

    public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

    [RequiresUnreferencedCode("The Type of value cannot be statically discovered. The public parameterless constructor or the 'Default' static field may be trimmed from the Attribute's Type.")]
    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(typeof(SelectionRange), attributes);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
}
