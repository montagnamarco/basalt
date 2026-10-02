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
/// process. <see cref="System.Runtime.InteropServices.ComTypes.IDataObject"/>
/// is not implemented: it exists on Windows for OLE's native drag and drop,
/// which this platform-independent control tree does not have.
/// </summary>
public class DataObject : IDataObject, ITypedDataObject
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

        if (_data.TryGetValue(format, out var exact)) return exact!;

        if (autoConvert)
        {
            foreach (var alias in TextAliases(format))
            {
                if (_data.TryGetValue(alias, out var value)) return value!;
            }
        }

        return null!;
    }

    public virtual object GetData(string format) => GetData(format, autoConvert: true);

    public virtual object GetData(Type format) => GetData(FormatNameForType(format), autoConvert: true);

    public virtual bool GetDataPresent(string format, bool autoConvert) => GetData(format, autoConvert) is not null;

    public virtual bool GetDataPresent(string format) => GetDataPresent(format, autoConvert: true);

    public virtual bool GetDataPresent(Type format) => GetDataPresent(FormatNameForType(format), autoConvert: true);

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

        // Text, UnicodeText and OemText are the same string under three names: setting one satisfies all three.
        if (autoConvert && data is string text && TextFormats.Contains(format))
        {
            foreach (var alias in TextFormats) _data[alias] = text;
        }
    }

    public virtual void SetData(string format, object? data) => SetData(format, autoConvert: true, data);

    public virtual void SetData(Type format, object? data) => SetData(FormatNameForType(format), autoConvert: true, data);

    public virtual void SetData(object? data)
    {
        if (data is null) return;

        SetData(FormatNameForType(data.GetType()), autoConvert: true, data);
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

    public bool TryGetData<T>(out T data) => TryGetData(FormatNameForType(typeof(T)), autoConvert: true, out data);

    public bool TryGetData<T>(string format, out T data) => TryGetData(format, autoConvert: true, out data);

    public bool TryGetData<T>(string format, bool autoConvert, out T data) =>
        TryGetDataCore(format, resolver: null, autoConvert, out data);

    public bool TryGetData<T>(string format, Func<Reflection.Metadata.TypeName, Type> resolver, bool autoConvert, out T data) =>
        TryGetDataCore(format, resolver, autoConvert, out data);

    /// <summary>
    /// Every format here is already the live object it was stored as, never a
    /// serialized payload, so there is nothing for <paramref name="resolver"/>
    /// to resolve; it is accepted for signature compatibility and ignored.
    /// </summary>
    protected virtual bool TryGetDataCore<T>(string format, Func<Reflection.Metadata.TypeName, Type>? resolver, bool autoConvert, out T data)
    {
        if (GetData(format, autoConvert) is T typed)
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

    private static string[] TextAliases(string format) => TextFormats.Contains(format, StringComparer.Ordinal) ? TextFormats : [];
}
