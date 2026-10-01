using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// The base of every GDI+ bitmap effect (blur, sharpen, colour curves and the
/// rest). GDI+ never exposed a public constructor for it either; an effect is
/// only ever built through one of its subclasses.
/// </summary>
public abstract class Effect : IDisposable
{
    private protected Effect()
    {
    }

    ~Effect() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// The Skia filter this effect becomes: an image filter for the two
    /// effects that need neighbouring pixels (blur, sharpen), a colour filter
    /// wrapped as one for every other effect. The caller disposes it.
    /// </summary>
    internal abstract SKImageFilter CreateFilter();
}
