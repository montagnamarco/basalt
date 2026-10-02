namespace System.Windows.Forms;

/// <summary>
/// Data carried on the clipboard or in a drag, tagged by a format name or by
/// a <see cref="Type"/> (its full name is the format). <see cref="DataObject"/>
/// is the implementation every clipboard and drag operation in this codebase
/// uses; a caller only needs this interface to read what it is given.
/// </summary>
public interface IDataObject
{
    object GetData(string format, bool autoConvert);

    object GetData(string format);

    object GetData(Type format);

    bool GetDataPresent(string format, bool autoConvert);

    bool GetDataPresent(string format);

    bool GetDataPresent(Type format);

    string[] GetFormats(bool autoConvert);

    string[] GetFormats();

    void SetData(string format, bool autoConvert, object? data);

    void SetData(string format, object? data);

    void SetData(Type format, object? data);

    void SetData(object? data);
}
