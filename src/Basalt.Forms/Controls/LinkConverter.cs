using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Converts a LinkLabel link to editable text and designer constructor data.</summary>
public class LinkConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            culture ??= CultureInfo.CurrentCulture;
            var parts = text.Split(culture.TextInfo.ListSeparator);
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), NumberStyles.Integer, culture, out var start)
                && int.TryParse(parts[1].Trim(), NumberStyles.Integer, culture, out var length))
            {
                return new LinkLabel.Link(start, length);
            }

            throw new ArgumentException($"Value '{text}' is not a valid link. Expected start and length.", nameof(value));
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is LinkLabel.Link link)
        {
            if (destinationType == typeof(string))
            {
                culture ??= CultureInfo.CurrentCulture;
                return string.Join(culture.TextInfo.ListSeparator + " ",
                    link.Start.ToString(culture), link.Length.ToString(culture));
            }

            if (destinationType == typeof(InstanceDescriptor))
            {
                if (link.LinkData is null)
                {
                    var constructor = typeof(LinkLabel.Link).GetConstructor([typeof(int), typeof(int)]);
                    if (constructor is not null) return new InstanceDescriptor(constructor, new object[] { link.Start, link.Length });
                }
                else
                {
                    var constructor = typeof(LinkLabel.Link).GetConstructor([typeof(int), typeof(int), typeof(object)]);
                    if (constructor is not null)
                    {
                        return new InstanceDescriptor(constructor, new object?[] { link.Start, link.Length, link.LinkData });
                    }
                }
            }
        }

        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
