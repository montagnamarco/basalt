using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// The run of a LinkLabel's text that is a link when <see cref="LinkLabel.Links"/>
/// has not been filled in by hand: a start index and a length, or -1 for "to the end".
/// </summary>
[TypeConverter(typeof(LinkArea.LinkAreaConverter))]
public struct LinkArea : IEquatable<LinkArea>
{
    public LinkArea(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; set; }

    public int Length { get; set; }

    /// <summary>Whether the area covers no text at all: both fields still at zero.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsEmpty => Start == 0 && Length == 0;

    public override bool Equals(object? o) => o is LinkArea other && Equals(other);

    public bool Equals(LinkArea other) => Start == other.Start && Length == other.Length;

    public override int GetHashCode() => HashCode.Combine(Start, Length);

    public override string ToString() => $"{{Start={Start}, Length={Length}}}";

    public static bool operator ==(LinkArea linkArea1, LinkArea linkArea2) => linkArea1.Equals(linkArea2);

    public static bool operator !=(LinkArea linkArea1, LinkArea linkArea2) => !linkArea1.Equals(linkArea2);

    /// <summary>Converts a LinkArea to and from the "start, length" text the designer writes.</summary>
    public class LinkAreaConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
            destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text)
            {
                culture ??= CultureInfo.CurrentCulture;
                var separator = culture.TextInfo.ListSeparator;
                var parts = text.Split(separator);

                if (parts.Length == 2
                    && int.TryParse(parts[0].Trim(), NumberStyles.Integer, culture, out var start)
                    && int.TryParse(parts[1].Trim(), NumberStyles.Integer, culture, out var length))
                {
                    return new LinkArea(start, length);
                }

                throw new ArgumentException($"Value '{text}' is not a valid value for LinkArea. The LinkArea should be of the form \"Start,Length\".", nameof(value));
            }

            return base.ConvertFrom(context, culture, value)!;
        }

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            ArgumentNullException.ThrowIfNull(destinationType);

            if (value is LinkArea area)
            {
                if (destinationType == typeof(string))
                {
                    culture ??= CultureInfo.CurrentCulture;
                    return $"{area.Start}{culture.TextInfo.ListSeparator} {area.Length}";
                }

                if (destinationType == typeof(InstanceDescriptor))
                {
                    var constructor = typeof(LinkArea).GetConstructor([typeof(int), typeof(int)]);
                    if (constructor is not null) return new InstanceDescriptor(constructor, new object[] { area.Start, area.Length });
                }
            }

            return base.ConvertTo(context, culture, value, destinationType)!;
        }

        public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
        {
            ArgumentNullException.ThrowIfNull(propertyValues);

            return new LinkArea((int)propertyValues[nameof(Start)]!, (int)propertyValues[nameof(Length)]!);
        }

        public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

        [RequiresUnreferencedCode("The Type of value cannot be statically discovered. The public parameterless constructor or the 'Default' static field may be trimmed from the Attribute's Type.")]
        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
            TypeDescriptor.GetProperties(typeof(LinkArea), attributes).Sort([nameof(Start), nameof(Length)]);

        public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;
    }
}
