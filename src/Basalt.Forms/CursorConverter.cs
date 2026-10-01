using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;
using System.Reflection;

namespace System.Windows.Forms;

/// <summary>
/// Converts standard cursors to and from their names, and to the Cursors
/// property the designer writes ("Cursors.Hand").
/// </summary>
public class CursorConverter : TypeConverter
{
    private StandardValuesCollection? _values;

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string name)
        {
            var property = CursorProperty(name.Trim());

            return property?.GetValue(null) ?? throw new ArgumentException($"Value '{name}' is not a valid value for Cursor.", nameof(value));
        }

        if (value is byte[] bytes) return new Cursor(new MemoryStream(bytes));

        return base.ConvertFrom(context, culture, value);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is Cursor cursor)
        {
            if (destinationType == typeof(string))
            {
                return CursorProperty(cursor.Name) is not null ? cursor.Name : "(Icon)";
            }

            if (destinationType == typeof(InstanceDescriptor) && CursorProperty(cursor.Name) is { } property)
            {
                return new InstanceDescriptor(property, null);
            }

            if (destinationType == typeof(byte[]))
            {
                throw new FormatException("A standard cursor has no image to write; only cursors loaded from a file can be stored.");
            }
        }

        if (value is null && destinationType == typeof(string)) return "(none)";

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        _values ??= new StandardValuesCollection(typeof(Cursors).GetProperties(BindingFlags.Public | BindingFlags.Static).Select(property => property.GetValue(null)).ToArray());

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    private static PropertyInfo? CursorProperty(string name) =>
        string.IsNullOrEmpty(name) ? null : typeof(Cursors).GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
}
