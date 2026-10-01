using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;

namespace System.Drawing;

/// <summary>
/// Converts an <see cref="Image"/> to and from the byte array a .resx or a
/// property grid stores it as.
/// </summary>
public class ImageConverter : TypeConverter
{
    // The first bytes GDI+'s own codecs recognise an image by. Long enough to
    // tell PNG, JPEG, GIF, BMP and ICO apart without reading the whole file.
    private static readonly byte[][] ImageSignatures =
    [
        [0x89, 0x50, 0x4E, 0x47], // PNG
        [0xFF, 0xD8, 0xFF], // JPEG
        [0x47, 0x49, 0x46, 0x38], // GIF87a / GIF89a
        [0x42, 0x4D], // BMP
        [0x00, 0x00, 0x01, 0x00], // ICO
    ];

    // Visual Studio's designer, for a long time, serialised an Image into a
    // .resx through the old OLE "serialized stream" format: a short header
    // before the image bytes actually start. The header's own length has
    // varied across versions, so this searches for where a real signature
    // begins instead of assuming one fixed offset.
    private const int MaxOleHeaderSearch = 64;

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(byte[]) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(byte[]) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is byte[] bytes && bytes.Length > 0)
        {
            var imageBytes = SkipOleHeaderIfPresent(bytes);
            return Image.FromStream(new MemoryStream(imageBytes));
        }

        // The base method's own signature is not annotated nullable, even
        // though it happily returns null for a null value; forwarding one is
        // deliberate, not a bug this warning would otherwise flag.
        return base.ConvertFrom(context, culture, value!);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(byte[]))
        {
            if (value is not Image image) return base.ConvertTo(context, culture, value, destinationType);

            // GDI+ writes a freshly drawn (MemoryBmp) bitmap back as PNG, since
            // MemoryBmp is not a format any decoder can read; anything decoded
            // from a real file keeps the format it came in as.
            var format = image.RawFormat.Equals(ImageFormat.MemoryBmp) ? ImageFormat.Png : image.RawFormat;

            using var stream = new MemoryStream();
            image.Save(stream, format);
            return stream.ToArray();
        }

        if (destinationType == typeof(string))
        {
            return value is null ? "(none)" : value.ToString();
        }

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) =>
        TypeDescriptor.GetProperties(value, attributes);

    public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

    private static byte[] SkipOleHeaderIfPresent(byte[] bytes)
    {
        if (StartsWithSignature(bytes, 0)) return bytes;

        var limit = Math.Min(MaxOleHeaderSearch, bytes.Length);
        for (var offset = 1; offset < limit; offset++)
        {
            if (StartsWithSignature(bytes, offset)) return bytes[offset..];
        }

        // Nothing recognisable anywhere in the search window: let the decoder
        // itself report why, rather than guessing a cut point.
        return bytes;
    }

    private static bool StartsWithSignature(byte[] bytes, int offset)
    {
        foreach (var signature in ImageSignatures)
        {
            if (offset + signature.Length > bytes.Length) continue;

            var matches = true;
            for (var i = 0; i < signature.Length; i++)
            {
                if (bytes[offset + i] != signature[i])
                {
                    matches = false;
                    break;
                }
            }

            if (matches) return true;
        }

        return false;
    }
}
