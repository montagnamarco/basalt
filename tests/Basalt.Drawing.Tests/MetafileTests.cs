using System.Drawing;
using System.Drawing.Imaging;
using SkiaSharp;

namespace Basalt.Drawing.Tests;

/// <summary>
/// Metafile: reading a WMF or EMF's header, replaying its records onto a Skia canvas, and
/// enumerating its records as Graphics.EnumerateMetafile does. Both metafiles used here are built
/// by hand, byte for byte, rather than copied from anywhere: a placeable WMF and a plain EMF, each
/// recording one solid-filled rectangle plus one record type the player does not draw.
/// </summary>
public class MetafileTests
{
    // Logical units equal pixels throughout: the WMF declares 96 units per inch and never sets a
    // window/viewport extent (so MM_TEXT's identity mapping applies), and the EMF never scales
    // either. That keeps the arithmetic in each assertion obvious.
    private const int Left = 10;
    private const int Top = 10;
    private const int Right = 90;
    private const int Bottom = 90;
    private static readonly uint ColorRefRed = 0x000000FF; // COLORREF 0x00BBGGRR: pure red.

    [Fact]
    public void A_placeable_WMF_header_reports_its_bounds_and_type()
    {
        using var metafile = new Metafile(new MemoryStream(BuildWmf()));
        var header = metafile.GetMetafileHeader();

        Assert.True(header.IsWmfPlaceable());
        Assert.Equal(new Rectangle(0, 0, 100, 100), header.Bounds);
    }

    [Fact]
    public void An_EMF_header_reports_its_bounds_and_type()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));
        var header = metafile.GetMetafileHeader();

        Assert.True(header.IsEmf());
        Assert.Equal(new Rectangle(0, 0, 100, 100), header.Bounds);
    }

    [Fact]
    public void A_WMF_rectangle_plays_onto_a_canvas_at_the_right_place_and_colour()
    {
        using var metafile = new Metafile(new MemoryStream(BuildWmf()));
        using var target = new Bitmap(100, 100);

        using (var canvas = new SKCanvas(target.Pixels))
        {
            canvas.Clear(SKColors.Transparent);
            metafile.Play(canvas, new SKRect(0, 0, 100, 100));
        }

        // Inside the rectangle: solid red. Outside it: untouched, so still transparent.
        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(50, 50).ToArgb());
        Assert.Equal(0, target.GetPixel(2, 2).A);
    }

    [Fact]
    public void An_EMF_rectangle_plays_onto_a_canvas_at_the_right_place_and_colour()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));
        using var target = new Bitmap(100, 100);

        using (var canvas = new SKCanvas(target.Pixels))
        {
            canvas.Clear(SKColors.Transparent);
            metafile.Play(canvas, new SKRect(0, 0, 100, 100));
        }

        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(50, 50).ToArgb());
        Assert.Equal(0, target.GetPixel(2, 2).A);
    }

    [Fact]
    public void Playing_a_metafile_scales_from_its_recorded_bounds_to_the_destination()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));
        using var target = new Bitmap(200, 200);

        using (var canvas = new SKCanvas(target.Pixels))
        {
            canvas.Clear(SKColors.Transparent);
            metafile.Play(canvas, new SKRect(0, 0, 200, 200));
        }

        // The rectangle's logical bounds (10,10)-(90,90) in a 100x100 metafile scale by 2x into a
        // 200x200 destination, so (50,50) -- the centre either way -- stays inside it.
        Assert.Equal(Color.Red.ToArgb(), target.GetPixel(100, 100).ToArgb());
        Assert.Equal(0, target.GetPixel(4, 4).A);
    }

    [Fact]
    public void An_unsupported_WMF_record_type_is_listed()
    {
        using var metafile = new Metafile(new MemoryStream(BuildWmf()));

        // WmfSetROP2 = WmfRecordBase (0x10000) + META_SETROP2 (0x0104).
        Assert.Contains(EmfPlusRecordType.WmfSetROP2, metafile.UnsupportedRecordTypes);
    }

    [Fact]
    public void An_unsupported_EMF_record_type_is_listed()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));

        Assert.Contains(EmfPlusRecordType.EmfSetIcmMode, metafile.UnsupportedRecordTypes);
    }

    [Fact]
    public void EnumerateMetafile_visits_the_WMF_records_in_order()
    {
        using var metafile = new Metafile(new MemoryStream(BuildWmf()));
        using var target = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(target);

        var visited = new List<EmfPlusRecordType>();

        graphics.EnumerateMetafile(metafile, new Point(0, 0),
            (EmfPlusRecordType recordType, int flags, int dataSize, IntPtr data, PlayRecordCallback? callbackData) =>
            {
                visited.Add(recordType);
                return true;
            });

        Assert.Equal(
            [
                EmfPlusRecordType.WmfCreateBrushIndirect,
                EmfPlusRecordType.WmfSelectObject,
                EmfPlusRecordType.WmfRectangle,
                EmfPlusRecordType.WmfSetROP2,
                EmfPlusRecordType.WmfRecordBase, // META_EOF: raw function 0.
            ],
            visited);
    }

    [Fact]
    public void EnumerateMetafile_passes_each_records_own_byte_count()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));
        using var target = new Bitmap(10, 10);
        using var graphics = Graphics.FromImage(target);

        var sizes = new List<int>();

        graphics.EnumerateMetafile(metafile, new Point(0, 0),
            (EmfPlusRecordType recordType, int flags, int dataSize, IntPtr data, PlayRecordCallback? callbackData) =>
            {
                sizes.Add(dataSize);
                return true;
            });

        // EMR_CREATEBRUSHINDIRECT's payload is ihBrush + LOGBRUSH32 = 4 + 12 bytes.
        Assert.Contains(16, sizes);
    }

    [Fact]
    public void AddMetafileComment_is_not_implemented()
    {
        using var target = new Bitmap(1, 1);
        using var graphics = Graphics.FromImage(target);

        Assert.Throws<NotImplementedException>(() => graphics.AddMetafileComment([1, 2, 3]));
    }

    [Fact]
    public void A_native_handle_constructor_is_not_supported()
    {
        Assert.Throws<PlatformNotSupportedException>(() => new Metafile(IntPtr.Zero, false));
        Assert.Throws<PlatformNotSupportedException>(() => new Metafile(IntPtr.Zero, EmfType.EmfOnly));
    }

    [Fact]
    public void GetHenhmetafile_and_PlayRecord_are_not_supported()
    {
        using var metafile = new Metafile(new MemoryStream(BuildEmf()));

        Assert.Throws<PlatformNotSupportedException>(() => metafile.GetHenhmetafile());
        Assert.Throws<PlatformNotSupportedException>(() => metafile.PlayRecord(EmfPlusRecordType.EmfRectangle, 0, 0, []));
    }

    // ── A placeable WMF with one solid-red rectangle, a CreateBrushIndirect/SelectObject pair to
    // select it, one unsupported record (SetROP2) and an EOF. ───────────────────────────────────
    [Fact]
    public void Bytes_that_are_not_a_metafile_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new Metafile(new MemoryStream([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])));
    }

    /// <summary>
    /// A record whose size field is near int.MaxValue once wrapped the bounds
    /// check negative and asked for a two-gigabyte array. It must end the
    /// records instead.
    /// </summary>
    [Fact]
    public void A_record_claiming_to_be_huge_ends_the_records_instead_of_allocating()
    {
        var emf = BuildEmf();
        var truncated = new byte[96];
        Array.Copy(emf, truncated, 88);
        BitConverter.GetBytes(0x7FFFFFFC).CopyTo(truncated, 92);
        BitConverter.GetBytes(27).CopyTo(truncated, 88);

        using var metafile = new Metafile(new MemoryStream(truncated));

        Assert.Equal(MetafileType.Emf, metafile.GetMetafileHeader().Type);
    }

    [Fact]
    public void A_placeable_WMF_reports_the_units_per_inch_it_declares()
    {
        var wmf = BuildWmf();
        BitConverter.GetBytes((short)1440).CopyTo(wmf, 14);

        using var metafile = new Metafile(new MemoryStream(wmf));

        Assert.Equal(1440f, metafile.GetMetafileHeader().DpiX);
    }

    private static byte[] BuildWmf()
    {
        var records = new List<byte[]>
        {
            WmfRecord(0x02FC, writer => // META_CREATEBRUSHINDIRECT
            {
                writer.Write((short)0); // BS_SOLID
                writer.Write(ColorRefRed);
                writer.Write((short)0); // hatch, unused for a solid brush
            }),
            WmfRecord(0x012D, writer => writer.Write((short)0)), // META_SELECTOBJECT: table index 0
            WmfRecord(0x041B, writer => // META_RECTANGLE: bottom, right, top, left
            {
                writer.Write((short)Bottom);
                writer.Write((short)Right);
                writer.Write((short)Top);
                writer.Write((short)Left);
            }),
            WmfRecord(0x0104, writer => writer.Write((short)2)), // META_SETROP2: not drawn, just recorded
            WmfRecord(0x0000, _ => { }), // META_EOF
        };

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // WmfPlaceableFileHeader: key, hmf, bbox (left, top, right, bottom), inch, reserved, checksum.
        writer.Write(0x9AC6CDD7u);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)100);
        writer.Write((short)100);
        writer.Write((short)96);
        writer.Write(0);
        writer.Write((short)0);

        var headerWords = 9;
        var totalWords = headerWords + records.Sum(r => r.Length / 2);
        var maxRecordWords = records.Max(r => r.Length / 2);

        // METAHEADER: type (memory), headerSize, version, size, noObjects, maxRecord, noParameters.
        writer.Write((short)1);
        writer.Write((short)9);
        writer.Write((short)0x300);
        writer.Write(totalWords);
        writer.Write((short)1);
        writer.Write(maxRecordWords);
        writer.Write((short)0);

        foreach (var record in records) writer.Write(record);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] WmfRecord(int function, Action<BinaryWriter> writeParams)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(0); // placeholder for the size field (in words, filled in below)
        writer.Write((ushort)function);
        writeParams(writer);
        writer.Flush();

        var bytes = stream.ToArray();
        var sizeInWords = BitConverter.GetBytes(bytes.Length / 2);
        Array.Copy(sizeInWords, 0, bytes, 0, 4);
        return bytes;
    }

    // ── An EMF with the same picture: one solid-red rectangle selected through
    // CreateBrushIndirect/SelectObject, one unsupported record (SetIcmMode) and an EOF. ──────────
    private static byte[] BuildEmf()
    {
        var records = new List<byte[]>
        {
            EmrRecord(39, writer => // EMR_CREATEBRUSHINDIRECT
            {
                writer.Write(1); // ihBrush
                writer.Write(0); // lbStyle: BS_SOLID
                writer.Write(ColorRefRed);
                writer.Write(0); // lbHatch
            }),
            EmrRecord(37, writer => writer.Write(1)), // EMR_SELECTOBJECT: ihObject
            EmrRecord(43, writer => // EMR_RECTANGLE: left, top, right, bottom
            {
                writer.Write(Left);
                writer.Write(Top);
                writer.Write(Right);
                writer.Write(Bottom);
            }),
            EmrRecord(98, writer => writer.Write(1)), // EMR_SETICMMODE: not drawn, just recorded
            EmrRecord(14, writer => // EMR_EOF: nPalEntries, offPalEntries, nSizeLast
            {
                writer.Write(0);
                writer.Write(12);
                writer.Write(20);
            }),
        };

        const int headerSize = 88;
        var totalBytes = headerSize + records.Sum(r => r.Length);
        var totalRecords = 1 + records.Count;

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(1); // iType: EMR_HEADER
        writer.Write(headerSize); // nSize
        writer.Write(0); writer.Write(0); writer.Write(100); writer.Write(100); // rclBounds
        writer.Write(0); writer.Write(0); writer.Write(2646); writer.Write(2646); // rclFrame, .01 mm
        writer.Write(0x464D4520u); // dSignature "EMF "
        writer.Write(0x10000); // nVersion
        writer.Write(totalBytes); // nBytes
        writer.Write(totalRecords); // nRecords
        writer.Write((short)1); // nHandles
        writer.Write((short)0); // sReserved
        writer.Write(0); // nDescription
        writer.Write(0); // offDescription
        writer.Write(0); // nPalEntries
        writer.Write(96); writer.Write(96); // szlDevice
        writer.Write(25); writer.Write(25); // szlMillimeters

        foreach (var record in records) writer.Write(record);

        writer.Flush();
        return stream.ToArray();
    }

    private static byte[] EmrRecord(int type, Action<BinaryWriter> writeParams)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(type);
        writer.Write(0); // placeholder for nSize, filled in below
        writeParams(writer);
        writer.Flush();

        var bytes = stream.ToArray();
        var size = BitConverter.GetBytes(bytes.Length);
        Array.Copy(size, 0, bytes, 4, 4);
        return bytes;
    }
}
