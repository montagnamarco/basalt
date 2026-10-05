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
        }

        return rows;
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
            _ => throw new NotSupportedException("This property has no built-in child editor."),
        };
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
