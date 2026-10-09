using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using Basalt.Designer;
using Forms = System.Windows.Forms;

namespace Basalt.Shell.Controls;

/// <summary>Built-in value children for the Forms grid; edits rebuild a value before source changes.</summary>
internal static class WinFormsExpandableProperties
{
    public static IReadOnlyList<DesignableProperty> Children(object component, string parentName, bool isExplicit = false)
    {
        var descriptor = TypeDescriptor.GetProperties(component)[parentName]
            ?? throw new ArgumentException("The property does not exist.", nameof(parentName));
        var value = descriptor.GetValue(component);
        var rows = new List<DesignableProperty>();

        void Add(string name, object child, PropertyEditorKind kind = PropertyEditorKind.Number,
            IReadOnlyList<string>? choices = null)
        {
            var text = Format(child);
            rows.Add(new DesignableProperty(name, child.GetType(), kind,
                Category(descriptor.Category), choices ?? [], isExplicit ? text : null)
            {
                ParentName = parentName,
                Depth = 1,
                CategoryName = descriptor.Category,
                DefaultValue = text,
                IsReadOnly = descriptor.IsReadOnly,
                WasSet = isExplicit,
            });
        }

        switch (value)
        {
            case Point point:
                Add(nameof(Point.X), point.X);
                Add(nameof(Point.Y), point.Y);
                break;
            case Size size:
                Add(nameof(Size.Width), size.Width);
                Add(nameof(Size.Height), size.Height);
                break;
            case Forms.Padding padding:
                Add(nameof(Forms.Padding.Left), padding.Left);
                Add(nameof(Forms.Padding.Top), padding.Top);
                Add(nameof(Forms.Padding.Right), padding.Right);
                Add(nameof(Forms.Padding.Bottom), padding.Bottom);
                break;
            case Font font:
                Add(nameof(Font.Name), font.Name, PropertyEditorKind.Text);
                Add(nameof(Font.Size), font.Size);
                Add(nameof(Font.Style), font.Style, PropertyEditorKind.Text);
                Add(nameof(Font.Bold), font.Bold, PropertyEditorKind.Boolean);
                Add(nameof(Font.Italic), font.Italic, PropertyEditorKind.Boolean);
                Add(nameof(Font.Underline), font.Underline, PropertyEditorKind.Boolean);
                Add(nameof(Font.Strikeout), font.Strikeout, PropertyEditorKind.Boolean);
                Add(nameof(Font.Unit), font.Unit, PropertyEditorKind.Enumeration,
                    Enum.GetNames<GraphicsUnit>());
                break;
            case Color color:
                Add(nameof(Color.A), color.A);
                Add(nameof(Color.R), color.R);
                Add(nameof(Color.G), color.G);
                Add(nameof(Color.B), color.B);
                break;

            default:
                // Any other expandable value: Point, Size, Padding, Font and Color are
                // listed by hand above because each rebuilds its own constructor
                // arguments, but a type with the same TypeConverter contract - override
                // GetPropertiesSupported, list its own members - gets the same rows
                // generically, the way Visual Studio's PropertyGrid does for any
                // ExpandableObjectConverter.
                if (value is not null)
                    foreach (var child in GenericChildren(descriptor, value, parentName, isExplicit))
                        rows.Add(child);
                break;
        }

        return rows;
    }

    private static IEnumerable<DesignableProperty> GenericChildren(
        PropertyDescriptor parent, object value, string parentName, bool isExplicit)
    {
        // The parent descriptor's own Converter, not TypeDescriptor.GetConverter(value):
        // a TypeConverterAttribute can sit on the property alone, overriding the value
        // type's own default converter for this one property only, and only the
        // descriptor that was actually asked for sees that override.
        var converter = parent.Converter;
        if (!converter.GetPropertiesSupported())
            yield break;

        // Rebuild only reconstructs a new value when the converter also supports
        // CreateInstance (the pattern FontConverter and PointConverter use for their own
        // immutable values). Without it, the designer has no safe way to write a changed
        // member back, so the row is shown but not editable rather than failing on commit.
        var canRebuild = converter.GetCreateInstanceSupported();
        var properties = converter.GetProperties(value);
        if (properties is null) yield break;

        foreach (PropertyDescriptor child in properties)
        {
            if (!child.IsBrowsable) continue;

            // A null member (a nullable reference such as a string? property) keeps its row, with
            // no value text, the way the panel shows a null property. Dropping it would hide a
            // member the user can still set from the top-level grid.
            // A null member is not "set" even when the parent is explicit: Reset of a child rebuilds
            // from the default instance's child, which is null too, so SetNestedPropertyAsync would
            // throw "The child has no default value." Offering Reset here would offer a failing command.
            var childValue = child.GetValue(value);
            var text = childValue is null ? null : Format(childValue);
            yield return new DesignableProperty(child.Name, child.PropertyType, GenericChildKind(child.PropertyType),
                Category(parent.Category),
                child.PropertyType.IsEnum && !child.PropertyType.IsDefined(typeof(FlagsAttribute), false)
                    ? Enum.GetNames(child.PropertyType) : [],
                isExplicit ? text : null)
            {
                ParentName = parentName,
                Depth = 1,
                CategoryName = parent.Category,
                DefaultValue = text,
                IsReadOnly = parent.IsReadOnly || child.IsReadOnly || !canRebuild,
                WasSet = isExplicit && childValue is not null,
            };
        }
    }

    private static PropertyEditorKind GenericChildKind(Type type)
    {
        var actual = Nullable.GetUnderlyingType(type) ?? type;

        if (actual == typeof(bool)) return PropertyEditorKind.Boolean;
        if (actual.IsEnum) return PropertyEditorKind.Enumeration;
        if (actual == typeof(byte) || actual == typeof(sbyte) || actual == typeof(short) ||
            actual == typeof(ushort) || actual == typeof(int) || actual == typeof(uint) ||
            actual == typeof(long) || actual == typeof(ulong) || actual == typeof(float) ||
            actual == typeof(double) || actual == typeof(decimal)) return PropertyEditorKind.Number;

        return PropertyEditorKind.Text;
    }

    /// <summary>Returns a new value. The caller serializes it through one undoable parent assignment.</summary>
    public static object Rebuild(object component, string parentName, string childName, string text)
    {
        var descriptor = TypeDescriptor.GetProperties(component)[parentName]
            ?? throw new ArgumentException("The property does not exist.", nameof(parentName));
        if (descriptor.IsReadOnly)
        {
            throw new InvalidOperationException("The parent property is read-only.");
        }

        var value = descriptor.GetValue(component)
            ?? throw new InvalidOperationException("The parent property has no value.");

        return value switch
        {
            Point point => RebuildPoint(point, childName, text),
            Size size => RebuildSize(size, childName, text),
            Forms.Padding padding => RebuildPadding(padding, childName, text),
            Font font => RebuildFont(font, childName, text),
            Color color => RebuildColor(color, childName, text),
            _ => GenericRebuild(descriptor.Converter, value, childName, text),
        };
    }

    /// <summary>
    /// Rebuilds any other expandable value through its own TypeConverter, the same
    /// CreateInstance contract FontConverter and PointConverter use: read every member's
    /// current value, replace the one that changed, and let the converter reconstruct a
    /// fresh instance. <see cref="GenericChildren"/> never lets this run for a converter
    /// that does not support CreateInstance, so reaching here with one is a program error.
    /// The parent property's own descriptor.Converter is passed in rather than
    /// re-derived from the bare value, for the same reason <see cref="GenericChildren"/>
    /// does: a TypeConverterAttribute can override the converter for this property alone.
    /// </summary>
    private static object GenericRebuild(TypeConverter converter, object value, string childName, string text)
    {
        if (!converter.GetPropertiesSupported() || !converter.GetCreateInstanceSupported())
            throw new NotSupportedException("This property has no built-in child editor.");

        var properties = converter.GetProperties(value) ?? throw UnknownChild(childName);
        var target = properties[childName] ?? throw UnknownChild(childName);
        var newValue = target.Converter.ConvertFromInvariantString(text);

        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (PropertyDescriptor property in properties)
            values[property.Name] = property.Name == childName ? newValue : property.GetValue(value);

        return converter.CreateInstance(null, values)
            ?? throw new InvalidOperationException("The converter did not rebuild a value.");
    }

    private static Point RebuildPoint(Point point, string childName, string text) => childName switch
    {
        nameof(Point.X) => new Point(ParseInteger(text), point.Y),
        nameof(Point.Y) => new Point(point.X, ParseInteger(text)),
        _ => throw UnknownChild(childName),
    };

    private static Size RebuildSize(Size size, string childName, string text) => childName switch
    {
        nameof(Size.Width) => new Size(ParseInteger(text), size.Height),
        nameof(Size.Height) => new Size(size.Width, ParseInteger(text)),
        _ => throw UnknownChild(childName),
    };

    private static Forms.Padding RebuildPadding(Forms.Padding padding, string childName, string text) => childName switch
    {
        nameof(Forms.Padding.Left) => new Forms.Padding(
            ParseInteger(text), padding.Top, padding.Right, padding.Bottom),
        nameof(Forms.Padding.Top) => new Forms.Padding(
            padding.Left, ParseInteger(text), padding.Right, padding.Bottom),
        nameof(Forms.Padding.Right) => new Forms.Padding(
            padding.Left, padding.Top, ParseInteger(text), padding.Bottom),
        nameof(Forms.Padding.Bottom) => new Forms.Padding(
            padding.Left, padding.Top, padding.Right, ParseInteger(text)),
        _ => throw UnknownChild(childName),
    };

    private static Font RebuildFont(Font font, string childName, string text)
    {
        var name = font.Name;
        var size = font.Size;
        var style = font.Style;
        var unit = font.Unit;

        switch (childName)
        {
            case nameof(Font.Name):
                name = text;
                break;
            case nameof(Font.Size):
                size = float.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                break;
            case nameof(Font.Style):
                style = ParseEnum<FontStyle>(text);
                break;
            case nameof(Font.Bold):
                style = Toggle(style, FontStyle.Bold, text);
                break;
            case nameof(Font.Italic):
                style = Toggle(style, FontStyle.Italic, text);
                break;
            case nameof(Font.Underline):
                style = Toggle(style, FontStyle.Underline, text);
                break;
            case nameof(Font.Strikeout):
                style = Toggle(style, FontStyle.Strikeout, text);
                break;
            case nameof(Font.Unit):
                unit = ParseEnum<GraphicsUnit>(text);
                break;
            default:
                throw UnknownChild(childName);
        }

        return new Font(name, size, style, unit, font.GdiCharSet, font.GdiVerticalFont);
    }

    private static Color RebuildColor(Color color, string childName, string text)
    {
        var channel = byte.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        return childName switch
        {
            nameof(Color.A) => Color.FromArgb(channel, color.R, color.G, color.B),
            nameof(Color.R) => Color.FromArgb(color.A, channel, color.G, color.B),
            nameof(Color.G) => Color.FromArgb(color.A, color.R, channel, color.B),
            nameof(Color.B) => Color.FromArgb(color.A, color.R, color.G, channel),
            _ => throw UnknownChild(childName),
        };
    }

    private static int ParseInteger(string text) =>
        int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);

    private static FontStyle Toggle(FontStyle style, FontStyle flag, string text) =>
        bool.Parse(text) ? style | flag : style & ~flag;

    private static T ParseEnum<T>(string text) where T : struct, Enum =>
        Enum.TryParse<T>(text, true, out var result) && (typeof(T) == typeof(FontStyle) || Enum.IsDefined(result))
            ? result : throw new FormatException("The value is not a supported choice.");

    private static string Format(object value) => value switch
    {
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static PropertyCategory Category(string category) => category switch
    {
        "Layout" => PropertyCategory.Layout,
        "Appearance" => PropertyCategory.Appearance,
        "Behavior" or "Focus" => PropertyCategory.Behavior,
        _ => PropertyCategory.Common,
    };

    private static ArgumentException UnknownChild(string childName) =>
        new("The child property does not exist.", nameof(childName));
}
