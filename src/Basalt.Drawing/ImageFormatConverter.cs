using System.ComponentModel;
using System.Drawing.Imaging;
using System.Globalization;

namespace System.Drawing;

/// <summary>Converts an <see cref="ImageFormat"/> to and from its name, as a property grid shows it.</summary>
public class ImageFormatConverter : TypeConverter
{
    // Every named ImageFormat GDI+ defines, in the order GetStandardValues lists them.
    private static readonly (string Name, ImageFormat Format)[] Known =
    [
        ("MemoryBmp", ImageFormat.MemoryBmp),
        ("Bmp", ImageFormat.Bmp),
        ("Emf", ImageFormat.Emf),
        ("Wmf", ImageFormat.Wmf),
        ("Gif", ImageFormat.Gif),
        ("Jpeg", ImageFormat.Jpeg),
        ("Png", ImageFormat.Png),
        ("Tiff", ImageFormat.Tiff),
        ("Exif", ImageFormat.Exif),
        ("Icon", ImageFormat.Icon),
        ("Heif", ImageFormat.Heif),
        ("Webp", ImageFormat.Webp),
    ];

    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

    public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
    {
        if (value is string name)
        {
            foreach (var (candidateName, format) in Known)
            {
                if (string.Equals(candidateName, name, StringComparison.OrdinalIgnoreCase)) return format;
            }

            throw new ArgumentException($"'{name}' is not a valid value for ImageFormat.");
        }

        // The base method's own signature is not annotated nullable, even
        // though it happily returns null for a null value; forwarding one is
        // deliberate, not a bug this warning would otherwise flag.
        return base.ConvertFrom(context, culture, value!);
    }

    public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
    {
        ArgumentNullException.ThrowIfNull(destinationType);

        if (destinationType == typeof(string) && value is ImageFormat format) return format.ToString();

        return base.ConvertTo(context, culture, value, destinationType);
    }

    public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) =>
        new(Array.ConvertAll(Known, k => (object)k.Format));

    public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;
}
