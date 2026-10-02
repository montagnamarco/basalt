using System.ComponentModel;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Turns the selected object(s) into the GridItem tree: TypeDescriptor gives
/// the properties (BrowsableAttributes is the filter PropertyGrid itself
/// documents), several objects keep only the properties they share by name
/// and type, and PropertySort decides whether they come back flat or grouped
/// by CategoryAttribute.
/// </summary>
public partial class PropertyGrid
{
    /// <summary>The top-level rows under the root: categories for the two Categorized sorts, properties directly otherwise.</summary>
    internal GridItemCollection BuildTopLevelEntries(GridItem parent, object[] components)
    {
        if (components.Length == 0) return GridItemCollection.Empty;

        var descriptors = CommonProperties(components);

        return BuildEntries(parent, descriptors, components);
    }

    /// <summary>An expanded property's own sub-properties: always built from that one sub-object, flat and alphabetical, as Microsoft's nested grid rows are.</summary>
    internal GridItemCollection BuildChildEntries(GridItem parent, object subObject)
    {
        var attributes = BrowsableAttributesArray();
        var descriptors = TypeDescriptor.GetProperties(subObject, attributes).Cast<PropertyDescriptor>().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();

        var items = descriptors.Select(d => (GridItem)new PropertyGridEntry(this, parent, [subObject], d)).ToArray();

        return new GridItemCollection(items);
    }

    /// <summary>The properties every selected component has, by name and declared type; a single component just keeps all of its own.</summary>
    private PropertyDescriptor[] CommonProperties(object[] components)
    {
        var attributes = BrowsableAttributesArray();
        var first = _defaultTab.GetProperties(components[0], attributes).Cast<PropertyDescriptor>();

        if (components.Length == 1) return [.. first];

        var shared = new List<PropertyDescriptor>();
        foreach (var descriptor in first)
        {
            var sharedByAll = true;
            for (var i = 1; i < components.Length; i++)
            {
                var others = _defaultTab.GetProperties(components[i], attributes);
                var match = others[descriptor.Name];
                if (match is null || match.PropertyType != descriptor.PropertyType)
                {
                    sharedByAll = false;
                    break;
                }
            }

            if (sharedByAll) shared.Add(descriptor);
        }

        return [.. shared];
    }

    private Attribute[]? BrowsableAttributesArray() =>
        _browsableAttributes.Count == 0 ? null : _browsableAttributes.Cast<Attribute>().ToArray();

    private GridItemCollection BuildEntries(GridItem parent, PropertyDescriptor[] descriptors, object[] components)
    {
        if (_propertySort is PropertySort.NoSort or PropertySort.Alphabetical)
        {
            var ordered = _propertySort == PropertySort.Alphabetical
                ? descriptors.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray()
                : descriptors;

            var items = ordered.Select(d => (GridItem)new PropertyGridEntry(this, parent, components, d)).ToArray();

            return new GridItemCollection(items);
        }

        return BuildCategorized(parent, descriptors, components);
    }

    /// <summary>Categorized and CategorizedAlphabetical: one CategoryGridEntry per CategoryAttribute, "Misc" last, properties sorted within a category only for the Alphabetical variant.</summary>
    private GridItemCollection BuildCategorized(GridItem parent, PropertyDescriptor[] descriptors, object[] components)
    {
        var byCategory = new Dictionary<string, List<PropertyDescriptor>>();
        var order = new List<string>();

        foreach (var descriptor in descriptors)
        {
            var category = descriptor.Category;
            if (!byCategory.TryGetValue(category, out var list))
            {
                list = [];
                byCategory[category] = list;
                order.Add(category);
            }

            list.Add(descriptor);
        }

        var categories = _propertySort == PropertySort.CategorizedAlphabetical
            ? order.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()
            : order;

        if (categories.Remove("Misc")) categories.Add("Misc");

        var categoryEntries = new List<GridItem>();
        foreach (var category in categories)
        {
            var entry = new CategoryGridEntry(this, parent, category);

            var props = byCategory[category];
            var ordered = _propertySort == PropertySort.CategorizedAlphabetical
                ? props.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToArray()
                : [.. props];

            entry.SetProperties([.. ordered.Select(d => new PropertyGridEntry(this, entry, components, d))]);
            categoryEntries.Add(entry);
        }

        return new GridItemCollection([.. categoryEntries]);
    }
}
