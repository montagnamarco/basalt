using System.Collections;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Basalt.Designer;

public enum PropertyEditorKind
{
    Text,
    Number,
    Boolean,
    Enumeration,
    Brush,

    /// <summary>Four numbers, as Margin and Padding are written.</summary>
    Thickness,
}

/// <summary>
/// The group a property is shown under.
/// </summary>
/// <remarks>
/// A value rather than a string: the categories are a fixed set, they decide
/// the order of the groups, and they have to be translated. Kept as text they
/// were Italian words inside English code, compared by spelling, and sorted by
/// a switch that had to repeat every one of them.
/// </remarks>
public enum PropertyCategory
{
    Common,
    Layout,
    Appearance,
    Behavior,
}

/// <summary>Editable property of a control selected in the designer.</summary>
public sealed record DesignableProperty(
    string Name,
    Type ValueType,
    PropertyEditorKind EditorKind,
    PropertyCategory Category,
    IReadOnlyList<string> AllowedValues,
    string? CurrentValue)
{
    /// <summary>
    /// What the property is for, in one line, or null when nothing is known.
    /// </summary>
    /// <remarks>
    /// Visual Studio shows this under the grid, and it is most of what makes
    /// an unfamiliar property usable without leaving the designer.
    /// </remarks>
    public string? Description { get; init; }

    /// <summary>
    /// The value the control has when the property is not set.
    /// </summary>
    public string? DefaultValue { get; init; }

    /// <summary>
    /// Whether the template sets this property rather than leaving it alone.
    /// </summary>
    /// <remarks>
    /// A set value is shown in bold and can be reset, as in Visual Studio:
    /// without the distinction there is no way to tell what the file actually
    /// says from what the control happens to default to.
    /// </remarks>
    public bool IsSet => CurrentValue is not null;
}

/// <summary>
/// Discovers by reflection the properties settable on an Avalonia control.
///
/// The registered AvaloniaProperty entries are queried instead of maintaining a
/// fixed list: this way the inspector also works with the user's custom controls
/// and stays correct when Avalonia is updated.
/// </summary>
public static class PropertyInspector
{
    /// <summary>
    /// Properties that the designer handles with dedicated tools, or that make
    /// no sense at design time.
    /// </summary>
    private static readonly HashSet<string> Hidden =
    [
        "Name", "DataContext", "Parent", "TemplatedParent", "Bounds",
        "IsInitialized", "IsLoaded", "Content", "Child", "Children", "Items"
    ];

    private static readonly HashSet<string> LayoutProperties =
    [
        "Width", "Height", "MinWidth", "MinHeight", "MaxWidth", "MaxHeight",
        "Margin", "Padding", "HorizontalAlignment", "VerticalAlignment",
        "Grid.Row", "Grid.Column", "Grid.RowSpan", "Grid.ColumnSpan",
        "DockPanel.Dock", "Canvas.Left", "Canvas.Top"
    ];

    private static readonly HashSet<string> AppearanceProperties =
    [
        "Background", "Foreground", "BorderBrush", "BorderThickness",
        "FontFamily", "FontSize", "FontWeight", "FontStyle", "Opacity",
        "CornerRadius", "BoxShadow"
    ];

    /// <summary>Lists the designable properties of a control type.</summary>
    public static IReadOnlyList<DesignableProperty> Describe(Type controlType, Func<string, string?>? currentValue = null)
    {
        var results = new List<DesignableProperty>();

        foreach (var property in AvaloniaPropertyRegistry.Instance.GetRegistered(controlType))
        {
            if (property.IsReadOnly || Hidden.Contains(property.Name)) continue;

            var valueType = property.PropertyType;
            var kind = ClassifyEditor(valueType);
            if (kind is null) continue;

            results.Add(new DesignableProperty(
                property.Name,
                valueType,
                kind.Value,
                Categorize(property.Name),
                kind == PropertyEditorKind.Enumeration ? Enum.GetNames(UnwrapNullable(valueType)) : [],
                currentValue?.Invoke(property.Name))
            {
                Description = DescriptionOf(property.Name, valueType),
                DefaultValue = DefaultOf(property),
            });
        }

        return results
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The value a control has when the property is not set.
    /// </summary>
    /// <remarks>
    /// Read from the registration rather than from an instance: constructing
    /// one control per property to read its defaults is slow enough to be felt
    /// while clicking around the designer, and some controls do real work in
    /// their constructor.
    /// </remarks>
    private static string? DefaultOf(AvaloniaProperty property)
    {
        try
        {
            var metadata = property.GetMetadata(property.OwnerType);

            return metadata is IStyledPropertyMetadata styled
                ? styled.DefaultValue?.ToString()
                : null;
        }
        catch (Exception)
        {
            // A property whose default cannot be read is not worth failing the
            // whole panel for: it simply shows no default.
            return null;
        }
    }

    /// <summary>
    /// One line saying what a property is for.
    /// </summary>
    /// <remarks>
    /// Avalonia carries no descriptions in its metadata and ships no XML
    /// documentation to read them from, so the ones worth explaining are
    /// listed. The type is the fallback: knowing that Tag takes an Object is
    /// still more than knowing nothing.
    /// </remarks>
    private static string DescriptionOf(string name, Type valueType) =>
        Descriptions.TryGetValue(name, out var text) ? text : Readable(valueType);

    private static readonly Dictionary<string, string> Descriptions = new(StringComparer.Ordinal)
    {
        ["Width"] = "How wide the control is. Leave it unset to size to content.",
        ["Height"] = "How tall the control is. Leave it unset to size to content.",
        ["Margin"] = "Space outside the control, as left,top,right,bottom.",
        ["Padding"] = "Space between the control's edge and its content.",
        ["HorizontalAlignment"] = "Where the control sits across its parent.",
        ["VerticalAlignment"] = "Where the control sits down its parent.",
        ["Background"] = "The brush painted behind the content.",
        ["Foreground"] = "The brush the text is drawn in.",
        ["BorderBrush"] = "The brush the border is drawn in.",
        ["BorderThickness"] = "How thick the border is, as left,top,right,bottom.",
        ["CornerRadius"] = "How rounded the corners are.",
        ["FontFamily"] = "The typeface the text is drawn in.",
        ["FontSize"] = "The size of the text, in device-independent pixels.",
        ["FontWeight"] = "How heavy the text is drawn.",
        ["Opacity"] = "How opaque the control is, from 0 to 1.",
        ["IsEnabled"] = "Whether the control responds to the user.",
        ["IsVisible"] = "Whether the control is drawn and takes up space.",
        ["Text"] = "The text shown.",
        ["ToolTip.Tip"] = "What is shown when the pointer rests on the control.",
        ["Tag"] = "A value of your own, carried by the control and otherwise unused.",
        ["ZIndex"] = "Which control is drawn on top when two overlap.",
    };

    /// <summary>A type name as a person would read it.</summary>
    private static string Readable(Type type)
    {
        var bare = UnwrapNullable(type);

        return bare.IsEnum ? $"One of the {bare.Name} values." : $"A value of type {bare.Name}.";
    }

    /// <summary>
    /// Picks the editor suited to the type. Returns null for types the designer
    /// cannot edit textually, for example collections.
    /// </summary>
    private static PropertyEditorKind? ClassifyEditor(Type type)
    {
        var actual = UnwrapNullable(type);

        if (actual == typeof(bool)) return PropertyEditorKind.Boolean;
        if (actual.IsEnum) return PropertyEditorKind.Enumeration;
        if (actual == typeof(string)) return PropertyEditorKind.Text;

        if (actual == typeof(double) || actual == typeof(float) ||
            actual == typeof(int) || actual == typeof(long) || actual == typeof(decimal))
            return PropertyEditorKind.Number;

        if (typeof(IBrush).IsAssignableFrom(actual) || actual == typeof(Color))
            return PropertyEditorKind.Brush;

        // Four numbers, which are worth four fields: "4,2,4,2" is easy to get
        // wrong by hand and gives no clue which number is which side.
        if (actual == typeof(Thickness)) return PropertyEditorKind.Thickness;

        // CornerRadius, GridLength and the like have a canonical textual form
        // in XAML ("8", "4,2,4,2"): they are edited as text.
        if (actual.IsValueType && HasXamlTextForm(actual)) return PropertyEditorKind.Text;

        // Collections and complex objects require dedicated editors.
        return null;
    }

    private static bool HasXamlTextForm(Type type) =>
        type == typeof(Thickness) || type == typeof(CornerRadius) ||
        type == typeof(GridLength) || type == typeof(Point) ||
        type == typeof(Size) || type == typeof(Rect) ||
        type == typeof(TimeSpan) || type == typeof(DateTime);

    private static Type UnwrapNullable(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static PropertyCategory Categorize(string name) =>
        LayoutProperties.Contains(name) ? PropertyCategory.Layout
        : AppearanceProperties.Contains(name) ? PropertyCategory.Appearance
        : name.StartsWith("Is", StringComparison.Ordinal) ? PropertyCategory.Behavior
        : PropertyCategory.Common;
}
