using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

/// <summary>
/// What ImageList.ImageStream exchanges: a snapshot of an image list's
/// pictures, size, colour depth and transparent colour.
/// </summary>
/// <remarks>
/// Microsoft stores this as a BinaryFormatter-encoded block inside a form's
/// .resx file, so a designer can round-trip an ImageList without a native
/// handle. BinaryFormatter is out of scope for Basalt (it was removed from
/// .NET itself for the vulnerabilities it carried), so this type only keeps
/// the snapshot in memory: <see cref="ImageList.ImageStream"/> reads and
/// writes it directly, and the two members that would touch a byte stream -
/// the ISerializable constructor a formatter calls to rebuild one, and
/// <see cref="GetObjectData"/> - refuse instead of writing a format nothing
/// here can read back.
/// </remarks>
public sealed class ImageListStreamer : IDisposable, ISerializable
{
    private const string SerializationNotSupported =
        "ImageListStreamer keeps an ImageList's pictures in memory only: it does not " +
        "read or write the BinaryFormatter-encoded block Windows Forms stored in a .resx file.";

    private List<Bitmap>? _images;
    private List<string>? _keys;
    private Size _imageSize;
    private ColorDepth _colorDepth;
    private Color _transparentColor;

    /// <summary>Takes a snapshot of an image list's current state, for its ImageStream getter.</summary>
    internal ImageListStreamer(ImageList source)
    {
        _images = [.. source.Images.CloneBitmaps()];
        _keys = [.. source.Images.CloneKeys()];
        _imageSize = source.ImageSize;
        _colorDepth = source.ColorDepth;
        _transparentColor = source.TransparentColor;
    }

    /// <summary>
    /// The constructor a BinaryFormatter-style deserializer calls by
    /// reflection to rebuild an instance from a stream. Never public: nothing
    /// in Basalt produces the bytes it would read.
    /// </summary>
    private ImageListStreamer(SerializationInfo info, StreamingContext context)
    {
        throw new PlatformNotSupportedException(SerializationNotSupported);
    }

    /// <summary>Hands the snapshot's pictures and settings to the ImageList whose ImageStream was set.</summary>
    internal void CopyTo(ImageList target)
    {
        ObjectDisposedException.ThrowIf(_images is null || _keys is null, this);

        target.ReplaceImages(_images, _keys, _imageSize, _colorDepth, _transparentColor);
    }

    public void GetObjectData(SerializationInfo si, StreamingContext context) =>
        throw new PlatformNotSupportedException(SerializationNotSupported);

    public void Dispose()
    {
        if (_images is null) return;

        foreach (var image in _images) image.Dispose();

        _images = null;
        _keys = null;
    }
}
