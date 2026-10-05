using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Shows the keys held by the image list associated with a property.</summary>
public class ImageKeyConverter : StringConverter
{
    protected virtual bool IncludeNoneAsStandardValue => true;

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text
            && (string.Equals(text.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text.Trim(), "(default)", StringComparison.OrdinalIgnoreCase)))
        {
            return string.Empty;
        }
        return base.ConvertFrom(context, culture, value)!;
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && (value is null || value is string { Length: 0 })) return "(none)";
        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var values = ImageIndexConverter.ImageListFromContext(context)?.Images.Keys.Cast<string>().Cast<object>().ToList() ?? [];
        if (IncludeNoneAsStandardValue) values.Add(string.Empty);
        return new StandardValuesCollection(values);
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
}
