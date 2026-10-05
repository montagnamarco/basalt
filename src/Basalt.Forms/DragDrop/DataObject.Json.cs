using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace System.Windows.Forms;

public partial class DataObject
{
    [RequiresUnreferencedCode("Uses default System.Text.Json behavior which is not trim-compatible.")]
    public void SetDataAsJson<T>(string format, T data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        ArgumentNullException.ThrowIfNull(data);
        if (IsPredefinedJsonFormat(format))
            throw new ArgumentException("Predefined formats cannot contain JSON data.", nameof(format));
        if (typeof(T).IsAssignableTo(typeof(DataObject)))
            throw new ArgumentException("A DataObject cannot be serialized as JSON data.", nameof(data));

        SetData(format, autoConvert: false, new JsonPayload<T>(JsonSerializer.SerializeToUtf8Bytes(data)));
    }

    [RequiresUnreferencedCode("Uses default System.Text.Json behavior which is not trim-compatible.")]
    public void SetDataAsJson<T>(T data) => SetDataAsJson(typeof(T).FullName ?? typeof(T).Name, data);

    private interface IJsonPayload
    {
        object? Deserialize();
        string TypeIdentity { get; }
        byte[] Bytes { get; }
    }

    private sealed class JsonPayload<T>(byte[] bytes) : IJsonPayload
    {
        public string TypeIdentity => typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name;

        public byte[] Bytes => bytes;

        // The type is chosen by the application's generic API call; the payload
        // cannot name an arbitrary assembly or activate a different runtime type.
        public object? Deserialize() => JsonSerializer.Deserialize<T>(bytes);

        public T? DeserializeTyped() => JsonSerializer.Deserialize<T>(bytes);
    }

    private sealed class ImportedJsonPayload(string typeIdentity, byte[] bytes) : IJsonPayload
    {
        public string TypeIdentity => typeIdentity;

        public byte[] Bytes => bytes;

        // The untyped API never chooses a runtime type from clipboard metadata.
        // JsonElement.Clone copies the value onto the managed heap, so the
        // document backing it can (and must) be disposed before returning.
        public object Deserialize()
        {
            using var document = JsonDocument.Parse(bytes);
            return document.RootElement.Clone();
        }

        public bool TryDeserialize<T>(out T data)
        {
            if (TypeIdentity != (typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name))
            {
                data = default!;
                return false;
            }

            data = JsonSerializer.Deserialize<T>(bytes)!;
            return true;
        }
    }

    internal IEnumerable<(string Format, object? Value)> StoredEntries =>
        _data.Select(entry => (entry.Key, entry.Value));

    internal bool TryExportJson(string format, out string typeIdentity, out byte[] bytes)
    {
        if (_data.TryGetValue(format, out var value) && value is IJsonPayload json)
        {
            typeIdentity = json.TypeIdentity;
            bytes = json.Bytes;
            return true;
        }

        typeIdentity = string.Empty;
        bytes = [];
        return false;
    }

    internal void SetImportedJson(string format, string typeIdentity, byte[] bytes) =>
        SetData(format, autoConvert: false, new ImportedJsonPayload(typeIdentity, bytes));

    private static bool IsPredefinedJsonFormat(string format) => format is
        "Text" or "UnicodeText" or "Rich Text Format" or "HTML Format" or "OEMText" or
        "FileDrop" or "FileName" or "FileNameW" or "System.String" or "System.Drawing.Bitmap" or
        "Csv" or "CSV" or "DeviceIndependentBitmap" or "DataInterchangeFormat" or "Locale" or
        "PenData" or "RiffAudio" or "RIFF" or "SymbolicLink" or "TaggedImageFileFormat" or
        "WaveAudio" or "Bitmap" or "EnhancedMetafile" or "Palette" or "MetaFilePict";
}
