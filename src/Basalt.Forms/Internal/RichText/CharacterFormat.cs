using System.Drawing;

namespace System.Windows.Forms.Internal.RichText;

/// <summary>
/// The formatting of one run of characters: everything RichTextBox exposes
/// through its SelectionFont/SelectionColor/... properties, resolved to
/// concrete values (never "mixed", that is a property of a range, not of a
/// single run).
/// </summary>
internal sealed record CharacterFormat(
    string FontFamily,
    float FontSize,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strikeout,
    Color Color,
    Color BackColor,
    int CharOffset,
    bool Protected)
{
    public FontStyle Style =>
        (Bold ? FontStyle.Bold : FontStyle.Regular) |
        (Italic ? FontStyle.Italic : FontStyle.Regular) |
        (Underline ? FontStyle.Underline : FontStyle.Regular) |
        (Strikeout ? FontStyle.Strikeout : FontStyle.Regular);

    public static CharacterFormat FromFont(Font font, Color color) => new(
        font.Name,
        font.SizeInPoints,
        font.Bold,
        font.Italic,
        font.Underline,
        font.Strikeout,
        color,
        Color.Empty,
        0,
        false);

    /// <summary>A Font matching this format; callers dispose it when they are done with it.</summary>
    public Font ToFont() => new(FontFamily, FontSize, Style);

    public CharacterFormat WithFont(Font font) => this with
    {
        FontFamily = font.Name,
        FontSize = font.SizeInPoints,
        Bold = font.Bold,
        Italic = font.Italic,
        Underline = font.Underline,
        Strikeout = font.Strikeout,
    };
}
