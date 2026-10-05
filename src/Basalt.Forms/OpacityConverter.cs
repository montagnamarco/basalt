using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Converts the Form.Opacity fraction to and from percentages in a property grid.</summary>
public class OpacityConverter : TypeConverter
{
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || sourceType == typeof(double) || base.CanConvertFrom(context, sourceType);

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is double opacity) return opacity;
        if (value is string text)
        {
            culture ??= CultureInfo.CurrentCulture;
            var trimmed = text.Trim();
            if (trimmed.EndsWith('%'))
                trimmed = trimmed[..^1].Trim();

            // WinForms' OpacityConverter always treats the number as a percentage,
            // with or without a trailing '%': "50" and "50%" both mean 0.5.
            var percent = double.Parse(trimmed, NumberStyles.Float, culture);
            if (percent < 0 || percent > 100)
                throw new ArgumentException($"'{text}' is not a valid value for Opacity. Opacity must be between 0% and 100%.");

            return percent / 100d;
        }

        return base.ConvertFrom(context, culture, value)!;
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture,
        object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is double opacity)
        {
            // WinForms shows whole percent only: the value is truncated, not rounded or given decimals.
            var percent = (int)(opacity * 100);
            return percent.ToString(culture ?? CultureInfo.CurrentCulture) + "%";
        }
        return base.ConvertTo(context, culture, value, destinationType)!;
    }
}
