using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.Serialization;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A picture made of pixels.</summary>
[TypeConverter(typeof(ImageConverter))]
public abstract class Image : MarshalByRefObject, ICloneable, IDisposable, ISerializable
{
    /// <summary>Whether <see cref="GetThumbnailImage"/> should keep making a thumbnail.</summary>
    public delegate bool GetThumbnailImageAbort();

    // Premultiplied BGRA, because a Skia canvas cannot draw on anything else.
    // The cost is that a pixel set with a very low alpha does not read back
    // with exactly the colour it was given, which GDI+ does preserve.
    private SKBitmap? _pixels;

    // The bytes an image was decoded from, kept only so GetFrameCount,
    // SelectActiveFrame and FrameDimensionsList can look at frames the initial
    // decode did not need. Null for an image that was never decoded from a
    // file or stream (a fresh Bitmap, one built from another Image, and so on).
    private readonly byte[]? _source;

    private readonly Dictionary<int, PropertyItem> _propertyItems = new();

    private ColorPalette _palette = new();

    private protected Image(SKBitmap pixels, PixelFormat format, ImageFormat rawFormat, byte[]? source = null)
    {
        _pixels = pixels;
        _source = source;
        PixelFormat = format;
        RawFormat = rawFormat;

        AddFrameProperties();
    }

    // The property tags GDI+ fills in for an animated image: each frame's delay
    // in hundredths of a second, and how many times the animation repeats.
    private const int FrameDelayTag = 0x5100;
    private const int LoopCountTag = 0x5101;

    /// <summary>
    /// Records an animation's frame delays and loop count as property items,
    /// where ImageAnimator and applications read them, as GDI+ does on decoding.
    /// </summary>
    private void AddFrameProperties()
    {
        if (_source is null) return;

        using var codec = SKCodec.Create(SKData.CreateCopy(_source));
        if (codec is null || codec.FrameCount < 2) return;

        var delays = new byte[codec.FrameCount * 4];
        for (var i = 0; i < codec.FrameCount; i++)
        {
            // Skia gives milliseconds; GDI+ keeps hundredths of a second.
            BitConverter.TryWriteBytes(delays.AsSpan(i * 4), codec.FrameInfo[i].Duration / 10);
        }

        _propertyItems[FrameDelayTag] = new PropertyItem { Id = FrameDelayTag, Type = 4, Len = delays.Length, Value = delays };

        // Skia answers -1 for "forever", which GIF writes as 0.
        var loops = (short)Math.Max(0, codec.RepetitionCount);
        _propertyItems[LoopCountTag] = new PropertyItem { Id = LoopCountTag, Type = 3, Len = 2, Value = BitConverter.GetBytes(loops) };
    }

    ~Image() => Dispose(false);

    internal SKBitmap Pixels => _pixels ?? throw new ArgumentException("Parameter is not valid.");

    // How many Graphics and locks hold on to these pixels right now.
    private int _users;

    /// <summary>
    /// Told by a Graphics or a lock that it has started, or stopped, using the
    /// pixels of this image where they are.
    /// </summary>
    internal void MarkInUse() => _users++;

    internal void MarkFree() => _users--;

    /// <summary>
    /// Refuses to swap the pixels for others while something holds on to them.
    /// </summary>
    /// <remarks>
    /// Rotating an image, or moving to another frame, replaces the block of
    /// pixels; a Graphics made earlier would go on drawing into the old block,
    /// which is freed, and a lock would write its rectangle back into the new one. GDI+ refuses with this same exception and message.
    /// </remarks>
    private void CheckNotInUse()
    {
        if (_users > 0) throw new InvalidOperationException("Object is currently in use elsewhere.");
    }

    /// <summary>Frees what the subclass holds outside the garbage collector's reach.</summary>
    private protected virtual void ReleaseUnmanaged()
    {
    }

    public int Width => Pixels.Width;

    public int Height => Pixels.Height;

    public Size Size => new(Width, Height);

    /// <summary>The width and height, in pixels, of this image.</summary>
    public SizeF PhysicalDimension => new(Width, Height);

    public PixelFormat PixelFormat { get; }

    public ImageFormat RawFormat { get; }

    public object? Tag { get; set; }

    public float HorizontalResolution { get; private set; } = 96f;

    public float VerticalResolution { get; private set; } = 96f;

    /// <summary>
    /// A rough approximation of GDI+'s own flags: this library does not track
    /// colour space or scalability the way the native codecs do, so only what
    /// can be answered from the pixel format itself is reported.
    /// </summary>
    public int Flags
    {
        get
        {
            var flags = ImageFlags.ColorSpaceRgb;
            if (IsAlphaPixelFormat(PixelFormat)) flags |= ImageFlags.HasAlpha;
            return (int)flags;
        }
    }

    public Guid[] FrameDimensionsList => [FrameCount() > 1 ? FrameDimension.Time.Guid : FrameDimension.Page.Guid];

    public ColorPalette Palette
    {
        get => _palette;
        set => _palette = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int[] PropertyIdList
    {
        get
        {
            var ids = new int[_propertyItems.Count];
            _propertyItems.Keys.CopyTo(ids, 0);
            Array.Sort(ids);
            return ids;
        }
    }

    public PropertyItem[] PropertyItems
    {
        get
        {
            var ids = PropertyIdList;
            var items = new PropertyItem[ids.Length];
            for (var i = 0; i < ids.Length; i++) items[i] = _propertyItems[ids[i]];
            return items;
        }
    }

    public static Image FromStream(Stream stream) => FromStream(stream, useEmbeddedColorManagement: false);

    public static Image FromStream(Stream stream, bool useEmbeddedColorManagement) =>
        FromStream(stream, useEmbeddedColorManagement, validateImageData: true);

    public static Image FromStream(Stream stream, bool useEmbeddedColorManagement, bool validateImageData)
    {
        // Neither flag changes anything this library does: there is no ICC
        // colour management to apply, and decoding always validates as far as
        // SKCodec is willing to. They are accepted so existing calls still compile.
        ArgumentNullException.ThrowIfNull(stream);

        return new Bitmap(stream);
    }

    public static Image FromFile(string filename) => FromFile(filename, useEmbeddedColorManagement: false);

    public static Image FromFile(string filename, bool useEmbeddedColorManagement) => new Bitmap(filename);

    public static Bitmap FromHbitmap(IntPtr hbitmap) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Bitmap FromHbitmap(IntPtr hbitmap, IntPtr hpalette) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static int GetPixelFormatSize(PixelFormat pixfmt) => ((int)pixfmt >> 8) & 0xff;

    public static bool IsAlphaPixelFormat(PixelFormat pixfmt) => ((int)pixfmt & (int)PixelFormat.Alpha) != 0;

    public static bool IsCanonicalPixelFormat(PixelFormat pixfmt) => ((int)pixfmt & (int)PixelFormat.Canonical) != 0;

    public static bool IsExtendedPixelFormat(PixelFormat pixfmt) => ((int)pixfmt & (int)PixelFormat.Extended) != 0;

    public object Clone() => new Bitmap(Pixels.Copy(), PixelFormat, RawFormat);

    public RectangleF GetBounds(ref GraphicsUnit pageUnit)
    {
        pageUnit = GraphicsUnit.Pixel;
        return new RectangleF(0, 0, Width, Height);
    }

    /// <summary>
    /// A scaled copy. GDI+ lets a caller abort a slow thumbnail through
    /// <paramref name="callback"/>; this library's resize is one call into
    /// Skia and finishes before the callback could ever be asked, so it is
    /// accepted but never invoked.
    /// </summary>
    public Image GetThumbnailImage(int thumbWidth, int thumbHeight, GetThumbnailImageAbort? callback, IntPtr callbackData)
    {
        if (thumbWidth <= 0 || thumbHeight <= 0) throw new ArgumentException("Parameter is not valid.");

        var info = new SKImageInfo(thumbWidth, thumbHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        var resized = thumbWidth == Width && thumbHeight == Height
            ? Pixels.Copy()
            : Pixels.Resize(info, SKSamplingOptions.Default) ?? throw new ArgumentException("Parameter is not valid.");

        return new Bitmap(resized, PixelFormat, ImageFormat.MemoryBmp);
    }

    /// <summary>
    /// Turns the image 90, 180 or 270 degrees clockwise, mirrors it horizontally,
    /// or both, in place.
    /// </summary>
    /// <remarks>
    /// RotateFlipType packs 8 distinct transforms into 16 named values: bits 0-1
    /// give the clockwise rotation in quarter turns, bit 2 says whether a
    /// horizontal mirror (GDI+'s "FlipX") is applied before rotating. That
    /// reading was checked against the identities GDI+'s own aliases encode -
    /// for instance RotateNoneFlipX and Rotate180FlipY name the same integer
    /// value, which only holds if a horizontal mirror equals a 180-degree
    /// rotation of a vertical mirror - and held for all of them.
    /// </remarks>
    public void RotateFlip(RotateFlipType rotateFlipType)
    {
        CheckNotInUse();

        var value = (int)rotateFlipType;
        var rotationSteps = value & 3;
        var flipHorizontal = (value & 4) != 0;

        var source = Pixels;
        var (w, h) = (source.Width, source.Height);
        var (dw, dh) = rotationSteps is 1 or 3 ? (h, w) : (w, h);

        var destination = new SKBitmap(new SKImageInfo(dw, dh, SKColorType.Bgra8888, SKAlphaType.Premul));

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var x2 = flipHorizontal ? w - 1 - x : x;
                var y2 = y;

                var (dx, dy) = rotationSteps switch
                {
                    0 => (x2, y2),
                    1 => (h - 1 - y2, x2),
                    2 => (w - 1 - x2, h - 1 - y2),
                    _ => (y2, w - 1 - x2),
                };

                destination.SetPixel(dx, dy, source.GetPixel(x, y));
            }
        }

        _pixels = destination;
        source.Dispose();
    }

    public void Save(string filename) => Save(filename, FormatFromExtension(filename));

    public void Save(string filename, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(format);

        using var file = File.Create(filename);
        SaveCore(file, format, quality: 75);
    }

    public void Save(string filename, ImageCodecInfo encoder, EncoderParameters? encoderParams)
    {
        ArgumentNullException.ThrowIfNull(filename);
        ArgumentNullException.ThrowIfNull(encoder);

        using var file = File.Create(filename);
        SaveCore(file, new ImageFormat(encoder.FormatID), QualityFrom(encoderParams));
    }

    public void Save(Stream stream, ImageFormat format)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(format);

        SaveCore(stream, format, quality: 75);
    }

    public void Save(Stream stream, ImageCodecInfo encoder, EncoderParameters? encoderParams)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(encoder);

        SaveCore(stream, new ImageFormat(encoder.FormatID), QualityFrom(encoderParams));
    }

    public void SaveAdd(EncoderParameters? encoderParams) =>
        throw new NotImplementedException("Adding a frame to a multi-frame Save is not implemented yet: only a single-frame Save is.");

    public void SaveAdd(Image image, EncoderParameters? encoderParams) =>
        throw new NotImplementedException("Adding a frame to a multi-frame Save is not implemented yet: only a single-frame Save is.");

    public int GetFrameCount(FrameDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(dimension);

        return FrameCount();
    }

    /// <returns>0, the GDI+ status code for success, as GDI+'s own interop calls do.</returns>
    public int SelectActiveFrame(FrameDimension dimension, int index)
    {
        CheckNotInUse();

        ArgumentNullException.ThrowIfNull(dimension);

        var count = FrameCount();
        if (index < 0 || index >= count) throw new ArgumentException("Parameter is not valid.");
        if (_source is null) return 0; // The only frame there is, and it is already selected.

        using var codec = SKCodec.Create(SKData.CreateCopy(_source)) ?? throw new ArgumentException("Parameter is not valid.");
        var info = codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul);
        var frame = new SKBitmap(info);
        var result = codec.GetPixels(info, frame.GetPixels(), frame.RowBytes, new SKCodecOptions(index));
        if (result is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            frame.Dispose();
            throw new ArgumentException("Parameter is not valid.");
        }

        _pixels?.Dispose();
        _pixels = frame;
        return 0;
    }

    public PropertyItem GetPropertyItem(int propid) =>
        _propertyItems.TryGetValue(propid, out var item) ? item : throw new ArgumentException("Property not found.");

    public void SetPropertyItem(PropertyItem propitem)
    {
        ArgumentNullException.ThrowIfNull(propitem);

        _propertyItems[propitem.Id] = propitem;
    }

    public void RemovePropertyItem(int propid)
    {
        if (!_propertyItems.Remove(propid)) throw new ArgumentException("Property not found.");
    }

    public EncoderParameters GetEncoderParameterList(Guid encoder)
    {
        var codec = Array.Find(ImageCodecInfo.GetImageEncoders(), c => c.Clsid == encoder)
            ?? throw new ArgumentException("Parameter is not valid.");

        if (codec.FormatID != ImageFormat.Jpeg.Guid) return new EncoderParameters(0);

        // The one parameter this library's own Save reads: JPEG quality, 0-100.
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 0L, 100L);
        return parameters;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        ReleaseUnmanaged();

        if (!disposing) return;

        _pixels?.Dispose();
        _pixels = null;
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        using var stream = new MemoryStream();
        Save(stream, ImageFormat.Png);
        info.AddValue("Data", stream.ToArray(), typeof(byte[]));
    }

    private protected void SetResolutionCore(float xDpi, float yDpi)
    {
        HorizontalResolution = xDpi;
        VerticalResolution = yDpi;
    }

    private int FrameCount()
    {
        if (_source is null) return 1;

        using var codec = SKCodec.Create(SKData.CreateCopy(_source));
        return codec is null ? 1 : Math.Max(1, codec.FrameCount);
    }

    private void SaveCore(Stream stream, ImageFormat format, int quality)
    {
        if (format.Guid == ImageFormat.Png.Guid) { using var data = Pixels.Encode(SKEncodedImageFormat.Png, quality); data.SaveTo(stream); return; }
        if (format.Guid == ImageFormat.Jpeg.Guid) { using var data = Pixels.Encode(SKEncodedImageFormat.Jpeg, quality); data.SaveTo(stream); return; }
        if (format.Guid == ImageFormat.Webp.Guid) { using var data = Pixels.Encode(SKEncodedImageFormat.Webp, quality); data.SaveTo(stream); return; }
        if (format.Guid == ImageFormat.Bmp.Guid) { BmpWriter.Write(stream, Pixels, PixelFormat); return; }

        // Not PlatformNotSupportedException: that one says "never off Windows",
        // and code catches it to mean exactly that.
        throw new NotImplementedException($"Saving as {format} is not implemented yet: Png, Jpeg, Webp and Bmp are.");
    }

    private static int QualityFrom(EncoderParameters? parameters)
    {
        if (parameters?.Param is null) return 75;

        foreach (var parameter in parameters.Param)
        {
            if (parameter is not null && parameter.Encoder.Guid == Encoder.Quality.Guid && parameter.Values.Length > 0)
            {
                return (int)parameter.Values[0];
            }
        }

        return 75;
    }

    /// <summary>
    /// The format Image.Save(string) writes when only a name is given: GDI+
    /// reads it from the file's own extension, defaulting to PNG for one it
    /// does not recognise.
    /// </summary>
    private static ImageFormat FormatFromExtension(string filename)
    {
        ArgumentNullException.ThrowIfNull(filename);

        var extension = Path.GetExtension(filename).TrimStart('.').ToUpperInvariant();
        return extension switch
        {
            "JPG" or "JPEG" or "JPE" or "JFIF" => ImageFormat.Jpeg,
            "GIF" => ImageFormat.Gif,
            "BMP" or "DIB" or "RLE" => ImageFormat.Bmp,
            "TIF" or "TIFF" => ImageFormat.Tiff,
            "ICO" => ImageFormat.Icon,
            "WEBP" => ImageFormat.Webp,
            _ => ImageFormat.Png,
        };
    }
}
