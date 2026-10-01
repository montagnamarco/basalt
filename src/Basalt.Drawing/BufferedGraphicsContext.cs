namespace System.Drawing;

/// <summary>Hands out <see cref="BufferedGraphics"/> buffers, one allocation at a time.</summary>
public sealed class BufferedGraphicsContext : IDisposable
{
    // The size above which GDI+ stops keeping a reusable buffer around and
    // allocates one just for that call; this library never keeps one across
    // calls in the first place (see the Allocate remarks), so the value is
    // only reported. 225 by 96 is the default of Microsoft's System.Drawing
    // source; not compared with a running Windows.
    private Size _maximumBuffer = new(225, 96);

    public BufferedGraphicsContext()
    {
    }

    ~BufferedGraphicsContext()
    {
    }

    public Size MaximumBuffer
    {
        get => _maximumBuffer;
        set
        {
            if (value.Width <= 0 || value.Height <= 0) throw new ArgumentException("Parameter is not valid.");

            _maximumBuffer = value;
        }
    }

    /// <summary>
    /// A fresh offscreen bitmap the size of <paramref name="targetRectangle"/>,
    /// with a <see cref="Graphics"/> over it to draw on.
    /// </summary>
    /// <remarks>
    /// GDI+ reuses one cached buffer across calls when it fits under
    /// <see cref="MaximumBuffer"/>, and allocates a one-off bitmap only when it
    /// does not. This always allocates a fresh bitmap: correct either way,
    /// since <see cref="BufferedGraphics.Dispose"/> always frees it, but not
    /// the same reuse optimisation GDI+ makes.
    /// </remarks>
    public BufferedGraphics Allocate(Graphics targetGraphics, Rectangle targetRectangle)
    {
        ArgumentNullException.ThrowIfNull(targetGraphics);

        var width = Math.Max(1, targetRectangle.Width);
        var height = Math.Max(1, targetRectangle.Height);

        var buffer = new Bitmap(width, height);
        var graphics = Graphics.FromImage(buffer);

        return new BufferedGraphics(graphics, targetGraphics, buffer, targetRectangle);
    }

    public BufferedGraphics Allocate(IntPtr targetDC, Rectangle targetRectangle) =>
        throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>
    /// Drops any buffer this context cached for reuse. There is none here to
    /// drop: see the remarks on <see cref="Allocate(Graphics, Rectangle)"/>.
    /// </summary>
    public void Invalidate()
    {
    }

    public void Dispose() => GC.SuppressFinalize(this);
}
