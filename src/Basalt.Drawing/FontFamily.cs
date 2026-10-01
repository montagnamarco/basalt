using System.Drawing.Text;
using SkiaSharp;

namespace System.Drawing;

/// <summary>A family of typefaces sharing a design, such as Arial or Times New Roman.</summary>
public sealed class FontFamily : MarshalByRefObject, IDisposable
{
    // Null for the installed fonts.
    private readonly FontCollection? _collection;

    public FontFamily(GenericFontFamilies genericFamily)
        : this(genericFamily switch
        {
            GenericFontFamilies.Serif => FontResolver.Serif,
            GenericFontFamilies.Monospace => FontResolver.Monospace,
            _ => FontResolver.SansSerif,
        })
    {
    }

    public FontFamily(string name)
        : this(name, null)
    {
    }

    public FontFamily(string name, FontCollection? fontCollection)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (fontCollection is null or InstalledFontCollection)
        {
            Name = FontResolver.CanonicalName(name) ?? throw NotFound(name);
        }
        else
        {
            Name = fontCollection.FamilyNames().FirstOrDefault(family => string.Equals(family, name, StringComparison.OrdinalIgnoreCase))
                ?? throw NotFound(name);
            _collection = fontCollection;
        }
    }

    ~FontFamily() => Dispose(false);

    public string Name { get; }

    public static FontFamily GenericSansSerif => new(GenericFontFamilies.SansSerif);

    public static FontFamily GenericSerif => new(GenericFontFamilies.Serif);

    public static FontFamily GenericMonospace => new(GenericFontFamilies.Monospace);

    public static FontFamily[] Families => new InstalledFontCollection().Families;

    [Obsolete("FontFamily.GetFamilies has been deprecated. Use Families instead.")]
    public static FontFamily[] GetFamilies(Graphics graphics)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        return Families;
    }

    /// <summary>The family's name; the language is not consulted, as the name has one spelling here.</summary>
    public string GetName(int language) => Name;

    /// <summary>
    /// True for every style a typeface of the family exists for, and also for
    /// bold and italic where they would be simulated, as GDI+ draws them.
    /// </summary>
    public bool IsStyleAvailable(FontStyle style) => true;

    public int GetEmHeight(FontStyle style) => Metrics(style).EmHeight;

    public int GetCellAscent(FontStyle style) => Metrics(style).Ascent;

    public int GetCellDescent(FontStyle style) => Metrics(style).Descent;

    public int GetLineSpacing(FontStyle style) => Metrics(style).LineSpacing;

    public override bool Equals(object? obj) =>
        obj is FontFamily other && string.Equals(other.Name, Name, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(other._collection, _collection);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Name);

    public override string ToString() => $"[{GetType().Name}: Name={Name}]";

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
    }

    /// <summary>
    /// The typeface that draws this family in a style. It belongs to the
    /// family's collection or to Skia's cache: callers do not dispose it.
    /// </summary>
    internal SKTypeface Typeface(FontStyle style)
    {
        if (_collection is not null) return _collection.Typeface(Name, style) ?? SKTypeface.Default;

        return FontResolver.Typeface(FontResolver.Resolve(Name) ?? Name, style);
    }

    private (int EmHeight, int Ascent, int Descent, int LineSpacing) Metrics(FontStyle style) =>
        FontResolver.DesignMetrics(Typeface(style));

    private static ArgumentException NotFound(string name) => new($"Font '{name}' cannot be found.", nameof(name));
}
