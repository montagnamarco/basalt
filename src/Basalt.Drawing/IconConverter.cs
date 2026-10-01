using System.ComponentModel;
using System.Globalization;

namespace System.Drawing;

/// <summary>Converts an <see cref="Icon"/> to and from the byte array a .resx stores it as.</summary>
public class IconConverter : ExpandableObjectConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value) =>
        value is byte[] bytes && bytes.Length > 0
            ? new Icon(new MemoryStream(bytes))
            // The base method's own signature is not annotated nullable, even
            // though it happily returns null for a null value.
            : base.ConvertFrom(context, culture, value!);

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(byte[]))
        {
            if (value is not Icon icon) return base.ConvertTo(context, culture, value, destinationType);

            using var stream = new MemoryStream();
            icon.Save(stream);
            return stream.ToArray();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }
}
