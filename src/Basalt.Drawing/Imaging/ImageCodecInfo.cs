namespace System.Drawing.Imaging;

/// <summary>Describes one of GDI+'s built-in codecs: BMP, JPEG, GIF, TIFF or PNG.</summary>
/// <remarks>
/// The five here, their CLSIDs, MIME types and filename extensions are GDI+'s
/// own built-in codecs, published in the Windows SDK's gdiplusimaging.h.
/// <see cref="GetImageEncoders"/> and <see cref="GetImageDecoders"/> only
/// report the ones this library actually reads or writes: TIFF is in neither
/// list (this library does not read or write it), and GIF is a decoder only
/// (see <see cref="Image.Save(Stream, ImageFormat)"/>). ICO and WebP are files
/// this library can read through SkiaSharp, but are not among GDI+'s five
/// built-in format codecs, so they are not described here either.
/// </remarks>
public sealed class ImageCodecInfo
{
    internal ImageCodecInfo()
    {
        CodecName = "";
        DllName = "";
        FormatDescription = "";
        FilenameExtension = "";
        MimeType = "";
        SignatureMasks = [];
        SignaturePatterns = [];
    }

    public Guid Clsid { get; set; }

    public Guid FormatID { get; set; }

    public string CodecName { get; set; }

    public string DllName { get; set; }

    public string FormatDescription { get; set; }

    public string FilenameExtension { get; set; }

    public string MimeType { get; set; }

    public ImageCodecFlags Flags { get; set; }

    public int Version { get; set; }

    public byte[][] SignatureMasks { get; set; }

    public byte[][] SignaturePatterns { get; set; }

    public static ImageCodecInfo[] GetImageDecoders() =>
        Array.FindAll(Registry, codec => (codec.Flags & ImageCodecFlags.Decoder) != 0);

    public static ImageCodecInfo[] GetImageEncoders() =>
        Array.FindAll(Registry, codec => (codec.Flags & ImageCodecFlags.Encoder) != 0);

    private static readonly ImageCodecInfo[] Registry =
    [
        Describe("557cf400-1a04-11d3-9a73-0000f81ef32e", ImageFormat.Bmp, "BMP", "*.BMP;*.DIB;*.RLE", "image/bmp",
            ImageCodecFlags.Encoder | ImageCodecFlags.Decoder | ImageCodecFlags.SupportBitmap | ImageCodecFlags.Builtin,
            [[0x42, 0x4D]], [[0xFF, 0xFF]]),

        Describe("557cf401-1a04-11d3-9a73-0000f81ef32e", ImageFormat.Jpeg, "JPEG", "*.JPG;*.JPEG;*.JPE;*.JFIF", "image/jpeg",
            ImageCodecFlags.Encoder | ImageCodecFlags.Decoder | ImageCodecFlags.SupportBitmap | ImageCodecFlags.Builtin,
            [[0xFF, 0xD8]], [[0xFF, 0xFF]]),

        Describe("557cf402-1a04-11d3-9a73-0000f81ef32e", ImageFormat.Gif, "GIF", "*.GIF", "image/gif",
            ImageCodecFlags.Decoder | ImageCodecFlags.SupportBitmap | ImageCodecFlags.Builtin,
            [[0x47, 0x49, 0x46, 0x38]], [[0xFF, 0xFF, 0xFF, 0xFF]]),

        Describe("557cf406-1a04-11d3-9a73-0000f81ef32e", ImageFormat.Png, "PNG", "*.PNG", "image/png",
            ImageCodecFlags.Encoder | ImageCodecFlags.Decoder | ImageCodecFlags.SupportBitmap | ImageCodecFlags.Builtin,
            [[0x89, 0x50, 0x4E, 0x47]], [[0xFF, 0xFF, 0xFF, 0xFF]]),
    ];

    private static ImageCodecInfo Describe(
        string clsid, ImageFormat format, string name, string extension, string mimeType,
        ImageCodecFlags flags, byte[][] signaturePatterns, byte[][] signatureMasks) => new()
        {
            Clsid = new Guid(clsid),
            FormatID = format.Guid,
            CodecName = $"Built-in {name} Codec",
            FormatDescription = name,
            FilenameExtension = extension,
            MimeType = mimeType,
            Flags = flags,
            Version = 1,
            SignaturePatterns = signaturePatterns,
            SignatureMasks = signatureMasks,
        };
}
