using System.Collections;
using System.ComponentModel;
using System.ComponentModel.Design.Serialization;
using System.Drawing.Text;
using System.Globalization;

namespace System.Drawing;

/// <summary>
/// Turns a font into the text the property grid and .resx files show, such as
/// "Microsoft Sans Serif, 8.25pt, style=Bold", and back.
/// </summary>
/// <remarks>
/// The text format is the contract: designers write it into .resx files and
/// read it back, so names, the unit suffixes (pt, px, world, in, mm, doc) and
/// the "style=" part follow System.Drawing's, with the culture's list
/// separator between parts.
/// </remarks>
public class FontConverter : TypeConverter
{
    private const string StylePrefix = "style=";

    public FontConverter()
    {
    }

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || destinationType == typeof(InstanceDescriptor) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value);

        text = text.Trim();
        if (text.Length == 0) return null;

        culture ??= CultureInfo.CurrentCulture;
        var separator = culture.TextInfo.ListSeparator[0];

        var parts = text.Split(separator);
        var name = parts[0].Trim();
        var size = 8.25f;
        var unit = GraphicsUnit.Point;
        var style = FontStyle.Regular;

        foreach (var raw in parts.Skip(1))
        {
            var part = raw.Trim();
            if (part.Length == 0) continue;

            if (part.StartsWith(StylePrefix, StringComparison.OrdinalIgnoreCase))
            {
                style |= ParseStyle(part[StylePrefix.Length..]);
            }
            else if (style != FontStyle.Regular || char.IsLetter(part[0]))
            {
                // The styles after "style=" are separated like the parts.
                style |= ParseStyle(part);
            }
            else
            {
                (size, unit) = ParseSize(part, culture);
            }
        }

        return new Font(name, size, style, unit);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (value is Font font && destinationType == typeof(string))
        {
            culture ??= CultureInfo.CurrentCulture;
            var separator = culture.TextInfo.ListSeparator + " ";

            var text = font.Name + separator + font.Size.ToString(culture) + UnitSuffix(font.Unit);
            if (font.Style != FontStyle.Regular)
            {
                text += separator + StylePrefix + font.Style.ToString().Replace(",", separator.TrimEnd(), StringComparison.Ordinal);
            }

            return text;
        }

        if (value is Font described && destinationType == typeof(InstanceDescriptor))
        {
            var constructor = typeof(Font).GetConstructor([typeof(string), typeof(float), typeof(FontStyle), typeof(GraphicsUnit)])!;

            return new InstanceDescriptor(constructor, new object[] { described.Name, described.Size, described.Style, described.Unit });
        }

        if (value is null && destinationType == typeof(string)) return "(none)";

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override object CreateInstance(ITypeDescriptorContext? context, IDictionary propertyValues)
    {
        ArgumentNullException.ThrowIfNull(propertyValues);

        var name = propertyValues["Name"] as string ?? FontResolver.SansSerif;
        var size = propertyValues["Size"] is float s ? s : 8.25f;
        var unit = propertyValues["Unit"] is GraphicsUnit u ? u : GraphicsUnit.Point;
        var style = FontStyle.Regular;

        if (propertyValues["Bold"] is true) style |= FontStyle.Bold;
        if (propertyValues["Italic"] is true) style |= FontStyle.Italic;
        if (propertyValues["Underline"] is true) style |= FontStyle.Underline;
        if (propertyValues["Strikeout"] is true) style |= FontStyle.Strikeout;

        return new Font(name, size, style, unit);
    }

    public override bool GetCreateInstanceSupported(ITypeDescriptorContext? context) => true;

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(typeof(Font), attributes)
            .Sort(["Name", "Size", "Unit", "Bold", "GdiCharSet", "GdiVerticalFont", "Italic", "Strikeout", "Underline"]);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

    private static FontStyle ParseStyle(string text)
    {
        var style = FontStyle.Regular;

        foreach (var name in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries))
        {
            style |= Enum.Parse<FontStyle>(name, ignoreCase: true);
        }

        return style;
    }

    private static (float Size, GraphicsUnit Unit) ParseSize(string text, CultureInfo culture)
    {
        var digits = text.TrimEnd();
        var unit = GraphicsUnit.Point;

        foreach (var (suffix, value) in Suffixes)
        {
            if (digits.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                unit = value;
                digits = digits[..^suffix.Length];
                break;
            }
        }

        return (float.Parse(digits.Trim(), NumberStyles.Float, culture), unit);
    }

    private static readonly (string Suffix, GraphicsUnit Unit)[] Suffixes =
    [
        ("world", GraphicsUnit.World),
        ("display", GraphicsUnit.Display),
        ("doc", GraphicsUnit.Document),
        ("pt", GraphicsUnit.Point),
        ("px", GraphicsUnit.Pixel),
        ("in", GraphicsUnit.Inch),
        ("mm", GraphicsUnit.Millimeter),
    ];

    private static string UnitSuffix(GraphicsUnit unit) => unit switch
    {
        GraphicsUnit.World => "world",
        GraphicsUnit.Display => "display",
        GraphicsUnit.Pixel => "px",
        GraphicsUnit.Inch => "in",
        GraphicsUnit.Millimeter => "mm",
        GraphicsUnit.Document => "doc",
        _ => "pt",
    };

    /// <summary>The family names the property grid offers.</summary>
    public sealed class FontNameConverter : TypeConverter, IDisposable
    {
        public FontNameConverter()
        {
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
            sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        /// <summary>A name as given, matched to an installed family's spelling when there is one.</summary>
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is not string name) return base.ConvertFrom(context, culture, value);

            return FontResolver.InstalledFamilies.FirstOrDefault(family => string.Equals(family, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(FontResolver.InstalledFamilies.ToArray());

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        void IDisposable.Dispose()
        {
        }
    }

    /// <summary>The units a font can be measured in: all but Display.</summary>
    public class FontUnitConverter : EnumConverter
    {
        public FontUnitConverter()
            : base(typeof(GraphicsUnit))
        {
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
            new(Enum.GetValues<GraphicsUnit>().Where(unit => unit != GraphicsUnit.Display).ToArray());
    }
}
