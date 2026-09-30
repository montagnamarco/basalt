using SkiaSharp;

namespace System.Drawing;

/// <summary>What the inside of a shape is filled with.</summary>
public abstract class Brush : MarshalByRefObject, ICloneable, IDisposable
{
    protected Brush()
    {
    }

    ~Brush() => Dispose(false);

    public abstract object Clone();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>There is no GDI+ brush behind this one to replace.</summary>
    protected internal void SetNativeBrush(IntPtr brush) =>
        throw new PlatformNotSupportedException(Unsupported.NativeHandles);

    /// <summary>Configures a paint to fill with this brush.</summary>
    internal abstract void ApplyTo(SKPaint paint);
}
