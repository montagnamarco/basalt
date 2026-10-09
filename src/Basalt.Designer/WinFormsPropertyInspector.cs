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
            var image = IsImageType(type);
            // A picture keeps the single row with its Select Resource button: ImageConverter
            // and IconConverter report GetPropertiesSupported, and the generic expandable
            // row they would get instead broke the "…" click (measured by
            // WinFormsPropertyGridEllipsisPointerTests).
            var expandable = !image && IsExpandable(descriptor, type);
            var hasEditor = HasDesignTimeEditor(descriptor);
            var kind = BuiltInEditorKind(descriptor, type);
            string[]? allowedValues = null;

            if (kind is null or PropertyEditorKind.Text)
            {
                // TypeConverter.GetStandardValues gives the designer a closed set to
                // show as a drop-down even when the value is not a real enum - a font
                // name, a culture, or any other application choice list. Only an
                // exclusive set becomes one: a non-exclusive set is suggestions over
                // free text, and this grid's drop-down editor cannot mix the two.
                allowedValues = StandardValueChoices(descriptor);
                if (allowedValues is not null) kind = PropertyEditorKind.Enumeration;
            }

            var readOnly = descriptor.IsReadOnly && !editableCollection
                || kind is null && !componentReference && !editableCollection && !image && !hasEditor;
            var defaultValue = descriptor.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;

            properties.Add(new DesignableProperty(
                descriptor.Name,
                descriptor.PropertyType,
                kind ?? PropertyEditorKind.Text,
                Category(descriptor.Category),
                allowedValues ?? (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false)
                    ? Enum.GetNames(type)
                    : []),
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
                    || editableCollection || image || hasEditor,
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

    /// <summary>
    /// Point, Size, Padding, Font and Color are always expandable: Basalt reimplements
    /// their real .NET converters, which all override TypeConverter.GetPropertiesSupported.
    /// Any other property gets the same row generically when its own resolved converter
    /// does too - an application's struct with an ExpandableObjectConverter-derived one,
    /// whether declared on the type or, like <see cref="TypeConverterAttribute"/> allows,
    /// only on this one property. That is why the check reads descriptor.Converter rather
    /// than re-deriving a converter from the bare type: only the descriptor knows about a
    /// property-level override.
    /// </summary>
    private static bool IsExpandable(PropertyDescriptor descriptor, Type type) => type.FullName is
        "System.Drawing.Point" or "System.Drawing.Size" or "System.Windows.Forms.Padding"
        or "System.Drawing.Font" or "System.Drawing.Color" || HasExpandableConverter(descriptor.Converter);

    private static bool HasExpandableConverter(TypeConverter converter)
    {
        try { return converter.GetPropertiesSupported(); }
        catch
        {
            // A converter that fails to report support is treated the same as one
            // that reports none: the property simply stays a single row.
            return false;
        }
    }

    /// <summary>
    /// Image, BackgroundImage, InitialImage, ErrorImage and Icon: picture properties
    /// the designer edits through the "Select Resource" dialog rather than text.
    /// </summary>
    private static bool IsImageType(Type type) => type.FullName is
        "System.Drawing.Image" or "System.Drawing.Bitmap" or "System.Drawing.Icon";

    private static PropertyEditorKind? BuiltInEditorKind(PropertyDescriptor descriptor, Type type)
    {
        if (type == typeof(bool)) return PropertyEditorKind.Boolean;
        if (type.IsEnum) return type.IsDefined(typeof(FlagsAttribute), false)
            ? PropertyEditorKind.Text : PropertyEditorKind.Enumeration;
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) ||
            type == typeof(ushort) || type == typeof(int) || type == typeof(uint) ||
            type == typeof(long) || type == typeof(ulong) || type == typeof(float) ||
            type == typeof(double) || type == typeof(decimal)) return PropertyEditorKind.Number;
        if (type == typeof(string)) return PropertyEditorKind.Text;

        // Font, Point, Size, Padding, Color and any custom value with the same shape -
        // an expandable TypeConverter that also round-trips a one-line string - show
        // that string at the top row, with their members underneath when expanded. One
        // that cannot convert a string is still expandable (IsExpandable, set by the
        // caller), just without a top-row text box: ConvertFromInvariantString would
        // have nothing to parse back into the object the sub-rows just edited.
        return IsExpandable(descriptor, type) && CanConvertToText(descriptor.Converter) ? PropertyEditorKind.Text : null;
    }

    private static bool CanConvertToText(TypeConverter converter)
    {
        try { return converter.CanConvertFrom(typeof(string)) && converter.CanConvertTo(typeof(string)); }
        catch { return false; }
    }

    /// <summary>
    /// Converter-driven choices for a property that is not a real enum: TypeConverter's
    /// GetStandardValues gives the designer a closed set to show as a drop-down, as a
    /// FontFamily name list or a culture list would. GetStandardValuesExclusive must also
    /// say the set is closed: otherwise the values are suggestions over free text, which
    /// this grid's drop-down editor has no way to offer alongside typing.
    /// </summary>
    private static string[]? StandardValueChoices(PropertyDescriptor descriptor)
    {
        try
        {
            var converter = descriptor.Converter;
            if (!converter.GetStandardValuesSupported() || !converter.GetStandardValuesExclusive())
                return null;

            var values = converter.GetStandardValues();
            if (values is null) return null;

            var choices = new List<string>();
            foreach (var value in values)
            {
                if (value is null) continue;
                choices.Add((CanConvertToText(converter) ? converter.ConvertToInvariantString(value) : value.ToString()) ?? "");
            }

            return choices.Count == 0 ? null : choices.ToArray();
        }
        catch
        {
            // A converter that throws computing its standard values leaves the
            // property a plain row instead of failing the whole panel.
            return null;
        }
    }

    /// <summary>
    /// Whether the property carries an <c>[Editor(..., typeof(UITypeEditor))]</c>
    /// attribute whose edit style is not None: Visual Studio shows a "..." button or a
    /// drop-down arrow for exactly this. PropertyDescriptor.GetEditor needs the actual
    /// UITypeEditor Type to look for, and Basalt.Designer has no compile-time reference
    /// to the assembly that defines it (the reimplemented System.Windows.Forms, which
    /// also carries System.Drawing.Design) - so the base type the attribute itself names
    /// is resolved from its own assembly-qualified name instead.
    /// </summary>
    private static bool HasDesignTimeEditor(PropertyDescriptor descriptor)
    {
        foreach (var attribute in descriptor.Attributes.OfType<EditorAttribute>())
        {
            try
            {
                if (attribute.EditorBaseTypeName is not { Length: > 0 } baseTypeName
                    || Type.GetType(baseTypeName) is not { FullName: "System.Drawing.Design.UITypeEditor" } baseType)
                    continue;

                var editor = descriptor.GetEditor(baseType);
                if (editor is null) continue;

                var style = baseType.GetMethod("GetEditStyle", Type.EmptyTypes)?.Invoke(editor, null)?.ToString();
                if (style is not null and not "None") return true;
            }
            catch
            {
                // An editor attribute naming a type that fails to load or construct
                // leaves the property without a "..." button instead of failing the
                // whole panel.
            }
        }

        return false;
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

            var allowedValues = kind == PropertyEditorKind.Text ? StandardValueChoices(descriptor) : null;
            if (allowedValues is not null) kind = PropertyEditorKind.Enumeration;

            var defaultValue = descriptor.Attributes[typeof(DefaultValueAttribute)] as DefaultValueAttribute;
            properties.Add(new DesignableProperty(
                descriptor.Name, descriptor.PropertyType, kind.Value, Category(descriptor.Category),
                allowedValues ?? (type.IsEnum && !type.IsDefined(typeof(FlagsAttribute), false) ? Enum.GetNames(type) : []),
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

    /// <summary>
    /// Uses Reset methods and default attributes provided by the component. For a user subclass of a
    /// built-in control (see <see cref="WinFormsPropertyResetMetadata.ShouldContainHookFailure"/>), an
    /// exception from its own CanResetX or ResetX is contained and reported as "not reset", matching
    /// <see cref="WinFormsPropertyResetMetadata.Describe"/> reporting the property as not resettable.
    /// An exception from a built-in Forms or SerialPort hook propagates, so a bug in Basalt's own
    /// controls stays visible.
    /// </summary>
    public static bool ResetValue(object component, string propertyName)
    {
        var descriptor = Find(component, propertyName);
        if (descriptor.IsReadOnly)
            return false;

        var containFailure = WinFormsPropertyResetMetadata.ShouldContainHookFailure(component, descriptor);
        try
        {
            if (!descriptor.CanResetValue(component))
                return false;

            descriptor.ResetValue(component);
            return true;
        }
        catch (Exception) when (containFailure)
        {
            return false;
        }
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
