using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Includes TreeView's inherited image and explicit no-image choices.</summary>
public class TreeViewImageIndexConverter : ImageIndexConverter
{
    protected override bool IncludeNoneAsStandardValue => false;

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text)
        {
            if (string.Equals(text.Trim(), "(default)", StringComparison.OrdinalIgnoreCase)) return -1;
            if (string.Equals(text.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)) return -2;
        }

        return base.ConvertFrom(context, culture, value);
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is int index)
        {
            if (index == -1) return "(default)";
            if (index == -2) return "(none)";
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var values = base.GetStandardValues(context).Cast<object>().ToList();
        values.Add(-1);
        values.Add(-2);
        return new StandardValuesCollection(values);
    }
}
