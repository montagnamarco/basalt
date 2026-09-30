using System.Buffers.Binary;
using SkiaSharp;

namespace System.Drawing.Drawing2D;

/// <summary>
/// The byte layout of RegionData: the EMF+ region record (MS-EMFPLUS 2.2.1.8)
/// after a size and a checksum, as GDI+'s GetRegionData writes it.
/// </summary>
/// <remarks>
/// A region is written as a single node: a rectangle list is not kept, so the
/// area goes out as one path, or as the empty or infinite node. Reading
/// accepts the whole node tree GDI+ can write (rectangles, paths, empty,
/// infinite and the five combinations), with points as floats or as 16-bit
/// integers. The checksum is written and not checked on reading; whether
/// Windows accepts these bytes, and computes the checksum the same way, has
/// not been verified.
/// </remarks>
internal static class RegionDataFormat
{
    private const uint Version = 0xDBC01002;

    private const uint And = 1;
    private const uint Or = 2;
    private const uint Xor = 3;
    private const uint Exclude = 4;
    private const uint Complement = 5;
    private const uint Rect = 0x10000000;
    private const uint Path = 0x10000001;
    private const uint Empty = 0x10000002;
    private const uint Infinite = 0x10000003;

    // The flag of an EMF+ path whose points are 16-bit integers.
    private const ushort CompressedPoints = 0x4000;

    public static byte[] Write(SKPath? area)
    {
        using var body = new MemoryStream();
        using (var writer = new BinaryWriter(body, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Version);

            // The number of nodes below the root: one node, none below it.
            writer.Write(0u);

            if (area is null)
            {
                writer.Write(Infinite);
            }
            else if (Region.HasNoArea(area))
            {
                writer.Write(Empty);
            }
            else
            {
                WritePath(writer, area);
            }
        }

        var content = body.ToArray();
        var data = new byte[8 + content.Length];

        BinaryPrimitives.WriteUInt32LittleEndian(data, (uint)(content.Length + 4));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), Crc32(content));
        content.CopyTo(data, 8);

        return data;
    }

    public static SKPath? Read(byte[]? data)
    {
        if (data is null || data.Length < 16) throw new ArgumentException("Parameter is not valid.");

        using var reader = new BinaryReader(new MemoryStream(data, 8, data.Length - 8));

        if (reader.ReadUInt32() != Version) throw new ArgumentException("Parameter is not valid.");

        reader.ReadUInt32();

        return ReadNode(reader);
    }

    private static void WritePath(BinaryWriter writer, SKPath area)
    {
        var points = new List<PointF>();
        var types = new List<byte>();
        PathGeometry.FromSkia(area, points, types);

        using var path = new MemoryStream();
        using (var pathWriter = new BinaryWriter(path, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            pathWriter.Write(Version);
            pathWriter.Write(points.Count);
            pathWriter.Write(0u);

            foreach (var point in points)
            {
                pathWriter.Write(point.X);
                pathWriter.Write(point.Y);
            }

            foreach (var type in types)
            {
                pathWriter.Write(type);
            }

            // The types are padded to a whole number of four-byte words.
            while (path.Length % 4 != 0)
            {
                pathWriter.Write((byte)0);
            }
        }

        writer.Write(Path);
        writer.Write((uint)path.Length);
        writer.Write(path.ToArray());
    }

    private static SKPath? ReadNode(BinaryReader reader)
    {
        var type = reader.ReadUInt32();

        switch (type)
        {
            case Rect:
                return Region.RectanglePath(new RectangleF(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

            case Empty:
                return new SKPath();

            case Infinite:
                return null;

            case Path:
                return ReadPath(reader);

            case And or Or or Xor or Exclude or Complement:
                {
                    using var left = ReadNode(reader);
                    using var right = ReadNode(reader);
                    using var plane = Region.RectanglePath(Region.InfiniteBounds);

                    var operation = type switch
                    {
                        And => SKPathOp.Intersect,
                        Or => SKPathOp.Union,
                        Xor => SKPathOp.Xor,
                        Exclude => SKPathOp.Difference,
                        _ => SKPathOp.ReverseDifference,
                    };

                    return (left ?? plane).Op(right ?? plane, operation) ?? throw new ArgumentException("Parameter is not valid.");
                }

            default:
                throw new ArgumentException("Parameter is not valid.");
        }
    }

    private static SKPath ReadPath(BinaryReader reader)
    {
        var size = reader.ReadUInt32();
        var start = reader.BaseStream.Position;

        reader.ReadUInt32();
        var count = reader.ReadInt32();
        var flags = reader.ReadUInt16();
        reader.ReadUInt16();

        var points = new PointF[count];
        for (var i = 0; i < count; i++)
        {
            points[i] = (flags & CompressedPoints) != 0
                ? new PointF(reader.ReadInt16(), reader.ReadInt16())
                : new PointF(reader.ReadSingle(), reader.ReadSingle());
        }

        var types = reader.ReadBytes(count);
        reader.BaseStream.Position = start + size;

        return PathGeometry.ToSkia(points, types, FillMode.Alternate);
    }

    /// <summary>
    /// CRC-32 with the polynomial of zip and PNG. Written out because the
    /// framework only ships it in a package this library does not depend on.
    /// </summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;

        foreach (var value in data)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
