using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>
/// The average character size WinForms scales forms by in AutoScaleMode.Font.
/// </summary>
/// <remarks>
/// Windows measures it with GDI on the real font. Here the font may be a
/// stand-in with slightly different widths, and a form designed with
/// AutoScaleDimensions (6F, 13F) would be scaled by a few percent for no
/// visible reason. For the fonts the designer writes those dimensions for,
/// the Windows values are used; any other font is measured.
/// </remarks>
internal static class FontScaling
{
    private static readonly Dictionary<(string Family, float Size), SizeF> WindowsValues = new()
    {
        [("Microsoft Sans Serif", 8.25f)] = new SizeF(6f, 13f),
        [("Tahoma", 8.25f)] = new SizeF(6f, 13f),
        [("Segoe UI", 9f)] = new SizeF(7f, 15f),
    };

    private const string Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    public static SizeF AverageCharacterSize(Font font)
    {
        if (font.Style == FontStyle.Regular && font.Unit == GraphicsUnit.Point && WindowsValues.TryGetValue((font.Name, font.Size), out var known))
        {
            return known;
        }

        var width = Control.MeasurementGraphics.MeasureString(Alphabet, font, new PointF(0, 0), StringFormat.GenericTypographic).Width;

        return new SizeF((float)Math.Round(width / Alphabet.Length), font.Height);
    }
}
