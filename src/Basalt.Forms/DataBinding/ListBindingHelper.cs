using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// Turns whatever a DataSource property holds into the list a CurrencyManager
/// can walk: a DataTable's IListSource becomes its default DataView, a list
/// is returned unchanged, and anything else (a single business object) is
/// returned unchanged too, for the caller to treat as a scalar.
/// </summary>
/// <remarks>Only the overload BindingContext needs is implemented; the schema-reflection overloads are left out.</remarks>
public static class ListBindingHelper
{
    public static object? GetList(object? list) => list is IListSource source ? source.GetList() : list;
}
