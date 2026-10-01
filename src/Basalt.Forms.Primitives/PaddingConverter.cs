using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// Converts a <see cref="Padding"/> to and from "left, top, right, bottom"
/// text with the culture's list separator, and to the constructor call the
/// designer writes.
/// </summary>
public class PaddingConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);

        text = text.Trim();
        if (text.Length == 0) return null;

        culture ??= CultureInfo.CurrentCulture;
        var separator = culture.TextInfo.ListSeparator[0];
        var parts = text.Split(separator);
        var converter = TypeDescriptor.GetConverter(typeof(int));
        var values = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            values[i] = (int)converter.ConvertFromString(context, culture, parts[i])!;
        }

        if (values.Length != 4)
        {
            throw new ArgumentException($"Text \"{text}\" cannot be parsed. The expected text format is \"Left{separator} Top{separator} Right{separator} Bottom\".", nameof(value));
        }

        return new Padding(values[0], values[1], values[2], values[3]);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        if (value is Padding padding)
        {
            if (destinationType == typeof(string))
            {
                culture ??= CultureInfo.CurrentCulture;
                var separator = culture.TextInfo.ListSeparator + " ";
                var converter = TypeDescriptor.GetConverter(typeof(int));
                int[] sides = [padding.Left, padding.Top, padding.Right, padding.Bottom];

                return string.Join(separator, sides.Select(side => converter.ConvertToString(context, culture, side)));
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                // The short form when the four sides were set together, as the designer writes it.
                if (padding.All != -1)
                {
                    return new InstanceDescriptor(typeof(Padding).GetConstructor([typeof(int)]), new object[] { padding.All });
                }

                return new InstanceDescriptor(
                    typeof(Padding).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(int)]),
                    new object[] { padding.Left, padding.Top, padding.Right, padding.Bottom });
            }
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    /// <summary>
    /// Rebuilds a padding after one property changed in a property grid: a
    /// change to All sets the four sides, any other change keeps them apart.
    /// </summary>
    public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
    {
        ArgumentNullException.ThrowIfNull(propertyValues);

        var original = context?.PropertyDescriptor?.GetValue(context.Instance);

        try
        {
            var all = (int)propertyValues[nameof(Padding.All)]!;
            if (original is Padding before && before.All != all) return new Padding(all);

            return new Padding(
                (int)propertyValues[nameof(Padding.Left)]!,
                (int)propertyValues[nameof(Padding.Top)]!,
                (int)propertyValues[nameof(Padding.Right)]!,
                (int)propertyValues[nameof(Padding.Bottom)]!);
        }
        catch (InvalidCastException exception)
        {
            throw new ArgumentException("One or more entries are not valid in the IDictionary parameter. Verify that all values match up to the object's properties.", nameof(propertyValues), exception);
        }
        catch (NullReferenceException exception)
        {
            throw new ArgumentException("Some of the values in the IDictionary parameter are missing or not valid.", nameof(propertyValues), exception);
        }
    }

    public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(typeof(Padding), attributes).Sort([nameof(Padding.All), nameof(Padding.Left), nameof(Padding.Top), nameof(Padding.Right), nameof(Padding.Bottom)]);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
}
