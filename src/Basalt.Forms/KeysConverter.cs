using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>Converts shortcut keys for property grids and designer serialization.</summary>
public class KeysConverter : TypeConverter, IComparer
{
    private static readonly StandardValuesCollection s_standardValues = new(
        Enum.GetValues<Keys>()
            .Where(key => (key & Keys.Modifiers) == 0 && key != Keys.KeyCode && key != Keys.None)
            .Distinct()
            .Cast<object>()
            .ToArray());

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public int Compare(object? a, object? b) => StringComparer.CurrentCulture.Compare(
        ConvertToString(a), ConvertToString(b));

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is not string text) return base.ConvertFrom(context, culture, value)!;

        var result = Keys.None;
        var hasKeyCode = false;
        foreach (var part in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0) throw new FormatException("A shortcut key cannot contain an empty component.");

            var key = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => Keys.Control,
                "SHIFT" => Keys.Shift,
                "ALT" => Keys.Alt,
                "DEL" => Keys.Delete,
                "INS" => Keys.Insert,
                "ESC" => Keys.Escape,
                _ when Enum.TryParse<Keys>(part, true, out var parsed)
                    && Enum.IsDefined(parsed)
                    && parsed != Keys.KeyCode
                    && parsed != Keys.Modifiers => parsed,
                _ => throw new FormatException($"'{part}' is not a valid shortcut key."),
            };

            if ((key & Keys.Modifiers) == 0 && key != Keys.None)
            {
                if (hasKeyCode) throw new FormatException("A shortcut can contain only one key code.");
                hasKeyCode = true;
            }

            result |= key;
        }

        return result;
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is Keys keys) return Format(keys);
        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => s_standardValues;

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    private static string Format(Keys keys)
    {
        var parts = new List<string>(4);
        if ((keys & Keys.Control) != 0) parts.Add("Ctrl");
        if ((keys & Keys.Alt) != 0) parts.Add("Alt");
        if ((keys & Keys.Shift) != 0) parts.Add("Shift");

        var keyCode = keys & Keys.KeyCode;
        if (keyCode != Keys.None || parts.Count == 0) parts.Add(keyCode.ToString());
        return string.Join("+", parts);
    }
}
