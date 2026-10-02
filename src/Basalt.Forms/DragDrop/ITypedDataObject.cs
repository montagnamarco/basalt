namespace System.Windows.Forms;

/// <summary>
/// An <see cref="IDataObject"/> that can hand data back as a specific type
/// without the caller catching an exception to find out the format is absent
/// or shaped differently, as the obsolete untyped <c>GetData</c> methods require.
/// </summary>
public interface ITypedDataObject : IDataObject
{
    bool TryGetData<T>(out T data);

    bool TryGetData<T>(string format, out T data);

    bool TryGetData<T>(string format, bool autoConvert, out T data);

    bool TryGetData<T>(string format, Func<Reflection.Metadata.TypeName, Type> resolver, bool autoConvert, out T data);
}
