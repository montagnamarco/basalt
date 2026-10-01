using System.Buffers.Binary;
using SkiaSharp;

namespace System.Drawing.Text;

/// <summary>
/// Finds the typeface behind a family name, and reads the metrics GDI+
/// reports for it.
/// </summary>
/// <remarks>
/// Forms written on Windows name Windows fonts: Microsoft Sans Serif, Segoe UI,
/// Tahoma. Where the font itself is missing, the first installed font from a
/// list of ones with close metrics stands in, so that a label measures about
/// the same width; the family keeps the name it was asked for. How close each
/// stand-in comes to the Windows font's widths has not been measured.
/// </remarks>
internal static class FontResolver
{
    // The family GDI+ answers for GenericSansSerif, GenericSerif and GenericMonospace.
    public const string SansSerif = "Microsoft Sans Serif";
    public const string Serif = "Times New Roman";
    public const string Monospace = "Courier New";

    private static readonly Dictionary<string, string[]> StandIns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Microsoft Sans Serif"] = ["Microsoft Sans Serif", "Arial", "Liberation Sans", "Arimo", "Helvetica", "DejaVu Sans"],
        ["MS Sans Serif"] = ["Microsoft Sans Serif", "Arial", "Liberation Sans", "Arimo", "Helvetica", "DejaVu Sans"],
        ["Segoe UI"] = ["Segoe UI", "Selawik", "Helvetica Neue", "Arial", "Liberation Sans", "DejaVu Sans"],
        ["Tahoma"] = ["Tahoma", "DejaVu Sans", "Verdana", "Arial", "Liberation Sans"],
        ["Verdana"] = ["Verdana", "DejaVu Sans", "Arial", "Liberation Sans"],
        ["Arial"] = ["Arial", "Liberation Sans", "Arimo", "Helvetica", "DejaVu Sans"],
        ["Times New Roman"] = ["Times New Roman", "Liberation Serif", "Tinos", "Times", "DejaVu Serif"],
        ["Courier New"] = ["Courier New", "Liberation Mono", "Cousine", "Courier", "DejaVu Sans Mono"],
        ["Consolas"] = ["Consolas", "Menlo", "DejaVu Sans Mono", "Liberation Mono", "Courier New"],
        ["Calibri"] = ["Calibri", "Carlito", "Arial", "Liberation Sans"],
        ["Cambria"] = ["Cambria", "Caladea", "Times New Roman", "Liberation Serif"],
    };

    private static readonly Lazy<Dictionary<string, string>> Installed = new(() =>
        SKFontManager.Default.FontFamilies
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(name => name, name => name, StringComparer.OrdinalIgnoreCase));

    public static IEnumerable<string> InstalledFamilies => Installed.Value.Values.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The installed family that will draw this name, or null when the name is
    /// neither installed nor a Windows font with a stand-in.
    /// </summary>
    public static string? Resolve(string name)
    {
        if (Installed.Value.TryGetValue(name, out var installed)) return installed;
        if (!StandIns.TryGetValue(name, out var candidates)) return null;

        foreach (var candidate in candidates)
        {
            if (Installed.Value.TryGetValue(candidate, out var found)) return found;
        }

        return null;
    }

    /// <summary>The name a family is known by: the installed spelling, or the Windows name asked for.</summary>
    public static string? CanonicalName(string name) =>
        Installed.Value.TryGetValue(name, out var installed) ? installed
        : StandIns.ContainsKey(name) && Resolve(name) is not null ? StandIns.Keys.First(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
        : null;

    public static SKTypeface Typeface(string drawingFamily, FontStyle style)
    {
        var weight = (style & FontStyle.Bold) != 0 ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal;
        var slant = (style & FontStyle.Italic) != 0 ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright;

        return SKFontManager.Default.MatchFamily(drawingFamily, new SKFontStyle(weight, SKFontStyleWidth.Normal, slant))
            ?? SKTypeface.Default;
    }

    /// <summary>
    /// The four numbers GDI+ gives in design units: em height, cell ascent,
    /// cell descent and line spacing.
    /// </summary>
    /// <remarks>
    /// GDI+ takes the cell from the OS/2 table's Windows ascent and descent, and
    /// the line spacing from the larger of that cell and the hhea table's
    /// ascender, descender and line gap. For Arial this gives the values
    /// Microsoft documents: 2048, 1854, 434 and 2355.
    /// </remarks>
    public static (int EmHeight, int Ascent, int Descent, int LineSpacing) DesignMetrics(SKTypeface typeface)
    {
        var em = typeface.UnitsPerEm;

        int ascent;
        int descent;
        if (typeface.TryGetTableData(Tag("OS/2"), out var os2) && os2.Length >= 78)
        {
            ascent = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(74));
            descent = BinaryPrimitives.ReadUInt16BigEndian(os2.AsSpan(76));
        }
        else
        {
            using var font = new SKFont(typeface, em);
            ascent = (int)MathF.Round(-font.Metrics.Ascent);
            descent = (int)MathF.Round(font.Metrics.Descent);
        }

        var lineSpacing = ascent + descent;
        if (typeface.TryGetTableData(Tag("hhea"), out var hhea) && hhea.Length >= 10)
        {
            var hheaAscent = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(4));
            var hheaDescent = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(6));
            var lineGap = BinaryPrimitives.ReadInt16BigEndian(hhea.AsSpan(8));

            lineSpacing = Math.Max(lineSpacing, hheaAscent - hheaDescent + lineGap);
        }

        return (em, ascent, descent, lineSpacing);
    }

    private static uint Tag(string name) =>
        (uint)(name[0] << 24 | name[1] << 16 | name[2] << 8 | name[3]);
}
