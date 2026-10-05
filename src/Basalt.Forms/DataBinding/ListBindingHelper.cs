using System.ComponentModel;
using System.Collections;

namespace System.Windows.Forms;

/// <summary>
/// Turns whatever a DataSource property holds into the list a CurrencyManager
/// can walk: a DataTable's IListSource becomes its default DataView, a list
/// is returned unchanged, and anything else (a single business object) is
/// returned unchanged too, for the caller to treat as a scalar.
/// </summary>
public static class ListBindingHelper
{
    public static object? GetList(object? list) => list is IListSource source ? source.GetList() : list;

    public static object? GetList(object? dataSource, string? dataMember)
    {
        var list = GetList(dataSource);
        if (list is null || list is Type || string.IsNullOrEmpty(dataMember)) return list;
        var property = GetListItemProperties(list).Find(dataMember, true)
            ?? throw new ArgumentException($"The data member '{dataMember}' does not exist.", nameof(dataMember));
        object? item = list;
        if (list is ICurrencyManagerProvider provider)
        {
            var manager = provider.CurrencyManager;
            item = manager is not null && manager.Position >= 0 && manager.Position < manager.Count ? manager.Current : null;
        }
        else if (list is IEnumerable enumerable) item = FirstItem(enumerable);
        return item is null ? null : property.GetValue(item);
    }

    public static Type? GetListItemType(object? list)
    {
        list = GetList(list);
        if (list is null) return null;
        var type = list as Type ?? list.GetType();
        if (type.IsArray) return type.GetElementType();
        var indexer = type.GetProperties().FirstOrDefault(property => property.PropertyType != typeof(object)
            && property.GetIndexParameters() is [{ ParameterType: var parameterType }] && parameterType == typeof(int));
        if (indexer is not null) return indexer.PropertyType;
        var genericEnumerable = type.GetInterfaces().Append(type).FirstOrDefault(candidate => candidate.IsGenericType
            && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (genericEnumerable is not null) return genericEnumerable.GenericTypeArguments[0];
        return list is IEnumerable enumerable && FirstItem(enumerable) is { } item ? item.GetType()
            : typeof(IEnumerable).IsAssignableFrom(type) ? typeof(object) : type;
    }

    public static Type GetListItemType(object? dataSource, string? dataMember)
    {
        if (string.IsNullOrEmpty(dataMember)) return GetListItemType(dataSource) ?? typeof(object);
        var property = GetListItemProperties(dataSource).Find(dataMember, true);
        return property is null ? typeof(object) : GetListItemType(property.PropertyType) ?? typeof(object);
    }

    public static PropertyDescriptorCollection GetListItemProperties(object? list)
    {
        list = GetList(list);
        if (list is null) return PropertyDescriptorCollection.Empty;
        if (list is ITypedList typed) return typed.GetItemProperties(null);
        if (list is Type || list is IEnumerable)
            return TypeDescriptor.GetProperties(GetListItemType(list) ?? typeof(object), [BrowsableAttribute.Yes]);
        return TypeDescriptor.GetProperties(list);
    }

    public static PropertyDescriptorCollection GetListItemProperties(object? list, PropertyDescriptor[]? listAccessors)
    {
        var current = GetList(list);
        if (current is ITypedList typed) return typed.GetItemProperties(listAccessors);
        if (listAccessors is null || listAccessors.Length == 0) return GetListItemProperties(current);
        foreach (var accessor in listAccessors)
        {
            if (accessor is null) return PropertyDescriptorCollection.Empty;
            var item = current is IEnumerable enumerable ? FirstItem(enumerable) : current;
            current = item is null || item is Type ? accessor.PropertyType : accessor.GetValue(item) ?? accessor.PropertyType;
            current = GetList(current);
        }
        return GetListItemProperties(current);
    }

    public static PropertyDescriptorCollection GetListItemProperties(object? dataSource, string? dataMember, PropertyDescriptor[]? listAccessors)
    {
        if (string.IsNullOrEmpty(dataMember)) return GetListItemProperties(dataSource, listAccessors);
        var property = GetListItemProperties(dataSource).Find(dataMember, true)
            ?? throw new ArgumentException($"The data member '{dataMember}' does not exist.", nameof(dataMember));
        return GetListItemProperties(dataSource, [property, .. listAccessors ?? []]);
    }

    public static string GetListName(object? list, PropertyDescriptor[]? listAccessors)
    {
        list = GetList(list);
        if (list is null) return string.Empty;
        if (list is ITypedList typed) return typed.GetListName(listAccessors);
        var type = listAccessors is { Length: > 0 } ? listAccessors[0].PropertyType : list as Type ?? list.GetType();
        return (typeof(IList).IsAssignableFrom(type) ? GetListItemType(type) : type)?.Name ?? string.Empty;
    }

    private static object? FirstItem(IEnumerable source)
    {
        var enumerator = source.GetEnumerator();
        try { return enumerator.MoveNext() ? enumerator.Current : null; }
        finally { (enumerator as IDisposable)?.Dispose(); }
    }
}
