namespace System.Drawing.Text;

/// <summary>A set of font families: those installed, or those an application loaded itself.</summary>
public abstract class FontCollection : IDisposable
{
    private protected FontCollection()
    {
    }

    ~FontCollection() => Dispose(false);

    public FontFamily[] Families => [.. FamilyNames().Select(name => new FontFamily(name, this))];

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
    }

    /// <summary>The names of the families in this collection.</summary>
    internal abstract IEnumerable<string> FamilyNames();

    /// <summary>The typeface of a family of this collection in a style, or null when it has none.</summary>
    internal abstract SkiaSharp.SKTypeface? Typeface(string family, FontStyle style);
}
