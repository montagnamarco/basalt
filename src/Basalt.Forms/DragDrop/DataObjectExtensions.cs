namespace System.Windows.Forms;

/// <summary>
/// Typed reads for any <see cref="IDataObject"/>, including one that is not
/// a <see cref="DataObject"/> (an <see cref="ITypedDataObject"/> already
/// answers these directly; this forwards to it rather than duplicating the logic).
/// </summary>
public static class DataObjectExtensions
{
    public static bool TryGetData<T>(this IDataObject dataObject, out T data)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        return dataObject is ITypedDataObject typed
            ? typed.TryGetData(out data)
            : TryGetData(dataObject, DataObject.FormatNameForType(typeof(T)), out data);
    }

    public static bool TryGetData<T>(this IDataObject dataObject, string format, out T data) =>
        TryGetData(dataObject, format, autoConvert: true, out data);

    public static bool TryGetData<T>(this IDataObject dataObject, string format, bool autoConvert, out T data)
    {
        ArgumentNullException.ThrowIfNull(dataObject);

        if (dataObject is ITypedDataObject typed) return typed.TryGetData(format, autoConvert, out data);

        if (dataObject.GetDataPresent(format, autoConvert) && dataObject.GetData(format, autoConvert) is T typedValue)
        {
            data = typedValue;
            return true;
        }

        data = default!;
        return false;
    }

    /// <summary>
    /// A data object built here never holds a serialized payload, so there is
    /// nothing for <paramref name="resolver"/> to resolve; see <see cref="DataObject.TryGetDataCore{T}"/>.
    /// </summary>
    public static bool TryGetData<T>(
        this IDataObject dataObject,
        string format,
        Func<Reflection.Metadata.TypeName, Type> resolver,
        bool autoConvert,
        out T data) => TryGetData(dataObject, format, autoConvert, out data);
}
