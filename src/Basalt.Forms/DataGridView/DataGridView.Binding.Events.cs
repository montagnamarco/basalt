using System.ComponentModel;
using System.Data;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// The property-changed events DataGridView.Binding.cs raises, DataBindingComplete and DataError,
/// and the conversion that turns an edited cell's Value into whatever type the bound property
/// expects before writing it back through its PropertyDescriptor.
/// </summary>
public partial class DataGridView
{
    public event EventHandler? AutoGenerateColumnsChanged
    {
        add => Events.AddHandler(s_autoGenerateColumnsChangedEvent, value);
        remove => Events.RemoveHandler(s_autoGenerateColumnsChangedEvent, value);
    }

    public event DataGridViewBindingCompleteEventHandler? DataBindingComplete
    {
        add => Events.AddHandler(s_dataBindingCompleteEvent, value);
        remove => Events.RemoveHandler(s_dataBindingCompleteEvent, value);
    }

    public event DataGridViewDataErrorEventHandler? DataError
    {
        add => Events.AddHandler(s_dataErrorEvent, value);
        remove => Events.RemoveHandler(s_dataErrorEvent, value);
    }

    public event EventHandler? DataMemberChanged
    {
        add => Events.AddHandler(s_dataMemberChangedEvent, value);
        remove => Events.RemoveHandler(s_dataMemberChangedEvent, value);
    }

    public event EventHandler? DataSourceChanged
    {
        add => Events.AddHandler(s_dataSourceChangedEvent, value);
        remove => Events.RemoveHandler(s_dataSourceChangedEvent, value);
    }

    private static readonly object s_autoGenerateColumnsChangedEvent = new();
    private static readonly object s_dataBindingCompleteEvent = new();
    private static readonly object s_dataErrorEvent = new();
    private static readonly object s_dataMemberChangedEvent = new();
    private static readonly object s_dataSourceChangedEvent = new();

    protected virtual void OnAutoGenerateColumnsChanged(EventArgs e) => ((EventHandler?)Events[s_autoGenerateColumnsChangedEvent])?.Invoke(this, e);

    protected virtual void OnDataBindingComplete(DataGridViewBindingCompleteEventArgs e) => ((DataGridViewBindingCompleteEventHandler?)Events[s_dataBindingCompleteEvent])?.Invoke(this, e);

    /// <summary>
    /// Raises DataError. displayErrorDialogIfNoHandler matches Microsoft's signature but a dialog
    /// is never shown in this implementation: an error with no handler is reported through the
    /// returned EventArgs.ThrowException flag only, left for the caller to act on.
    /// </summary>
    protected virtual void OnDataError(bool displayErrorDialogIfNoHandler, DataGridViewDataErrorEventArgs e) => ((DataGridViewDataErrorEventHandler?)Events[s_dataErrorEvent])?.Invoke(this, e);

    protected virtual void OnDataMemberChanged(EventArgs e) => ((EventHandler?)Events[s_dataMemberChangedEvent])?.Invoke(this, e);

    protected virtual void OnDataSourceChanged(EventArgs e) => ((EventHandler?)Events[s_dataSourceChangedEvent])?.Invoke(this, e);

    /// <summary>
    /// Writes a cell's current Value back into the bound item through its PropertyDescriptor,
    /// converting to the property's type first. A conversion or setter failure raises DataError
    /// with the exception; a handler that cancels it refuses the commit, leaving the cell in edit
    /// mode, exactly as a refused CellValidating does.
    /// </summary>
    /// <returns>True to let the commit proceed (nothing bound, or the write succeeded); false to keep editing.</returns>
    private bool PushCellValueToDataSource(DataGridViewCell cell)
    {
        if (!TryResolveBoundProperty(cell, out var descriptor, out var item)) return true;

        try
        {
            var converted = ConvertCellValueForWrite(cell.Value, descriptor, item);
            descriptor.SetValue(item, converted);

            // Reflects the actual, converted value back onto the cell: the editor only ever
            // produced a raw string, not the property's real type. DBNull (a DataTable column
            // cleared to null) is never shown as the literal value DBNull.Value.
            cell.Value = converted is DBNull ? null : converted;

            return true;
        }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        {
            var args = new DataGridViewDataErrorEventArgs(ex, cell.ColumnIndex, cell.RowIndex, DataGridViewDataErrorContexts.Commit);
            OnDataError(displayErrorDialogIfNoHandler: false, args);

            return !args.Cancel;
        }
    }

    /// <summary>The PropertyDescriptor and bound item a cell's column resolves to, or false when the grid or this column is not bound.</summary>
    private bool TryResolveBoundProperty(DataGridViewCell cell, out PropertyDescriptor descriptor, out object item)
    {
        descriptor = null!;
        item = null!;

        if (cell.OwningColumn is not { IsDataBound: true } column) return false;
        if (cell.OwningRow?.DataBoundItem is not { } boundItem) return false;
        if (_itemProperties?.Find(column.DataPropertyName, true) is not { } found) return false;

        descriptor = found;
        item = boundItem;

        return true;
    }

    /// <summary>
    /// An empty string clears the cell: null for anything that already accepts it (a
    /// Nullable&lt;T&gt; or a reference type other than string itself), DBNull.Value for a
    /// DataTable column that allows it, and otherwise the normal conversion below - which fails,
    /// raising DataError, exactly as typing an empty string into a non-nullable value type should.
    /// </summary>
    private static object? ConvertCellValueForWrite(object? value, PropertyDescriptor descriptor, object item)
    {
        if (value is string { Length: 0 } && descriptor.PropertyType != typeof(string))
        {
            if (item is DataRowView rowView && rowView.Row.Table.Columns.Contains(descriptor.Name))
            {
                var column = rowView.Row.Table.Columns[descriptor.Name]!;
                if (column.AllowDBNull) return DBNull.Value;
            }
            else if (AcceptsNull(descriptor.PropertyType))
            {
                return null;
            }
        }

        return ConvertToPropertyType(value, descriptor.PropertyType);
    }

    private static bool AcceptsNull(Type propertyType) => !propertyType.IsValueType || Nullable.GetUnderlyingType(propertyType) is not null;

    private static object? ConvertToPropertyType(object? value, Type propertyType)
    {
        if (value is null) return null;
        if (propertyType.IsInstanceOfType(value)) return value;

        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying.IsInstanceOfType(value)) return value;

        if (value is string text && underlying.IsEnum) return Enum.Parse(underlying, text, ignoreCase: true);

        var converter = TypeDescriptor.GetConverter(underlying);
        if (converter.CanConvertFrom(value.GetType())) return converter.ConvertFrom(null, CultureInfo.CurrentCulture, value);

        return Convert.ChangeType(value, underlying, CultureInfo.CurrentCulture);
    }
}
