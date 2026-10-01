using System.Drawing;

namespace System.Windows.Forms;

/// <summary>The graphics to paint with and the part of the control that needs it.</summary>
public class PaintEventArgs : EventArgs, IDeviceContext, IDisposable
{
    private Graphics? _graphics;

    public PaintEventArgs(Graphics graphics, Rectangle clipRect)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        _graphics = graphics;
        ClipRectangle = clipRect;
    }

    ~PaintEventArgs() => Dispose(false);

    public Rectangle ClipRectangle { get; }

    public Graphics Graphics => _graphics ?? throw new ObjectDisposedException(nameof(PaintEventArgs));

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Forgets the graphics without disposing it: whoever created the
    /// arguments owns the graphics, as when WinForms paints a window.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        _graphics = null;
    }

    IntPtr IDeviceContext.GetHdc() => Graphics.GetHdc();

    void IDeviceContext.ReleaseHdc() => Graphics.ReleaseHdc();
}
