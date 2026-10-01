using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Imaging.Effects;
using System.Numerics;
using SkiaSharp;

namespace System.Drawing;

/// <summary>Drawing images: at their size, into a rectangle, or onto a parallelogram.</summary>
/// <remarks>
/// Every overload comes down to one mapping: a rectangle of the source image,
/// in its pixels, onto three corners of the destination (upper-left,
/// upper-right, lower-left) in the caller's coordinates. Drawn at its own
/// size, an image keeps its physical size, as in GDI+: a 72 dpi image is drawn
/// a third larger than its pixel count on this 96 dpi surface.
/// </remarks>
public sealed partial class Graphics
{
    /// <summary>Asked before an image is drawn; true stops the drawing.</summary>
    public delegate bool DrawImageAbort(IntPtr callbackdata);

    public void DrawImage(Image image, Point point) => DrawImage(image, (float)point.X, point.Y);

    public void DrawImage(Image image, PointF point) => DrawImage(image, point.X, point.Y);

    public void DrawImage(Image image, int x, int y) => DrawImage(image, (float)x, y);

    public void DrawImage(Image image, float x, float y)
    {
        ArgumentNullException.ThrowIfNull(image);

        var size = PhysicalSize(image, image.Width, image.Height);
        DrawImageCore(image, Corners(new RectangleF(x, y, size.Width, size.Height)), WholeImage(image), null);
    }

    public void DrawImage(Image image, Rectangle rect) => DrawImage(image, (RectangleF)rect);

    public void DrawImage(Image image, RectangleF rect)
    {
        ArgumentNullException.ThrowIfNull(image);

        DrawImageCore(image, Corners(rect), WholeImage(image), null);
    }

    public void DrawImage(Image image, int x, int y, int width, int height) => DrawImage(image, new RectangleF(x, y, width, height));

    public void DrawImage(Image image, float x, float y, float width, float height) => DrawImage(image, new RectangleF(x, y, width, height));

    public void DrawImage(Image image, Point[] destPoints) => DrawImage(image, ToPointFArray(destPoints));

    public void DrawImage(Image image, PointF[] destPoints)
    {
        ArgumentNullException.ThrowIfNull(image);

        DrawImageCore(image, Parallelogram(destPoints), WholeImage(image), null);
    }

    public void DrawImage(Image image, int x, int y, Rectangle srcRect, GraphicsUnit srcUnit) =>
        DrawImage(image, (float)x, y, (RectangleF)srcRect, srcUnit);

    /// <summary>A part of the image at its physical size.</summary>
    public void DrawImage(Image image, float x, float y, RectangleF srcRect, GraphicsUnit srcUnit)
    {
        ArgumentNullException.ThrowIfNull(image);

        var source = SourceInPixels(image, srcRect, srcUnit);
        var size = PhysicalSize(image, source.Width, source.Height);
        DrawImageCore(image, Corners(new RectangleF(x, y, size.Width, size.Height)), source, null);
    }

    public void DrawImage(Image image, Rectangle destRect, Rectangle srcRect, GraphicsUnit srcUnit) =>
        DrawImage(image, (RectangleF)destRect, (RectangleF)srcRect, srcUnit);

    public void DrawImage(Image image, RectangleF destRect, RectangleF srcRect, GraphicsUnit srcUnit)
    {
        ArgumentNullException.ThrowIfNull(image);

        DrawImageCore(image, Corners(destRect), SourceInPixels(image, srcRect, srcUnit), null);
    }

    public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit) =>
        DrawImage(image, ToPointFArray(destPoints), (RectangleF)srcRect, srcUnit);

    public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit)
    {
        ArgumentNullException.ThrowIfNull(image);

        DrawImageCore(image, Parallelogram(destPoints), SourceInPixels(image, srcRect, srcUnit), null);
    }

    public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit) =>
        DrawImage(image, (RectangleF)destRect, new RectangleF(srcX, srcY, srcWidth, srcHeight), srcUnit);

    public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit) =>
        DrawImage(image, (RectangleF)destRect, new RectangleF(srcX, srcY, srcWidth, srcHeight), srcUnit);

    public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr) =>
        DrawImage(image, ToPointFArray(destPoints), (RectangleF)srcRect, srcUnit, imageAttr, null, 0);

    public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr, DrawImageAbort? callback) =>
        DrawImage(image, ToPointFArray(destPoints), (RectangleF)srcRect, srcUnit, imageAttr, callback, 0);

    public void DrawImage(Image image, Point[] destPoints, Rectangle srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr, DrawImageAbort? callback, int callbackData) =>
        DrawImage(image, ToPointFArray(destPoints), (RectangleF)srcRect, srcUnit, imageAttr, callback, callbackData);

    public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr) =>
        DrawImage(image, destPoints, srcRect, srcUnit, imageAttr, null, 0);

    public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr, DrawImageAbort? callback) =>
        DrawImage(image, destPoints, srcRect, srcUnit, imageAttr, callback, 0);

    public void DrawImage(Image image, PointF[] destPoints, RectangleF srcRect, GraphicsUnit srcUnit, ImageAttributes? imageAttr, DrawImageAbort? callback, int callbackData)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (Aborted(callback, callbackData)) return;

        DrawWithAttributes(image, Parallelogram(destPoints), SourceInPixels(image, srcRect, srcUnit), imageAttr);
    }

    public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttr) =>
        DrawImage(image, destRect, (float)srcX, srcY, srcWidth, srcHeight, srcUnit, imageAttr, null, IntPtr.Zero);

    public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttr, DrawImageAbort? callback) =>
        DrawImage(image, destRect, (float)srcX, srcY, srcWidth, srcHeight, srcUnit, imageAttr, callback, IntPtr.Zero);

    public void DrawImage(Image image, Rectangle destRect, int srcX, int srcY, int srcWidth, int srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttrs, DrawImageAbort? callback, IntPtr callbackData) =>
        DrawImage(image, destRect, (float)srcX, srcY, srcWidth, srcHeight, srcUnit, imageAttrs, callback, callbackData);

    public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttrs) =>
        DrawImage(image, destRect, srcX, srcY, srcWidth, srcHeight, srcUnit, imageAttrs, null, IntPtr.Zero);

    public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttrs, DrawImageAbort? callback) =>
        DrawImage(image, destRect, srcX, srcY, srcWidth, srcHeight, srcUnit, imageAttrs, callback, IntPtr.Zero);

    public void DrawImage(Image image, Rectangle destRect, float srcX, float srcY, float srcWidth, float srcHeight, GraphicsUnit srcUnit, ImageAttributes? imageAttrs, DrawImageAbort? callback, IntPtr callbackData)
    {
        ArgumentNullException.ThrowIfNull(image);

        if (Aborted(callback, callbackData)) return;

        var source = SourceInPixels(image, new RectangleF(srcX, srcY, srcWidth, srcHeight), srcUnit);
        DrawWithAttributes(image, Corners(destRect), source, imageAttrs);
    }

    public void DrawImage(Image image, Effect effect, RectangleF srcRect = default, Matrix? transform = null, GraphicsUnit srcUnit = GraphicsUnit.Pixel, ImageAttributes? imageAttr = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(effect);

        var source = srcRect.Width <= 0 || srcRect.Height <= 0 ? WholeImage(image) : SourceInPixels(image, srcRect, srcUnit);
        var size = PhysicalSize(image, source.Width, source.Height);

        DrawImageThroughEffect(image, Corners(new RectangleF(0, 0, size.Width, size.Height)), source, effect, imageAttr, transform);
    }

    public void DrawImage(Image image, Effect effect) =>
        DrawImage(image, effect, default, null, GraphicsUnit.Pixel, null);

    /// <summary>Despite the name, scaled to the image's physical size, as GDI+ does.</summary>
    public void DrawImageUnscaled(Image image, Point point) => DrawImage(image, (float)point.X, point.Y);

    public void DrawImageUnscaled(Image image, int x, int y) => DrawImage(image, (float)x, y);

    public void DrawImageUnscaled(Image image, Rectangle rect) => DrawImage(image, (float)rect.X, rect.Y);

    public void DrawImageUnscaled(Image image, int x, int y, int width, int height) => DrawImage(image, (float)x, y);

    /// <summary>The image pixel for pixel, cut to the rectangle when larger.</summary>
    public void DrawImageUnscaledAndClipped(Image image, Rectangle rect)
    {
        ArgumentNullException.ThrowIfNull(image);

        var width = Math.Min(rect.Width, image.Width);
        var height = Math.Min(rect.Height, image.Height);

        // Cut to a rectangle with no room in it, there is nothing to draw.
        if (width <= 0 || height <= 0) return;

        DrawImage(image, new RectangleF(rect.X, rect.Y, width, height), new RectangleF(0, 0, width, height), GraphicsUnit.Pixel);
    }

    public void DrawIcon(Icon icon, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(icon);

        DrawIcon(icon, new Rectangle(x, y, icon.Width, icon.Height));
    }

    /// <summary>The icon's chosen image, scaled to the rectangle.</summary>
    public void DrawIcon(Icon icon, Rectangle targetRect)
    {
        ArgumentNullException.ThrowIfNull(icon);

        DrawPixels(icon.Pixels, Corners(targetRect), new RectangleF(0, 0, icon.Width, icon.Height), null);
    }

    /// <summary>The icon at its own size, cut to the rectangle when larger.</summary>
    public void DrawIconUnstretched(Icon icon, Rectangle targetRect)
    {
        ArgumentNullException.ThrowIfNull(icon);

        var width = Math.Min(targetRect.Width, icon.Width);
        var height = Math.Min(targetRect.Height, icon.Height);

        if (width <= 0 || height <= 0) return;

        DrawPixels(icon.Pixels, Corners(new RectangleF(targetRect.X, targetRect.Y, width, height)), new RectangleF(0, 0, width, height), null);
    }

    public void CopyFromScreen(Point upperLeftSource, Point upperLeftDestination, Size blockRegionSize) =>
        throw new PlatformNotSupportedException(ScreenCapture);

    public void CopyFromScreen(Point upperLeftSource, Point upperLeftDestination, Size blockRegionSize, CopyPixelOperation copyPixelOperation) =>
        throw new PlatformNotSupportedException(ScreenCapture);

    public void CopyFromScreen(int sourceX, int sourceY, int destinationX, int destinationY, Size blockRegionSize) =>
        throw new PlatformNotSupportedException(ScreenCapture);

    public void CopyFromScreen(int sourceX, int sourceY, int destinationX, int destinationY, Size blockRegionSize, CopyPixelOperation copyPixelOperation) =>
        throw new PlatformNotSupportedException(ScreenCapture);

    public static Graphics FromHdc(IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Graphics FromHdc(IntPtr hdc, IntPtr hdevice) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Graphics FromHdcInternal(IntPtr hdc) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Graphics FromHwnd(IntPtr hwnd) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public static Graphics FromHwndInternal(IntPtr hwnd) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>
    /// GDI+ asks the callback while it draws, so that a slow drawing can be
    /// stopped; drawing here is one call, so it is asked once, before.
    /// </summary>
    private static bool Aborted(DrawImageAbort? callback, IntPtr callbackData) => callback is not null && callback(callbackData);

    /// <summary>
    /// The colour adjustments of ImageAttributes for bitmaps, then the
    /// drawing. GDI+ applies the Bitmap category to DrawImage.
    /// </summary>
    private void DrawWithAttributes(Image image, (PointF, PointF, PointF) destination, RectangleF source, ImageAttributes? attributes)
    {
        using var filter = attributes?.ToColorFilter(ColorAdjustType.Bitmap);

        DrawImageCore(image, destination, source, filter, attributes?.Wrap);
    }

    public void DrawCachedBitmap(CachedBitmap cachedBitmap, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(cachedBitmap);

        _canvas.Save();
        try
        {
            if (_clip is not null) _canvas.ClipPath(_clip, SKClipOperation.Intersect, antialias: IsSmooth);
            _canvas.SetMatrix(WorldTranslationToDevice().ToSkia());

            using var pixels = cachedBitmap.Pixels.PeekPixels();
            using var picture = SKImage.FromPixels(pixels);
            _canvas.DrawImage(picture, x, y);
        }
        finally
        {
            _canvas.Restore();
        }
    }

    /// <summary>
    /// The device mapping <see cref="DrawCachedBitmap"/> uses: page unit and
    /// scale, then the canvas's own base matrix, apply as usual, but the
    /// world transform (<see cref="Transform"/>) contributes only its
    /// translation - GDI+ documents DrawCachedBitmap as ignoring the rest of
    /// that transform.
    /// </summary>
    private Matrix3x2 WorldTranslationToDevice()
    {
        var translationOnly = Matrix3x2.CreateTranslation(_worldTransform.M31, _worldTransform.M32);
        var page = Matrix3x2.CreateScale(PixelsPerUnit(_pageUnit) * _pageScale);
        var toPage = MatrixInterop.Combine(translationOnly, page, MatrixOrder.Append);

        return MatrixInterop.Combine(toPage, _baseMatrix, MatrixOrder.Append);
    }

    /// <summary>
    /// Draws a rectangle of the image's pixels through an Effect's filter,
    /// composed with ImageAttributes' own colour filter on top - the effect
    /// reshapes the source first, as Bitmap.ApplyEffect would, and the
    /// ImageAttributes adjustment runs on that result, the same order
    /// DrawWithAttributes applies its own filter in.
    /// </summary>
    private void DrawImageThroughEffect(Image image, (PointF UpperLeft, PointF UpperRight, PointF LowerLeft) destination, RectangleF source, Effect effect, ImageAttributes? attributes, Matrix? transform)
    {
        // A metafile is drawn from the picture its records were played into
        // when it was read, at its own bounds.
        if (image is not (Bitmap or Metafile)) throw new NotImplementedException($"Drawing an image of type {image.GetType().Name} is not implemented.");
        if (source.Width <= 0 || source.Height <= 0) throw new ArgumentException("Parameter is not valid.");

        using var effectFilter = effect.CreateFilter();
        using var attributesFilter = attributes?.ToColorFilter(ColorAdjustType.Bitmap);
        using var attributesImageFilter = attributesFilter is null ? null : SKImageFilter.CreateColorFilter(attributesFilter);
        using var combined = attributesImageFilter is null ? null : SKImageFilter.CreateCompose(attributesImageFilter, effectFilter);

        var (upperLeft, upperRight, lowerLeft) = destination;
        var scaleX = (upperRight.X - upperLeft.X) / source.Width;
        var skewY = (upperRight.Y - upperLeft.Y) / source.Width;
        var skewX = (lowerLeft.X - upperLeft.X) / source.Height;
        var scaleY = (lowerLeft.Y - upperLeft.Y) / source.Height;
        var mapping = new Matrix3x2(
            scaleX, skewY,
            skewX, scaleY,
            upperLeft.X - scaleX * source.X - skewX * source.Y,
            upperLeft.Y - skewY * source.X - scaleY * source.Y);

        if (transform is { } extra) mapping = MatrixInterop.Combine(mapping, extra.Value, MatrixOrder.Append);

        using var paint = new SKPaint
        {
            IsAntialias = IsSmooth,
            BlendMode = BlendMode,
            ImageFilter = combined ?? effectFilter,
        };

        using var pixels = image.Pixels.PeekPixels();
        using var picture = SKImage.FromPixels(pixels);
        var sampling = SamplingFor(mapping);

        using var aligned = AlignToPixels();
        _canvas.Concat(mapping.ToSkia());

        var rect = new SKRect(source.Left, source.Top, source.Right, source.Bottom);
        _canvas.DrawImage(picture, rect, rect, sampling, paint);
    }

    private const string ScreenCapture =
        "Basalt.Drawing does not read the screen: capturing it needs the system's own permission and API on each platform.";

    /// <summary>
    /// Draws a rectangle of the image, in its pixels, onto three corners of
    /// the destination. The colour filter comes from ImageAttributes.
    /// </summary>
    private void DrawImageCore(Image image, (PointF UpperLeft, PointF UpperRight, PointF LowerLeft) destination, RectangleF source, SKColorFilter? filter, (WrapMode Mode, Color Color)? wrap = null)
    {
        if (image is not Bitmap) throw new NotImplementedException("Drawing a metafile is not implemented: only bitmaps are.");

        DrawPixels(image.Pixels, destination, source, filter, wrap);
    }

    private void DrawPixels(SKBitmap bitmap, (PointF UpperLeft, PointF UpperRight, PointF LowerLeft) destination, RectangleF source, SKColorFilter? filter, (WrapMode Mode, Color Color)? wrap = null)
    {
        // GDI+ answers InvalidParameter for a source with no pixels in it.
        if (source.Width <= 0 || source.Height <= 0) throw new ArgumentException("Parameter is not valid.");

        var (upperLeft, upperRight, lowerLeft) = destination;

        // The affine map that sends the source rectangle's corners to the
        // three destination corners.
        var scaleX = (upperRight.X - upperLeft.X) / source.Width;
        var skewY = (upperRight.Y - upperLeft.Y) / source.Width;
        var skewX = (lowerLeft.X - upperLeft.X) / source.Height;
        var scaleY = (lowerLeft.Y - upperLeft.Y) / source.Height;
        var mapping = new Matrix3x2(
            scaleX, skewY,
            skewX, scaleY,
            upperLeft.X - scaleX * source.X - skewX * source.Y,
            upperLeft.Y - skewY * source.X - scaleY * source.Y);

        using var paint = new SKPaint
        {
            IsAntialias = IsSmooth,
            BlendMode = BlendMode,
            ColorFilter = filter,
        };

        // No copy: the pixels are read here and now, before anything can change them.
        using var pixels = bitmap.PeekPixels();
        using var picture = SKImage.FromPixels(pixels);

        var sampling = SamplingFor(mapping);

        using var aligned = AlignToPixels();
        _canvas.Concat(mapping.ToSkia());

        var rect = new SKRect(source.Left, source.Top, source.Right, source.Bottom);

        if (TileModes(wrap) is { } tiles)
        {
            // Outside the image the picture repeats or mirrors, so the source
            // rectangle may reach past its edges.
            paint.Shader = picture.ToShader(tiles.X, tiles.Y, sampling);
            _canvas.DrawRect(rect, paint);
            return;
        }

        _canvas.DrawImage(picture, rect, rect, sampling, paint);
    }

    /// <summary>
    /// The tiling ImageAttributes.SetWrapMode asks for; null for GDI+'s own
    /// behaviour, Clamp to transparency, where nothing lies outside the image.
    /// </summary>
    private static (SKShaderTileMode X, SKShaderTileMode Y)? TileModes((WrapMode Mode, Color Color)? wrap)
    {
        if (wrap is not { } settings) return null;

        var (mode, colour) = settings;

        return mode switch
        {
            WrapMode.Tile => (SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
            WrapMode.TileFlipX => (SKShaderTileMode.Mirror, SKShaderTileMode.Repeat),
            WrapMode.TileFlipY => (SKShaderTileMode.Repeat, SKShaderTileMode.Mirror),
            WrapMode.TileFlipXY => (SKShaderTileMode.Mirror, SKShaderTileMode.Mirror),
            WrapMode.Clamp when colour.A == 0 => null,
            _ => throw new NotImplementedException("WrapMode.Clamp with a border colour that is not transparent is not implemented."),
        };
    }

    /// <summary>
    /// How to read the image's pixels for this mapping.
    /// </summary>
    /// <remarks>
    /// Drawn at its own size on whole pixels, an image is copied pixel for
    /// pixel, whatever InterpolationMode says: a filter would blend each pixel
    /// with its neighbour by the small offset of the pixel-grid shift, and an
    /// icon drawn on a button would come out soft. Otherwise the mode decides;
    /// GDI+'s Default is bilinear.
    /// </remarks>
    private SKSamplingOptions SamplingFor(Matrix3x2 mapping)
    {
        var toDevice = MatrixInterop.Combine(mapping, WorldToDevice(), MatrixOrder.Append);

        var oneToOne = toDevice.M11 == 1 && toDevice.M22 == 1 && toDevice.M12 == 0 && toDevice.M21 == 0;
        var onWholePixels = toDevice.M31 == MathF.Round(toDevice.M31) && toDevice.M32 == MathF.Round(toDevice.M32);

        if (oneToOne && onWholePixels) return new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None);

        return InterpolationMode switch
        {
            InterpolationMode.NearestNeighbor => new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None),
            InterpolationMode.HighQualityBilinear => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear),
            InterpolationMode.Bicubic or InterpolationMode.HighQualityBicubic or InterpolationMode.High =>
                new SKSamplingOptions(SKCubicResampler.CatmullRom),
            _ => new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None),
        };
    }

    /// <summary>
    /// The size, in the caller's units, of a block of image pixels at the
    /// image's resolution.
    /// </summary>
    private SizeF PhysicalSize(Image image, float width, float height)
    {
        var unitsPerPixel = Resolution / (PixelsPerUnit(_pageUnit) * _pageScale);

        return new SizeF(
            width / image.HorizontalResolution * unitsPerPixel,
            height / image.VerticalResolution * unitsPerPixel);
    }

    /// <summary>A source rectangle given in some unit, as image pixels.</summary>
    private static RectangleF SourceInPixels(Image image, RectangleF rect, GraphicsUnit unit)
    {
        float PixelsPerUnit(float resolution) => unit switch
        {
            GraphicsUnit.Pixel => 1f,
            GraphicsUnit.Point => resolution / 72f,
            GraphicsUnit.Inch => resolution,
            GraphicsUnit.Millimeter => resolution / 25.4f,
            GraphicsUnit.Document => resolution / 300f,
            GraphicsUnit.Display => resolution / 100f,

            // World units have no meaning for an image's own pixels.
            _ => throw new ArgumentException("Parameter is not valid.", nameof(unit)),
        };

        var x = PixelsPerUnit(image.HorizontalResolution);
        var y = PixelsPerUnit(image.VerticalResolution);

        return new RectangleF(rect.X * x, rect.Y * y, rect.Width * x, rect.Height * y);
    }

    private static RectangleF WholeImage(Image image) => new(0, 0, image.Width, image.Height);

    private static (PointF, PointF, PointF) Corners(RectangleF rect) =>
        (new PointF(rect.Left, rect.Top), new PointF(rect.Right, rect.Top), new PointF(rect.Left, rect.Bottom));

    /// <summary>Upper-left, upper-right and lower-left.</summary>
    /// <remarks>
    /// System.Drawing accepts a fourth, lower-right point and GDI+ then answers
    /// NotImplemented, which arrives as the same exception here.
    /// </remarks>
    private static (PointF, PointF, PointF) Parallelogram(PointF[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Length == 4) throw new NotImplementedException("Drawing an image onto four destination points is not implemented, in GDI+ either.");
        if (points.Length != 3) throw new ArgumentException("Destination points must be an array with a length of 3 or 4.", nameof(points));

        return (points[0], points[1], points[2]);
    }

    private static PointF[] ToPointFArray(Point[] points)
    {
        ArgumentNullException.ThrowIfNull(points);

        return Array.ConvertAll(points, point => (PointF)point);
    }
}
