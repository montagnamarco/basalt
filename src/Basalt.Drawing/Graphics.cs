using System.Drawing.Drawing2D;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A surface to draw on, with the coordinates and rules of GDI+.</summary>
public sealed class Graphics : MarshalByRefObject, IDeviceContext, IDisposable
{
    private readonly SKCanvas _canvas;
    private readonly bool _ownsCanvas;

    // Holds the image a canvas draws into: an SKCanvas does not keep its
    // bitmap alive, and drawing into freed pixels ends the process.
    private readonly Image? _target;

    internal Graphics(SKCanvas canvas, bool ownsCanvas, Image? target = null)
    {
        _canvas = canvas;
        _ownsCanvas = ownsCanvas;
        _target = target;
    }

    public SmoothingMode SmoothingMode { get; set; } = SmoothingMode.None;

    public PixelOffsetMode PixelOffsetMode { get; set; } = PixelOffsetMode.Default;

    public static Graphics FromImage(Image image)
    {
        ArgumentNullException.ThrowIfNull(image);

        return new Graphics(new SKCanvas(image.Pixels), ownsCanvas: true, image);
    }

    public void Clear(Color color) => _canvas.Clear(color.ToSkia());

    public void DrawLine(Pen pen, int x1, int y1, int x2, int y2) => DrawLine(pen, (float)x1, y1, x2, y2);

    public void DrawLine(Pen pen, Point pt1, Point pt2) => DrawLine(pen, (float)pt1.X, pt1.Y, pt2.X, pt2.Y);

    public void DrawLine(Pen pen, PointF pt1, PointF pt2) => DrawLine(pen, pt1.X, pt1.Y, pt2.X, pt2.Y);

    public void DrawLine(Pen pen, float x1, float y1, float x2, float y2)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        _canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    public void DrawRectangle(Pen pen, Rectangle rect) => DrawRectangle(pen, (float)rect.X, rect.Y, rect.Width, rect.Height);

    public void DrawRectangle(Pen pen, RectangleF rect) => DrawRectangle(pen, rect.X, rect.Y, rect.Width, rect.Height);

    public void DrawRectangle(Pen pen, int x, int y, int width, int height) => DrawRectangle(pen, (float)x, y, width, height);

    public void DrawRectangle(Pen pen, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(pen);

        using var paint = Stroke(pen);
        using var aligned = AlignToPixels();
        _canvas.DrawRect(x, y, width, height, paint);
    }

    public void FillRectangle(Brush brush, Rectangle rect) => FillRectangle(brush, (float)rect.X, rect.Y, rect.Width, rect.Height);

    public void FillRectangle(Brush brush, RectangleF rect) => FillRectangle(brush, rect.X, rect.Y, rect.Width, rect.Height);

    public void FillRectangle(Brush brush, int x, int y, int width, int height) => FillRectangle(brush, (float)x, y, width, height);

    public void FillRectangle(Brush brush, float x, float y, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(brush);

        using var paint = new SKPaint { Style = SKPaintStyle.Fill, IsAntialias = IsSmooth };
        brush.ApplyTo(paint);

        using var aligned = AlignToPixels();
        _canvas.DrawRect(x, y, width, height, paint);
    }

    public IntPtr GetHdc() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void ReleaseHdc() => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void Dispose()
    {
        _canvas.Flush();
        if (_ownsCanvas) _canvas.Dispose();

        GC.KeepAlive(_target);
    }

    /// <summary>
    /// Moves the device so that a GDI+ coordinate lands where GDI+ puts it, for
    /// as long as the returned scope lives.
    /// </summary>
    /// <remarks>
    /// GDI+ puts the centre of a pixel on the whole coordinates unless asked
    /// for PixelOffsetMode.Half; Skia always puts it on the halves. Without the
    /// half pixel, DrawRectangle(0, 0, 10, 10) with a pen of one smears over two
    /// rows of pixels per side instead of the eleven crisp pixels GDI+ draws,
    /// and every border in every control is wrong.
    ///
    /// Without smoothing the shift stops a little short of the half. A shape
    /// edge then falls exactly on pixel centres, and the two libraries break
    /// the tie in opposite directions: Skia gives the pixel to the shape on its
    /// right and bottom edges, GDI+ on its left and top. Measured: with the
    /// full half, FillRectangle(2, 3, 10, 8) filled columns 3 to 12 instead of
    /// 2 to 11. The tie rule of GDI+ is the documented top-left one and still
    /// has to be confirmed against Windows pixel by pixel.
    ///
    /// The shift is applied after the world transform, in device pixels: it is
    /// a property of the pixel grid and must not be scaled with the drawing.
    /// </remarks>
    private PixelAlignment AlignToPixels()
    {
        var shift = PixelOffsetMode is PixelOffsetMode.Half or PixelOffsetMode.HighQuality ? 0f
            : IsSmooth ? 0.5f
            : 0.5f - TieBreak;

        _canvas.Save();
        _canvas.SetMatrix(_canvas.TotalMatrix.PostConcat(SKMatrix.CreateTranslation(shift, shift)));

        return new PixelAlignment(_canvas);
    }

    // Small enough to be invisible, large enough to survive single precision
    // at any coordinate a window can have.
    private const float TieBreak = 1f / 32f;

    private readonly ref struct PixelAlignment(SKCanvas canvas)
    {
        public void Dispose() => canvas.Restore();
    }

    private bool IsSmooth => SmoothingMode is SmoothingMode.AntiAlias or SmoothingMode.HighQuality;

    private SKPaint Stroke(Pen pen)
    {
        var paint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            IsAntialias = IsSmooth,

            // A pen thinner than a pixel still draws a line one pixel wide.
            StrokeWidth = Math.Max(pen.Width, 1f),
            StrokeCap = SKStrokeCap.Butt,
            StrokeJoin = SKStrokeJoin.Miter,
        };

        pen.Brush.ApplyTo(paint);
        return paint;
    }
}
