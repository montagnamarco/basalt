using System.Buffers.Binary;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// The .ico format: a directory of images of different sizes and colour
/// depths, each stored as a PNG or as a Windows bitmap with a transparency
/// mask.
/// </summary>
/// <remarks>
/// One entry is chosen and decoded by handing Skia a file holding that entry
/// alone: Skia reads both kinds of entry, but given the whole file it always
/// picks the largest image, and an icon asked for at 16 pixels must get the
/// 16-pixel image its designer drew, not the 256-pixel one shrunk.
/// </remarks>
internal static class IconFile
{
    private const int HeaderSize = 6;
    private const int EntrySize = 16;

    /// <summary>One image of an icon file, as its directory describes it.</summary>
    public readonly record struct Entry(int Width, int Height, int BitCount, int Offset, int Length);

    public static IReadOnlyList<Entry> ReadDirectory(byte[] data)
    {
        // Reserved 0, type 1 (icon), then the count.
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt16LittleEndian(data) != 0 || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2)) != 1)
        {
            throw new ArgumentException("Argument 'picture' must be a picture that can be used as a Icon.");
        }

        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));
        var entries = new List<Entry>(count);

        for (var i = 0; i < count; i++)
        {
            var at = HeaderSize + i * EntrySize;
            if (at + EntrySize > data.Length) break;

            // A width or height of 0 in the directory means 256.
            var width = data[at] == 0 ? 256 : data[at];
            var height = data[at + 1] == 0 ? 256 : data[at + 1];
            var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 6));
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 8));
            var offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 12));

            if (offset < 0 || length <= 0 || offset + length > data.Length) continue;

            entries.Add(new Entry(width, height, bitCount, offset, length));
        }

        if (entries.Count == 0) throw new ArgumentException("Argument 'picture' must be a picture that can be used as a Icon.");

        return entries;
    }

    /// <summary>
    /// The entry closest to the size asked for, with the most colours among
    /// equally close ones; 0 asks for the system's icon size, 32.
    /// </summary>
    public static Entry Choose(IReadOnlyList<Entry> entries, int width, int height)
    {
        if (width == 0) width = 32;
        if (height == 0) height = 32;

        return entries
            .OrderBy(entry => Math.Abs(entry.Width - width) + Math.Abs(entry.Height - height))
            .ThenByDescending(entry => entry.BitCount)
            .First();
    }

    /// <summary>The pixels of one entry, premultiplied BGRA like every bitmap here.</summary>
    public static SKBitmap Decode(byte[] data, Entry entry)
    {
        var single = SingleEntryFile(data, entry);

        using var decoded = SKBitmap.Decode(single) ?? throw new ArgumentException("Argument 'picture' must be a picture that can be used as a Icon.");

        var pixels = new SKBitmap(new SKImageInfo(decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        if (!decoded.CopyTo(pixels, SKColorType.Bgra8888)) throw new ArgumentException("Argument 'picture' must be a picture that can be used as a Icon.");

        return pixels;
    }

    /// <summary>An icon file of PNG images, one per bitmap, as SystemIcons builds them.</summary>
    public static byte[] FromImages(IReadOnlyList<SKBitmap> images)
    {
        var encoded = images.Select(image =>
        {
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }).ToList();

        var output = new byte[HeaderSize + EntrySize * images.Count + encoded.Sum(png => png.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4), (ushort)images.Count);

        var offset = HeaderSize + EntrySize * images.Count;
        for (var i = 0; i < images.Count; i++)
        {
            var at = HeaderSize + i * EntrySize;
            output[at] = (byte)(images[i].Width >= 256 ? 0 : images[i].Width);
            output[at + 1] = (byte)(images[i].Height >= 256 ? 0 : images[i].Height);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(at + 6), 32);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 8), encoded[i].Length);
            BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(at + 12), offset);

            encoded[i].CopyTo(output, offset);
            offset += encoded[i].Length;
        }

        return output;
    }

    private static byte[] SingleEntryFile(byte[] data, Entry entry)
    {
        var file = new byte[HeaderSize + EntrySize + entry.Length];

        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(4), 1);

        // The original directory entry, with the image now right after it.
        var original = HeaderSize + IndexOf(data, entry) * EntrySize;
        data.AsSpan(original, EntrySize).CopyTo(file.AsSpan(HeaderSize));
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(HeaderSize + 12), HeaderSize + EntrySize);
        data.AsSpan(entry.Offset, entry.Length).CopyTo(file.AsSpan(HeaderSize + EntrySize));

        return file;
    }

    private static int IndexOf(byte[] data, Entry entry)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4));

        for (var i = 0; i < count; i++)
        {
            if (BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(HeaderSize + i * EntrySize + 12)) == entry.Offset) return i;
        }

        throw new ArgumentException("Argument 'picture' must be a picture that can be used as a Icon.");
    }
}
