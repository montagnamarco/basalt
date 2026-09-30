namespace System.Drawing.Imaging.Effects;

/// <summary>
/// The base of every GDI+ bitmap effect (blur, sharpen, colour curves and the
/// rest). None of the concrete effects are implemented yet: only the base
/// exists, so that <see cref="Bitmap.ApplyEffect"/> has a parameter type to
/// take. GDI+ never exposed a public constructor for it either; an effect is
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
}
