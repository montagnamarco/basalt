using System.Drawing.Imaging;
using System.Runtime.Serialization;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A Windows icon: one image chosen, by size, from an .ico file.</summary>
/// <remarks>
/// The whole file is kept, as GDI+ keeps it: Save writes it back unchanged, and
/// a new Icon made from this one at another size chooses again from all of its
/// images rather than resizing the one already chosen.
/// </remarks>
public sealed class Icon : MarshalByRefObject, ICloneable, IDisposable, ISerializable
{
    private readonly byte[] _data;
    private SKBitmap? _pixels;

    public Icon(string fileName)
        : this(fileName, 0, 0)
    {
    }

    public Icon(string fileName, Size size)
        : this(fileName, size.Width, size.Height)
    {
    }

    public Icon(string fileName, int width, int height)
        : this(File.ReadAllBytes(fileName ?? throw new ArgumentNullException(nameof(fileName))), width, height)
    {
    }

    public Icon(Stream stream)
        : this(stream, 0, 0)
    {
    }

    public Icon(Stream stream, Size size)
        : this(stream, size.Width, size.Height)
    {
    }

    public Icon(Stream stream, int width, int height)
        : this(ReadAll(stream), width, height)
    {
    }

    public Icon(Icon original, Size size)
        : this(original, size.Width, size.Height)
    {
    }

    public Icon(Icon original, int width, int height)
        : this((original ?? throw new ArgumentNullException(nameof(original)))._data, width, height)
    {
    }

    public Icon(Type type, string resource)
        : this(ResourceBytes(type, resource), 0, 0)
    {
    }

    internal Icon(byte[] data, int width, int height)
    {
        _data = data;

        var entry = IconFile.Choose(IconFile.ReadDirectory(data), width, height);
        _pixels = IconFile.Decode(data, entry);
    }

    private Icon(SerializationInfo info, StreamingContext context)
        : this((byte[])info.GetValue("IconData", typeof(byte[]))!, ((Size)info.GetValue("IconSize", typeof(Size))!).Width, ((Size)info.GetValue("IconSize", typeof(Size))!).Height)
    {
    }

    ~Icon() => Dispose(false);

    public IntPtr Handle => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    public Size Size => new(Width, Height);

    internal SKBitmap Pixels => _pixels ?? throw new ObjectDisposedException(nameof(Icon));

    public static Icon FromHandle(IntPtr handle) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>The icon the system shows for a file: the shell's answer, which only Windows has.</summary>
    public static Icon? ExtractAssociatedIcon(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        if (!File.Exists(filePath)) throw new FileNotFoundException(null, filePath);

        throw new PlatformNotSupportedException(ShellIcons);
    }

    public static Icon? ExtractIcon(string filePath, int id, bool smallIcon) => throw new PlatformNotSupportedException(ShellIcons);

    public static Icon? ExtractIcon(string filePath, int id, int size) => throw new PlatformNotSupportedException(ShellIcons);

    public object Clone() => new Icon(_data, Width, Height);

    /// <summary>Writes the icon file this icon was read from, all of its images.</summary>
    public void Save(Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(outputStream);

        outputStream.Write(_data);
    }

    public Bitmap ToBitmap() => new(Pixels.Copy(), PixelFormat.Format32bppArgb, ImageFormat.MemoryBmp);

    public override string ToString() => "(Icon)";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!disposing) return;

        _pixels?.Dispose();
        _pixels = null;
    }

    void ISerializable.GetObjectData(SerializationInfo si, StreamingContext context)
    {
        si.AddValue("IconData", _data, typeof(byte[]));
        si.AddValue("IconSize", Size, typeof(Size));
    }

    private const string ShellIcons =
        "The icons of files and executables come from the Windows shell, which Basalt.Drawing does not have.";

    private static byte[] ReadAll(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        return copy.ToArray();
    }

    private static byte[] ResourceBytes(Type type, string resource)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(resource);

        using var stream = type.Module.Assembly.GetManifestResourceStream(type, resource)
            ?? throw new ArgumentException($"Resource '{resource}' cannot be found in class '{type}'.");

        return ReadAll(stream);
    }
}
