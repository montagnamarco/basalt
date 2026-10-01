using System.Drawing;

namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// A short-lived cache of the Font objects a layout or a paint pass needs,
/// one per distinct family/size/style: a formatted document can mix many
/// runs that share the same look, and Font owns a native typeface that is
/// worth not re-creating for every character.
/// </summary>
internal sealed class RichFonts : IDisposable
{
    private readonly Dictionary<(string Family, float Size, FontStyle Style), Font> _cache = [];

    /// <summary><paramref name="zoom"/> scales the size only: ZoomFactor changes how a RichTextBox is painted and measured, never the stored format.</summary>
    public Font Get(CharacterFormat format, float zoom = 1f)
    {
        var key = (Family: format.FontFamily, Size: format.FontSize * zoom, Style: format.Style);
        if (_cache.TryGetValue(key, out var font)) return font;

        font = new Font(key.Family, key.Size, key.Style);
        _cache[key] = font;
        return font;
    }

    public void Dispose()
    {
        foreach (var font in _cache.Values) font.Dispose();
        _cache.Clear();
    }
}
