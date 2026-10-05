using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// Data tagged by format name, carried on the clipboard or in a drag. Every
/// format lives as the live .NET object it was given: nothing is serialized,
/// so a format is only readable back as the type it was stored with (or a
/// compatible one through the small set of conversions below) within this
/// process. Native OLE IDataObject calls are exposed for signature compatibility
/// but reject use because this platform-independent control tree has no OLE store.
/// </summary>
public partial class DataObject : IDataObject, ITypedDataObject, System.Runtime.InteropServices.ComTypes.IDataObject
{
    // Ordinal: format names are compared exactly, as Windows clipboard formats are.
    private readonly Dictionary<string, object?> _data = new(StringComparer.Ordinal);

    public DataObject()
    {
    }

    public DataObject(object data) => SetData(data);

    public DataObject(string format, object data) => SetData(format, data);

    public virtual object GetData(string format, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(format);

        if (_data.TryGetValue(format, out var exact)) return exact is IJsonPayload json ? json.Deserialize()! : exact!;

        if (autoConvert)
        {
            foreach (var alias in MappedFormats(format))
            {
                if (_data.TryGetValue(alias, out var value)) return value!;
            }
        }

        return null!;
    }

    public virtual object GetData(string format) => GetData(format, autoConvert: true);

    public virtual object GetData(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetData(format.FullName ?? format.Name, autoConvert: true);
    }

    public virtual bool GetDataPresent(string format, bool autoConvert)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (_data.ContainsKey(format)) return true;
        return autoConvert && MappedFormats(format).Any(_data.ContainsKey);
    }

    public virtual bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

    public virtual bool GetDataPresent(Type format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return GetDataPresent(format.FullName ?? format.Name, autoConvert: true);
    }

    public virtual string[] GetFormats(bool autoConvert)
    {
        if (!autoConvert) return [.. _data.Keys];

        // In the order the data was set, the text formats it converts to after them.
        var formats = new List<string>(_data.Keys);
        if (formats.Any(format => TextFormats.Contains(format)))
        {
            foreach (var format in TextFormats)
            {
                if (!formats.Contains(format)) formats.Add(format);
            }
        }

        return [.. formats];
    }

    public virtual string[] GetFormats() => GetFormats(autoConvert: true);

    public virtual void SetData(string format, bool autoConvert, object? data)
    {
        ArgumentNullException.ThrowIfNull(format);

        _data[format] = data;

        // Text/UnicodeText/OemText/"System.String" are the same string under four names, and
        // Bitmap/"System.Drawing.Bitmap" are the same image under two: setting one satisfies all.
        if (autoConvert && IsMappedPayload(format, data))
        {
            foreach (var alias in MappedFormats(format)) _data[alias] = data;
        }
    }

    public virtual void SetData(string format, object? data) => SetData(format, autoConvert: true, data);

    public virtual void SetData(Type format, object? data) => SetData(format.FullName ?? format.Name, autoConvert: true, data);

    public virtual void SetData(object? data)
    {
        if (data is null) return;

        // WinForms stores an untagged object under its own runtime type name, exactly as
        // SetData(Type, object) would with that type: the two overloads agree on the key.
        SetData(data.GetType().FullName ?? data.GetType().Name, autoConvert: true, data);
    }

    public virtual bool ContainsText() => GetDataPresent(DataFormats.UnicodeText);

    public virtual bool ContainsText(TextDataFormat format) => GetDataPresent(TextFormatName(format), autoConvert: false);

    public virtual string GetText() => GetText(TextDataFormat.UnicodeText);

    public virtual string GetText(TextDataFormat format) => GetData(TextFormatName(format), autoConvert: false) as string ?? string.Empty;

    public virtual void SetText(string textData) => SetText(textData, TextDataFormat.UnicodeText);

    public virtual void SetText(string textData, TextDataFormat format)
    {
        ArgumentException.ThrowIfNullOrEmpty(textData);

        SetData(TextFormatName(format), autoConvert: true, textData);
    }

    public virtual bool ContainsFileDropList() => GetDataPresent(DataFormats.FileDrop, autoConvert: false);

    public virtual StringCollection GetFileDropList() => GetData(DataFormats.FileDrop, autoConvert: false) as StringCollection ?? [];

    public virtual void SetFileDropList(StringCollection filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        SetData(DataFormats.FileDrop, autoConvert: false, filePaths);
    }

    public virtual bool ContainsImage() => GetDataPresent(DataFormats.Bitmap, autoConvert: false);

    public virtual Image GetImage() => (Image)GetData(DataFormats.Bitmap, autoConvert: false);

    public virtual void SetImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        SetData(DataFormats.Bitmap, autoConvert: false, image);
    }

    public virtual bool ContainsAudio() => GetDataPresent(DataFormats.WaveAudio, autoConvert: false);

    public virtual Stream GetAudioStream() => GetData(DataFormats.WaveAudio, autoConvert: false) is byte[] bytes
        ? new MemoryStream(bytes, writable: false)
        : Stream.Null;

    public virtual void SetAudio(byte[] audioBytes)
    {
        ArgumentNullException.ThrowIfNull(audioBytes);

        SetData(DataFormats.WaveAudio, autoConvert: false, audioBytes);
    }

    public virtual void SetAudio(Stream audioStream)
    {
        ArgumentNullException.ThrowIfNull(audioStream);

        using var buffer = new MemoryStream();
        audioStream.CopyTo(buffer);
        SetAudio(buffer.ToArray());
    }

    public bool TryGetData<T>(out T data) => TryGetData(typeof(T).FullName ?? typeof(T).Name, autoConvert: true, out data);

    public bool TryGetData<T>(string format, out T data) => TryGetData(format, autoConvert: true, out data);

    public bool TryGetData<T>(string format, bool autoConvert, out T data) =>
        TryGetDataCore(format, resolver: null, autoConvert, out data);

    public bool TryGetData<T>(string format, Func<Reflection.Metadata.TypeName, Type> resolver, bool autoConvert, out T data) =>
        TryGetDataCore(format, resolver, autoConvert, out data);

    /// <summary>
    /// The JSON format records its generic type when written. The resolver is
    /// accepted for signature compatibility; this local store never activates
    /// a type supplied by clipboard data.
    /// </summary>
    protected virtual bool TryGetDataCore<T>(string format, Func<Reflection.Metadata.TypeName, Type>? resolver, bool autoConvert, out T data)
    {
        if (_data.TryGetValue(format, out var stored) && TryGetStoredData(stored, out data))
            return true;

        if (autoConvert)
        {
            foreach (var alias in MappedFormats(format))
            {
                if (_data.TryGetValue(alias, out stored) && TryGetStoredData(stored, out data))
                    return true;
            }
        }

        data = default!;
        return false;
    }

    private static bool TryGetStoredData<T>(object? stored, out T data)
    {
        if (stored is JsonPayload<T> json)
        {
            data = json.DeserializeTyped()!;
            return true;
        }

        if (stored is ImportedJsonPayload imported)
            return imported.TryDeserialize(out data);

        if (stored is not IJsonPayload && stored is T typed)
        {
            data = typed;
            return true;
        }

        data = default!;
        return false;
    }

    /// <summary>The format name Microsoft's DataObject special-cases for a handful of common types; a type's own full name otherwise.</summary>
    internal static string FormatNameForType(Type type)
    {
        if (type == typeof(string)) return DataFormats.UnicodeText;
        if (typeof(Image).IsAssignableFrom(type)) return DataFormats.Bitmap;
        if (type == typeof(StringCollection)) return DataFormats.FileDrop;

        return type.FullName ?? type.Name;
    }

    private static string TextFormatName(TextDataFormat format) => format switch
    {
        TextDataFormat.Text => DataFormats.Text,
        TextDataFormat.UnicodeText => DataFormats.UnicodeText,
        TextDataFormat.Rtf => DataFormats.Rtf,
        TextDataFormat.Html => DataFormats.Html,
        TextDataFormat.CommaSeparatedValue => DataFormats.CommaSeparatedValue,
        _ => throw new InvalidEnumArgumentException(nameof(format), (int)format, typeof(TextDataFormat)),
    };

    private static readonly string[] TextFormats = [DataFormats.Text, DataFormats.UnicodeText, DataFormats.OemText];

    // A string travels under its own type name and the three classic text format
    // names interchangeably; a bitmap travels under its own type name and "Bitmap".
    // These are the groups System.Windows.Forms.DataFormats.GetMappedFormats returns.
    private static readonly string StringFormatName = typeof(string).FullName!;
    private static readonly string BitmapFormatName = typeof(Bitmap).FullName!;
    private static readonly string[] StringGroup = [StringFormatName, .. TextFormats];
    private static readonly string[] BitmapGroup = [BitmapFormatName, DataFormats.Bitmap];

    private static string[] MappedFormats(string format) =>
        StringGroup.Contains(format, StringComparer.Ordinal) ? StringGroup :
        BitmapGroup.Contains(format, StringComparer.Ordinal) ? BitmapGroup :
        [];

    /// <summary>
    /// Whether <paramref name="data"/> is the kind of payload <paramref name="format"/>'s
    /// mapped-format group exists for, so setting one alias of a group never stamps a
    /// mismatched value (e.g. an int) across the whole group.
    /// </summary>
    private static bool IsMappedPayload(string format, object? data) =>
        (StringGroup.Contains(format, StringComparer.Ordinal) && data is string)
        || (BitmapGroup.Contains(format, StringComparer.Ordinal) && data is Image);
}
