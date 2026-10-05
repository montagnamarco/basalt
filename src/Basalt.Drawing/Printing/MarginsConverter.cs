using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace System.Drawing.Printing;

/// <summary>
/// Converts a Margins to and from the "left,right,top,bottom" string a
/// property grid or a .resx shows, and rebuilds one from its four sub-values.
/// </summary>
public class MarginsConverter : ExpandableObjectConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);

        culture ??= CultureInfo.CurrentCulture;
        var separator = culture.TextInfo.ListSeparator;
        var parts = text.Split(separator);

        if (parts.Length != 4) throw new ArgumentException($"Cannot parse \"{text}\" as a Margins value.", nameof(value));

        var numbers = new int[4];
        for (var i = 0; i < 4; i++)
        {
            numbers[i] = (int)Convert.ChangeType(parts[i].Trim(), typeof(int), culture);
        }

        return new Margins(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (destinationType == typeof(string) && value is Margins margins)
        {
            culture ??= CultureInfo.CurrentCulture;
            var separator = culture.TextInfo.ListSeparator + " ";

            return string.Join(separator, margins.Left, margins.Right, margins.Top, margins.Bottom);
        }

        if (destinationType == typeof(InstanceDescriptor) && value is Margins marginsForDescriptor)
        {
            var constructor = typeof(Margins).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(int)])!;
            return new InstanceDescriptor(constructor, new object[] {
                marginsForDescriptor.Left,
                marginsForDescriptor.Right,
                marginsForDescriptor.Top,
                marginsForDescriptor.Bottom,
            });
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
    {
        ArgumentNullException.ThrowIfNull(propertyValues);

        return new Margins(
            (int)propertyValues[nameof(Margins.Left)]!,
            (int)propertyValues[nameof(Margins.Right)]!,
            (int)propertyValues[nameof(Margins.Top)]!,
            (int)propertyValues[nameof(Margins.Bottom)]!);
    }

    public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;
}
