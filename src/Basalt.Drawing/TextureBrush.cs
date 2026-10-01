using System.Drawing.Drawing2D;
using System.Numerics;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A brush that fills with a tiled image.</summary>
public sealed class TextureBrush : Brush
{
    private readonly Image _image;
    private readonly RectangleF? _sourceRect;
    private WrapMode _wrapMode;
    private Matrix3x2 _transform = Matrix3x2.Identity;

    public TextureBrush(Image image)
        : this(image, WrapMode.Tile)
    {
    }

    public TextureBrush(Image image, WrapMode wrapMode)
    {
        ArgumentNullException.ThrowIfNull(image);

        _image = image;
        _wrapMode = wrapMode;
    }

    public TextureBrush(Image image, WrapMode wrapMode, Rectangle dstRect)
        : this(image, wrapMode, (RectangleF)dstRect)
    {
    }

    public TextureBrush(Image image, WrapMode wrapMode, RectangleF dstRect)
        : this(image, wrapMode)
    {
        CheckInside(image, dstRect);
        _sourceRect = dstRect;
    }

    public TextureBrush(Image image, Rectangle dstRect)
        : this(image, WrapMode.Tile, (RectangleF)dstRect)
    {
    }

    public TextureBrush(Image image, RectangleF dstRect)
        : this(image, WrapMode.Tile, dstRect)
    {
    }

    public Image Image => _image;

    public WrapMode WrapMode
    {
        get => _wrapMode;
        set => _wrapMode = value;
    }

    public Drawing2D.Matrix Transform
    {
        get => new(_transform);
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            _transform = value.Value;
        }
    }

    public void MultiplyTransform(Drawing2D.Matrix matrix) => MultiplyTransform(matrix, MatrixOrder.Prepend);

    public void MultiplyTransform(Drawing2D.Matrix matrix, MatrixOrder order)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        _transform = MatrixInterop.Combine(_transform, matrix.Value, order);
    }

    public void ResetTransform() => _transform = Matrix3x2.Identity;

    public void RotateTransform(float angle) => RotateTransform(angle, MatrixOrder.Prepend);

    public void RotateTransform(float angle, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateRotation(angle * MathF.PI / 180f), order);

    public void ScaleTransform(float sx, float sy) => ScaleTransform(sx, sy, MatrixOrder.Prepend);

    public void ScaleTransform(float sx, float sy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateScale(sx, sy), order);

    public void TranslateTransform(float dx, float dy) => TranslateTransform(dx, dy, MatrixOrder.Prepend);

    public void TranslateTransform(float dx, float dy, MatrixOrder order) =>
        _transform = MatrixInterop.Combine(_transform, Matrix3x2.CreateTranslation(dx, dy), order);

    public override object Clone()
    {
        var clone = _sourceRect is { } rect ? new TextureBrush(_image, _wrapMode, rect) : new TextureBrush(_image, _wrapMode);
        clone._transform = _transform;
        return clone;
    }

    internal override void ApplyTo(SKPaint paint)
    {
        var full = _image.Pixels;

        // A crop rectangle needs a bitmap of its own; the whole image does
        // not, and must not be frozen immutable here, since it is the live,
        // still-mutable backing store of an Image other code keeps drawing on.
        using var cropped = _sourceRect is { } rect ? CropTo(full, rect) : null;
        var source = cropped ?? full;

        // SKImage.FromBitmap copies the pixels whenever the bitmap it is
        // given is not already immutable, which is exactly the case for
        // `full`: the copy is what keeps this shader valid after the next
        // draw changes the image.
        using var image = SKImage.FromBitmap(source);

        var (tileX, tileY) = _wrapMode switch
        {
            WrapMode.TileFlipX => (SKShaderTileMode.Mirror, SKShaderTileMode.Repeat),
            WrapMode.TileFlipY => (SKShaderTileMode.Repeat, SKShaderTileMode.Mirror),
            WrapMode.TileFlipXY => (SKShaderTileMode.Mirror, SKShaderTileMode.Mirror),
            WrapMode.Clamp => (SKShaderTileMode.Clamp, SKShaderTileMode.Clamp),
            _ => (SKShaderTileMode.Repeat, SKShaderTileMode.Repeat),
        };

        paint.Color = SKColors.White;
        paint.Shader = image.ToShader(tileX, tileY, _transform.ToSkia());
    }

    private static SKBitmap CropTo(SKBitmap source, RectangleF rect)
    {

        var subset = SKRectI.Create((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        var destination = new SKBitmap(subset.Width, subset.Height, source.ColorType, source.AlphaType);
        if (source.ExtractSubset(destination, subset)) return destination;

        // Native memory: nothing else would ever free it.
        destination.Dispose();
        throw new ArgumentException("The source rectangle must lie within the image.", nameof(rect));
    }

    /// <summary>
    /// GDI+ refuses, when the brush is made, a rectangle that reaches outside
    /// the image, with the OutOfMemory status that System.Drawing turns into
    /// this exception; Skia would quietly crop to what is there.
    /// </summary>
    private static void CheckInside(Image image, RectangleF rect)
    {
        if (rect.X < 0 || rect.Y < 0 || rect.Width <= 0 || rect.Height <= 0 || rect.Right > image.Width || rect.Bottom > image.Height)
        {
            throw new OutOfMemoryException("The source rectangle must lie within the image.");
        }
    }
}
