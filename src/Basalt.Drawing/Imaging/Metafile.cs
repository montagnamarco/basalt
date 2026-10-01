using System.Drawing.Imaging.Metafiles;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>A Windows or enhanced metafile.</summary>
/// <remarks>
/// <para><b>Reading and rendering work.</b> EMF and WMF are published record formats (MS-EMF,
/// MS-WMF), so this type parses them and replays the records onto a Skia canvas itself rather than
/// asking GDI to do it. A metafile loaded from a file or a stream rasterises into its own pixels at
/// construction, the same way a decoded <see cref="Bitmap"/> does, so it can be read like any other
/// <see cref="Image"/>.</para>
/// <para><b>Recording does not.</b> Creating a metafile by drawing into it needs the drawing API to
/// emit records instead of Skia calls; those constructors throw, as does anything that needs a
/// native HDC or HENHMETAFILE handle. EMF+ records, which travel inside EMF comment records, are
/// not decoded -- rendering the EMF half of a dual metafile is what a downlevel GDI renderer does,
/// so the picture is right rather than absent.</para>
/// <para>See <see cref="Imaging.Metafiles.MetafilePlayer"/> for exactly which records the players
/// draw.</para>
/// </remarks>
/// <remarks>
/// Ported from Majorsilence.Forms.Drawing.Common
/// (Majorsilence.Forms.Drawing.Common/Metafiles/Metafile.cs), MIT licensed, copyright Jonathan
/// Pobst and Peter Gill (Majorsilence). Adapted to this assembly's base class (<see cref="Image"/>
/// requires its pixels at construction, so parsing and the first <see cref="Play"/> both happen
/// before the base constructor runs) and to the exact signatures System.Drawing.Common exposes.
/// </remarks>
public sealed class Metafile : Image
{
    private readonly MetafileHeader _header;
    private readonly List<MetafileRecord> _records;
    private readonly bool _isWmf;

    /// <summary>Initializes a new instance of the <see cref="Metafile"/> class from a file.</summary>
    public Metafile(string filename) : this(Parse(ReadFile(filename)))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="Metafile"/> class from a stream.</summary>
    public Metafile(Stream stream) : this(Parse(ReadStream(stream)))
    {
    }

    private Metafile(Parsed parsed) : base(parsed.Pixels, PixelFormat.Format32bppArgb, parsed.Format, parsed.Bytes)
    {
        _header = parsed.Header;
        _records = parsed.Records;
        _isWmf = parsed.IsWmf;
        UnsupportedRecordTypes = parsed.Unsupported;
    }

    // Image requires its pixels at construction, with no parameterless base constructor to chain
    // to; every recording constructor below throws before this one pixel is ever drawn to, so the
    // placeholder bitmap it allocates is never observed.
    private Metafile() : base(new SKBitmap(1, 1, SKColorType.Bgra8888, SKAlphaType.Premul), PixelFormat.Format32bppArgb, ImageFormat.Emf, null)
    {
        _header = new MetafileHeader();
        _records = [];
        UnsupportedRecordTypes = [];
    }

    /// <summary>The types of the records this metafile carries that the player did not draw.</summary>
    /// <remarks>Empty for a metafile this layer fully understands. A non-empty list is how a file
    /// using records outside the supported set makes itself visible, rather than quietly rendering
    /// a partial picture with no trace of what was left out. Internal: not part of GDI+'s own
    /// surface, so a test reads it rather than a caller.</remarks>
    internal List<EmfPlusRecordType> UnsupportedRecordTypes { get; }

    /// <summary>The number of records read from the metafile.</summary>
    internal int RecordCount => _records.Count;

    /// <summary>Replays this metafile's records onto a canvas, scaled from its recorded bounds to
    /// <paramref name="destination"/>.</summary>
    internal void Play(SKCanvas canvas, SKRect destination)
    {
        var bounds = _header.Bounds;
        var output = OutputTransform(bounds, destination);
        var extent = new SKSize(
            bounds.Width > 0 ? bounds.Width : destination.Width,
            bounds.Height > 0 ? bounds.Height : destination.Height);

        if (_isWmf) new WmfPlayer(canvas, output, extent, UnsupportedRecordTypes).Play(_records);
        else new EmfPlayer(canvas, output, extent, UnsupportedRecordTypes).Play(_records);
    }

    /// <summary>Enumerates this metafile's records as <see cref="Graphics.EnumerateMetafile"/> does,
    /// converting each record's raw, format-specific code to the shared enumeration.</summary>
    internal IEnumerable<(EmfPlusRecordType Type, byte[] Data)> EnumerateRecords()
    {
        foreach (var record in _records)
            yield return (_isWmf ? (EmfPlusRecordType)(record.Type + 0x10000) : (EmfPlusRecordType)record.Type, record.Data);
    }

    // The size to rasterise at when nobody asked for a specific one: the recorded bounds, read as
    // if at 96 dots per inch, clamped to something SkiaSharp will allocate without complaint.
    private static Size DefaultSize(MetafileHeader header)
    {
        var bounds = header.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0) return new Size(1, 1);

        return new Size(
            Math.Clamp((int)Math.Round((double)bounds.Width), 1, 8192),
            Math.Clamp((int)Math.Round((double)bounds.Height), 1, 8192));
    }

    private static SKMatrix OutputTransform(Rectangle bounds, SKRect destination)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return SKMatrix.CreateTranslation(destination.Left, destination.Top);

        var sx = destination.Width / bounds.Width;
        var sy = destination.Height / bounds.Height;

        return SKMatrix.CreateTranslation(destination.Left, destination.Top)
            .PreConcat(SKMatrix.CreateScale(sx, sy))
            .PreConcat(SKMatrix.CreateTranslation(-bounds.X, -bounds.Y));
    }

    // Parses the header and records, then plays them once to produce the pixels Image requires at
    // construction -- there is no way to hand Image a bitmap later, so the first Play happens here.
    private static Parsed Parse(byte[] bytes)
    {
        var header = GetMetafileHeader(new MemoryStream(bytes));

        // Neither an EMF nor a WMF: GDI+ refuses it rather than giving back a blank picture.
        if (header.Type == MetafileType.Invalid) throw new ArgumentException("Parameter is not valid.");

        var isWmf = header.IsWmf();
        var records = isWmf ? WmfReader.Read(bytes) : header.IsEmfOrEmfPlus() ? EmfReader.Read(bytes) : [];
        var size = DefaultSize(header);
        var bitmap = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var unsupported = new List<EmfPlusRecordType>();

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            var destination = new SKRect(0, 0, size.Width, size.Height);
            var output = OutputTransform(header.Bounds, destination);
            var bounds = header.Bounds;
            var extent = new SKSize(
                bounds.Width > 0 ? bounds.Width : destination.Width,
                bounds.Height > 0 ? bounds.Height : destination.Height);

            if (isWmf) new WmfPlayer(canvas, output, extent, unsupported).Play(records);
            else new EmfPlayer(canvas, output, extent, unsupported).Play(records);
        }

        return new Parsed(bytes, header, records, isWmf, isWmf ? ImageFormat.Wmf : ImageFormat.Emf, bitmap, unsupported);
    }

    private static byte[] ReadFile(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);
        return File.ReadAllBytes(filename);
    }

    private static byte[] ReadStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private readonly record struct Parsed(
        byte[] Bytes,
        MetafileHeader Header,
        List<MetafileRecord> Records,
        bool IsWmf,
        ImageFormat Format,
        SKBitmap Pixels,
        List<EmfPlusRecordType> Unsupported);

    // ── Recording constructors: a Windows GDI facility with no Skia equivalent ─────────────────

    /// <summary>Initializes a new instance that wraps a native enhanced-metafile handle.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public Metafile(IntPtr henhmetafile, bool deleteEmf) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,bool)"/>
    public Metafile(IntPtr hmetafile, WmfPlaceableFileHeader wmfHeader) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,bool)"/>
    public Metafile(IntPtr hmetafile, WmfPlaceableFileHeader wmfHeader, bool deleteWmf) : this() => throw NoHandles();

    /// <summary>Initializes a new instance that records drawing against a device context.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public Metafile(IntPtr referenceHdc, EmfType emfType) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, EmfType emfType, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, Rectangle frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, RectangleF frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(string fileName, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, string? desc) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, Rectangle frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    /// <inheritdoc cref="Metafile(IntPtr,EmfType)"/>
    public Metafile(Stream stream, IntPtr referenceHdc, RectangleF frameRect, MetafileFrameUnit frameUnit, EmfType type, string? description) : this() => throw NoHandles();

    // ── Headers ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns this metafile's header.</summary>
    public MetafileHeader GetMetafileHeader() => _header;

    /// <summary>Reads a metafile's header from a file.</summary>
    public static MetafileHeader GetMetafileHeader(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        using var stream = File.OpenRead(fileName);
        return GetMetafileHeader(stream);
    }

    /// <summary>Reads a metafile's header from a stream.</summary>
    /// <remarks>Real parsing, not a stub: the EMF and placeable-WMF headers are fixed little-endian
    /// layouts, so the kind, size, bounds and resolution all come out of the bytes.</remarks>
    public static MetafileHeader GetMetafileHeader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var bytes = new byte[88];
        var read = stream.Read(bytes, 0, bytes.Length);
        var result = new MetafileHeader();

        if (read < 4) return result;

        var signature = BitConverter.ToUInt32(bytes, 0);

        // 0x9AC6CDD7 is the placeable-WMF magic; EMF starts with record type 1 (EMR_HEADER) and
        // carries "  EMF" at offset 40.
        if (signature == 0x9AC6CDD7 && read >= 22)
        {
            result.Type = MetafileType.WmfPlaceable;
            result.Version = 0x300;
            result.WmfHeader = new MetaHeader { Type = BitConverter.ToInt16(bytes, 22) };

            // Key at 0, handle at 4, the bounding box at 6 to 13, units per inch at 14.
            var inch = BitConverter.ToInt16(bytes, 14);
            result.DpiX = result.DpiY = inch == 0 ? 96f : inch;
            result.LogicalDpiX = result.LogicalDpiY = (int)result.DpiX;
            result.Bounds = new Rectangle(
                BitConverter.ToInt16(bytes, 6),
                BitConverter.ToInt16(bytes, 8),
                BitConverter.ToInt16(bytes, 10) - BitConverter.ToInt16(bytes, 6),
                BitConverter.ToInt16(bytes, 12) - BitConverter.ToInt16(bytes, 8));

            return result;
        }

        if (signature == 1 && read >= 88 && BitConverter.ToUInt32(bytes, 40) == 0x464D4520)
        {
            result.Type = MetafileType.Emf;
            result.MetafileSize = BitConverter.ToInt32(bytes, 48);
            result.Version = BitConverter.ToInt32(bytes, 44);
            result.Bounds = new Rectangle(
                BitConverter.ToInt32(bytes, 8),
                BitConverter.ToInt32(bytes, 12),
                BitConverter.ToInt32(bytes, 16) - BitConverter.ToInt32(bytes, 8),
                BitConverter.ToInt32(bytes, 20) - BitConverter.ToInt32(bytes, 12));

            // Device pixels over device millimetres, times 25.4, is dots per inch.
            var pixels = BitConverter.ToInt32(bytes, 72);
            var millimetres = BitConverter.ToInt32(bytes, 80);
            result.DpiX = result.DpiY = millimetres == 0 ? 96f : pixels * 25.4f / millimetres;
            result.LogicalDpiX = result.LogicalDpiY = (int)result.DpiX;

            return result;
        }

        result.Type = MetafileType.Invalid;
        return result;
    }

    /// <inheritdoc cref="GetMetafileHeader(string)"/>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public static MetafileHeader GetMetafileHeader(IntPtr henhmetafile) => throw NoHandles();

    /// <inheritdoc cref="GetMetafileHeader(IntPtr)"/>
    public static MetafileHeader GetMetafileHeader(IntPtr hmetafile, WmfPlaceableFileHeader wmfHeader) => throw NoHandles();

    /// <summary>Returns a handle to this metafile.</summary>
    /// <exception cref="PlatformNotSupportedException">Always.</exception>
    public IntPtr GetHenhmetafile() => throw NoHandles();

    /// <summary>Plays a single metafile record into a device context.</summary>
    /// <exception cref="PlatformNotSupportedException">Always: there is no device context to play
    /// into off Windows. Reading or replaying a whole metafile onto a Skia canvas does work -- see
    /// <see cref="GetMetafileHeader()"/> and <see cref="Graphics.EnumerateMetafile"/>.</exception>
    public void PlayRecord(EmfPlusRecordType recordType, int flags, int dataSize, byte[] data) => throw NoHandles();

    private static PlatformNotSupportedException NoHandles() => new(Unsupported.NativeHandles);
}
