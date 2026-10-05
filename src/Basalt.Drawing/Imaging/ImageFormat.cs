using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace System.Drawing.Imaging;

/// <summary>A file format of an image, identified as GDI+ identifies it.</summary>
[TypeConverter(typeof(ImageFormatConverter))]
public sealed class ImageFormat
{
    public ImageFormat(Guid guid) => Guid = guid;

    public Guid Guid { get; }

    // The identifiers GDI+ assigns. An application compares and stores them,
    // so they are the same values and not ours.
    public static ImageFormat MemoryBmp { get; } = Known("b96b3caa");
    public static ImageFormat Bmp { get; } = Known("b96b3cab");
    public static ImageFormat Emf { get; } = Known("b96b3cac");
    public static ImageFormat Wmf { get; } = Known("b96b3cad");
    public static ImageFormat Jpeg { get; } = Known("b96b3cae");
    public static ImageFormat Png { get; } = Known("b96b3caf");
    public static ImageFormat Gif { get; } = Known("b96b3cb0");
    public static ImageFormat Tiff { get; } = Known("b96b3cb1");
    public static ImageFormat Exif { get; } = Known("b96b3cb2");
    public static ImageFormat Icon { get; } = Known("b96b3cb5");
    public static ImageFormat Heif { get; } = Known("b96b3cb6");
    public static ImageFormat Webp { get; } = Known("b96b3cb7");

    private static ImageFormat Known(string first) => new(new Guid(first + "-0728-11d3-9d7b-0000f81ef32e"));

    public override bool Equals([NotNullWhen(true)] object? o) => o is ImageFormat other && other.Guid == Guid;

    public override int GetHashCode() => Guid.GetHashCode();

    public override string ToString()
    {
        if (Guid == MemoryBmp.Guid) return "MemoryBMP";
        if (Guid == Bmp.Guid) return "Bmp";
        if (Guid == Emf.Guid) return "Emf";
        if (Guid == Wmf.Guid) return "Wmf";
        if (Guid == Gif.Guid) return "Gif";
        if (Guid == Jpeg.Guid) return "Jpeg";
        if (Guid == Png.Guid) return "Png";
        if (Guid == Tiff.Guid) return "Tiff";
        if (Guid == Exif.Guid) return "Exif";
        if (Guid == Icon.Guid) return "Icon";
        if (Guid == Heif.Guid) return "Heif";
        if (Guid == Webp.Guid) return "Webp";

        return $"[ImageFormat: {Guid}]";
    }
}
