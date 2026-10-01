using SkiaSharp;

namespace System.Drawing.Text;

/// <summary>The fonts installed on the system, as the platform's font manager lists them.</summary>
public sealed class InstalledFontCollection : FontCollection
{
    public InstalledFontCollection()
    {
    }

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    internal override IEnumerable<string> FamilyNames() => FontResolver.InstalledFamilies;

    internal override SKTypeface? Typeface(string family, FontStyle style) =>
        FontResolver.Resolve(family) is { } drawing ? FontResolver.Typeface(drawing, style) : null;
}
