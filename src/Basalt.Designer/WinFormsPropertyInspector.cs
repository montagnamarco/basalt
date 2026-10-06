using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Reflection;

namespace Basalt.Designer;

/// <summary>Reads the component metadata used by Windows Forms and custom controls.</summary>
public static class WinFormsPropertyInspector
{
    public static IReadOnlyList<DesignableProperty> Describe(
        Type componentType, Func<string, string?>? currentValue = null) =>
        DescribeProperties(TypeDescriptor.GetProperties(componentType), currentValue);

    public static IReadOnlyList<DesignableProperty> Describe(
        object component, Func<string, string?>? currentValue = null) =>
        DescribeProperties(TypeDescriptor.GetProperties(component), currentValue);

    /// <summary>
    /// Metadata for the Forms designer, including browsable values that cannot be edited.
    /// The editor uses only known value types here; it does not run application converters.
    /// </summary>
    public static IReadOnlyList<DesignableProperty> DescribeDesigner(
        object component, Func<string, string?>? currentValue = null) =>
        DescribeDesignerProperties(TypeDescriptor.GetProperties(component), currentValue, component.GetType());

    public static string? ReadDesignerValue(object component, string propertyName)
    {
        var descriptor = Find(component, propertyName);
        if (component.GetType().FullName == "System.IO.Ports.SerialPort")
        {
            // Closed serial ports expose state-dependent getters such as BaseStream.
            // The designer must not open hardware just to fill a property row.
            try { return SafeFormat(descriptor.GetValue(component)); }
            catch (InvalidOperationException) { return null; }
            catch (TargetInvocationException exception) when (exception.InnerException is InvalidOperationException)
            { return null; }
        }

        return SafeFormat(descriptor.GetValue(component));
    }

    private static IReadOnlyList<DesignableProperty> DescribeDesignerProperties(
        PropertyDescriptorCollection descriptors, Func<string, string?>? currentValue, Type componentType)
    {
        var properties = new List<DesignableProperty>();
        foreach (PropertyDescriptor descriptor in descriptors)
        {
            if (!descriptor.IsBrowsable || descriptor.SerializationVisibility == DesignerSerializationVisibility.Hidden)
                continue;

            var type = Nullable.GetUnderlyingType(descriptor.PropertyType) ?? descriptor.PropertyType;
            var editableCollection = CanEditCollection(descriptor, componentType);
            var collection = editableCollection || type != typeof(string) && typeof(IList).IsAssignableFrom(type);
            var componentReference = typeof(IComponent).IsAssignableFrom(type);
            var expandable = IsExpandable(type);
            var image = IsImageType(type);
            var kind = BuiltInEditorKind(type);
            var readOnly = descriptor.IsReadOnly && !editableCollection
                || kind is null && !expandable && !componentReference && !editableCollection && !image;
            var defaultValue = descriptor.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;

            properties.Add(new DesignableProperty(
                descriptor.Name,
                descriptor.PropertyType,
                kind ?? PropertyEditorKind.Text,
                Category(descriptor.Category),
                type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false)
                    ? Enum.GetNames(type)
                    : [],
                currentValue?.Invoke(descriptor.Name))
            {
                CategoryName = descriptor.Category,
                DisplayName = descriptor.DisplayName,
                Description = string.IsNullOrEmpty(descriptor.Description) ? null : descriptor.Description,
                DefaultValue = defaultValue is null ? null : SafeFormat(defaultValue.Value),
                IsReadOnly = readOnly,
                IsExpandable = expandable,
                IsCollection = collection,
                IsComponentReference = componentReference,
                HasCustomEditor = type.FullName is "System.Drawing.Font" or "System.Drawing.Color"
                    || editableCollection || image,
            });
        }

        return properties.OrderBy(property => property.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool CanEditCollection(PropertyDescriptor descriptor, Type componentType)
    {
        if (descriptor.Name == "Columns")
        {
            return descriptor.ComponentType.FullName == "System.Windows.Forms.DataGridView";
        }

        return descriptor.Name == "Items"
            && (descriptor.ComponentType.FullName is "System.Windows.Forms.ListBox" or "System.Windows.Forms.ComboBox"
                || descriptor.ComponentType.FullName == "System.Windows.Forms.ToolStrip"
                    && componentType.FullName == "System.Windows.Forms.MenuStrip");
    }

    private static bool IsExpandable(Type type) => type.FullName is
        "System.Drawing.Point" or "System.Drawing.Size" or "System.Windows.Forms.Padding"
        or "System.Drawing.Font" or "System.Drawing.Color";

    /// <summary>
    /// Image, BackgroundImage, InitialImage, ErrorImage and Icon: picture properties
    /// the designer edits through the "Select Resource" dialog rather than text.
    /// </summary>
    private static bool IsImageType(Type type) => type.FullName is
        "System.Drawing.Image" or "System.Drawing.Bitmap" or "System.Drawing.Icon";

    private static PropertyEditorKind? BuiltInEditorKind(Type type)
    {
        if (type == typeof(bool)) return PropertyEditorKind.Boolean;
        if (type.IsEnum) return type.IsDefined(typeof(FlagsAttribute), false)
            ? PropertyEditorKind.Text : PropertyEditorKind.Enumeration;
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal)) return PropertyEditorKind.Number;
        return type == typeof(string) || IsExpandable(type) ? PropertyEditorKind.Text : null;
    }

    private static string? SafeFormat(object? value) => value switch
    {
        null => null,
        string text => text,
        bool boolean => boolean ? "True" : "False",
        Enum enumeration => enumeration.ToString(),
        IFormattable number when value is byte or sbyte or short or ushort or int or uint
            or long or ulong or float or double or decimal => number.ToString(null, CultureInfo.InvariantCulture),
        Point point => $"{point.X}, {point.Y}",
        Size size => $"{size.Width}, {size.Height}",
        _ when value.GetType().FullName == "System.Windows.Forms.Padding" => FormatPadding(value),
        // These exact framework types have built-in invariant converters. A component's
        // own converter is never invoked to format an unfamiliar application value.
        _ when value.GetType().FullName is "System.Drawing.Font" or "System.Drawing.Color" =>
            TypeDescriptor.GetConverter(value.GetType()).ConvertToInvariantString(value),
        // An Image's own converter would decode and re-encode the picture just to
        // name it; the type is enough to show something next to the thumbnail.
        _ when IsImageType(value.GetType()) => value.GetType().Name,
        _ => null,
    };

    private static string FormatPadding(object padding)
    {
        var type = padding.GetType();
        var sides = new[] { "Left", "Top", "Right", "Bottom" };
        var values = sides.Select(side =>
            Convert.ToString(type.GetProperty(side)!.GetValue(padding), CultureInfo.InvariantCulture));
        return string.Join(", ", values);
    }

    private static IReadOnlyList<DesignableProperty> DescribeProperties(
        PropertyDescriptorCollection descriptors, Func<string, string?>? currentValue)
    {
        var properties = new List<DesignableProperty>();
        foreach (PropertyDescriptor descriptor in descriptors)
        {
            if (!descriptor.IsBrowsable || descriptor.IsReadOnly ||
                descriptor.SerializationVisibility == DesignerSerializationVisibility.Hidden)
                continue;

            var type = Nullable.GetUnderlyingType(descriptor.PropertyType) ?? descriptor.PropertyType;
            var kind = EditorKind(type, descriptor.Converter);
            if (kind is null)
                continue;

            var defaultValue = descriptor.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;
            properties.Add(new DesignableProperty(
                descriptor.Name, descriptor.PropertyType, kind.Value, Category(descriptor.Category),
                type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) ? Enum.GetNames(type) : [],
                currentValue?.Invoke(descriptor.Name))
            {
                Description = string.IsNullOrEmpty(descriptor.Description) ? null : descriptor.Description,
                DefaultValue = defaultValue is null ? null : Format(descriptor, defaultValue.Value),
            });
        }

        return properties.OrderBy(property => property.Category)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>Formats through the property's converter, including custom component metadata.</summary>
    public static string? ReadValue(object component, string propertyName)
    {
        var descriptor = Find(component, propertyName);
        return Format(descriptor, descriptor.GetValue(component));
    }

    /// <summary>Converts before assigning so malformed input leaves the component unchanged.</summary>
    public static void SetValue(object component, string propertyName, string value)
    {
        var descriptor = Find(component, propertyName);
        if (descriptor.IsReadOnly)
            throw new InvalidOperationException($"Property '{propertyName}' is read-only.");

        var converted = descriptor.Converter.ConvertFrom(null, CultureInfo.InvariantCulture, value);
        descriptor.SetValue(component, converted);
    }

    /// <summary>Uses Reset methods and default attributes provided by the component.</summary>
    public static bool ResetValue(object component, string propertyName)
    {
        var descriptor = Find(component, propertyName);
        if (descriptor.IsReadOnly || !descriptor.CanResetValue(component))
            return false;

        descriptor.ResetValue(component);
        return true;
    }

    private static PropertyDescriptor Find(object component, string propertyName) =>
        TypeDescriptor.GetProperties(component)[propertyName] ??
        throw new ArgumentException($"Unknown property '{propertyName}'.", nameof(propertyName));

    private static string? Format(PropertyDescriptor descriptor, object? value) =>
        value is null ? null : descriptor.Converter.ConvertToInvariantString(value);

    private static PropertyEditorKind? EditorKind(Type type, TypeConverter converter)
    {
        if (type == typeof(bool)) return PropertyEditorKind.Boolean;
        // Flags accept combined values; a single-choice dropdown loses them.
        if (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false)) return PropertyEditorKind.Enumeration;
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal)) return PropertyEditorKind.Number;

        return converter.CanConvertFrom(typeof(string)) && converter.CanConvertTo(typeof(string))
            ? PropertyEditorKind.Text : null;
    }

    private static PropertyCategory Category(string category) => category switch
    {
        "Layout" => PropertyCategory.Layout,
        "Appearance" => PropertyCategory.Appearance,
        "Behavior" or "Focus" => PropertyCategory.Behavior,
        _ => PropertyCategory.Common,
    };
}
