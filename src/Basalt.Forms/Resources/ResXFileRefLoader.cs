using System.ComponentModel;

namespace System.Resources;

/// <summary>
/// The part of ResXFileRef.Converter and ResXDataNode.GetValue that actually
/// reads the referenced file: byte[] and string get the raw bytes or text,
/// everything else goes through a Stream constructor (Bitmap, Icon, Cursor...)
/// or, failing that, the type's own TypeConverter.
/// </summary>
internal static class ResXFileRefLoader
{
    public static object Load(string path, Type type, System.Text.Encoding? encoding)
    {
        if (type == typeof(byte[])) return File.ReadAllBytes(path);

        if (type == typeof(string)) return File.ReadAllText(path, encoding ?? System.Text.Encoding.Default);

        var streamCtor = type.GetConstructor([typeof(Stream)]);
        if (streamCtor is not null)
        {
            using var stream = File.OpenRead(path);
            return streamCtor.Invoke([stream]);
        }

        var converter = TypeDescriptor.GetConverter(type);
        var bytes = File.ReadAllBytes(path);
        if (converter.CanConvertFrom(typeof(byte[]))) return converter.ConvertFrom(bytes)!;

        throw new NotSupportedException(
            $"ResXFileRef cannot load '{path}' as {type}: it has no public constructor taking a Stream and its TypeConverter cannot convert from a byte[].");
    }

    /// <summary>Resolves a path against a base directory the way the reader/writer do: untouched when already rooted.</summary>
    public static string ResolvePath(string fileName, string? basePath) =>
        Path.IsPathRooted(fileName) || string.IsNullOrEmpty(basePath) ? fileName : Path.Combine(basePath, fileName);
}
