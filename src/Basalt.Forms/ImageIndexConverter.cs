using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace System.Windows.Forms;

/// <summary>Shows image indices available from the image list associated with a property.</summary>
public class ImageIndexConverter : Int32Converter
{
    protected virtual bool IncludeNoneAsStandardValue => true;

    public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
    {
        if (value is string text && string.Equals(text.Trim(), "(none)", StringComparison.OrdinalIgnoreCase)) return -1;
        return base.ConvertFrom(context, culture, value)!;
    }

    public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);
        if (destinationType == typeof(string) && value is int index && index == -1) return "(none)";
        return base.ConvertTo(context, culture, value, destinationType)!;
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
    {
        var count = ImageListFromContext(context)?.Images.Count ?? 0;
        var values = Enumerable.Range(0, count).Cast<object>().ToList();
        if (IncludeNoneAsStandardValue) values.Add(-1);
        return new StandardValuesCollection(values);
    }

    public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

    internal static ImageList? ImageListFromContext(ITypeDescriptorContext? context)
    {
        var instance = context?.Instance;
        if (instance is object[] items) instance = items.FirstOrDefault();
        if (instance is null) return null;
        var relatedName = context?.PropertyDescriptor?.Attributes[typeof(RelatedImageListAttribute)]
            is RelatedImageListAttribute related ? related.RelatedImageList : null;
        if (instance is ListViewItem stateItem && relatedName == "ListView.StateImageList")
            return stateItem.ListView?.StateImageList;
        if (instance is TreeNode node) instance = node.TreeView;

        if (instance is ColumnHeader header) return header.ImageList;
        if (instance is ListViewItem item) return item.ImageList;
        if (!string.IsNullOrEmpty(relatedName))
        {
            var relatedProperty = instance?.GetType().GetProperty(relatedName, BindingFlags.Public | BindingFlags.Instance);
            if (relatedProperty?.GetValue(instance) is ImageList list) return list;
        }

        var property = instance?.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(candidate => candidate.Name == "ImageList" && typeof(ImageList).IsAssignableFrom(candidate.PropertyType));
        return property?.GetValue(instance) as ImageList;
    }
}
