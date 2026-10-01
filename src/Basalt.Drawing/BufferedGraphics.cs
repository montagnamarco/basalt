namespace System.Drawing;

/// <summary>
/// A <see cref="Graphics"/> over an offscreen bitmap, drawn into to avoid the
/// flicker of several small draws landing on screen one at a time, then
/// copied onto a real surface all at once by <see cref="Render()"/>.
/// </summary>
public sealed class BufferedGraphics : IDisposable
{
    private readonly Graphics _targetGraphics;
    private readonly Bitmap _buffer;
    private readonly Rectangle _targetRectangle;
    private bool _disposed;

    internal BufferedGraphics(Graphics graphics, Graphics targetGraphics, Bitmap buffer, Rectangle targetRectangle)
    {
        Graphics = graphics;
        _targetGraphics = targetGraphics;
        _buffer = buffer;
        _targetRectangle = targetRectangle;
    }

    /// <summary>The surface a caller draws on; what <see cref="Render()"/> later copies out.</summary>
    public Graphics Graphics { get; }

    /// <summary>Copies the buffer onto the <see cref="Graphics"/> that <see cref="BufferedGraphicsContext.Allocate(Graphics, Rectangle)"/> was given.</summary>
    public void Render() => Render(_targetGraphics);

    public void Render(Graphics target)
    {
        ArgumentNullException.ThrowIfNull(target);

        target.DrawImage(_buffer, _targetRectangle.Location);
    }

    public void Render(IntPtr targetDC) => throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Graphics.Dispose();
        _buffer.Dispose();
    }
}
