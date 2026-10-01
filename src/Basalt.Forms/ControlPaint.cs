using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Drawing primitives a control uses to paint the classic Windows look
/// itself, the way Win32's <c>DrawFrameControl</c> and <c>DrawEdge</c> do:
/// a button's bevel, a check box's glyph, a focus rectangle's dotted line.
/// </summary>
/// <remarks>
/// Every colour comes from <see cref="SystemColors"/>, never a literal RGB
/// triplet, so a skin that changes those colours changes what this class
/// paints without a line here changing. The shapes (how many pixels a bevel
/// is, where a check mark's strokes fall) are this project's own reading of
/// the classic theme's screenshots and of the public description of
/// <c>DrawFrameControl</c>/<c>DrawEdge</c>, not a port of anyone else's code.
/// </remarks>
public static partial class ControlPaint
{
    /// <summary>
    /// All four edges, but not <see cref="Border3DSide.Middle"/>: the default
    /// for the overloads that take no <see cref="Border3DSide"/>, which draw
    /// a bevel around a rectangle without touching what is already inside
    /// it. <see cref="Border3DSide.All"/> includes Middle on purpose, for the
    /// callers (like <see cref="DrawButton"/>) that do want the face painted.
    /// </summary>
    private const Border3DSide AllEdges = Border3DSide.Left | Border3DSide.Top | Border3DSide.Right | Border3DSide.Bottom;

    /// <summary>Not high-contrast aware: this project has one theme, so this is always <see cref="SystemColors.ControlDark"/>.</summary>
    public static Color ContrastControlDark => SystemColors.ControlDark;

    public static Color Dark(Color baseColor) => baseColor == SystemColors.Control ? SystemColors.ControlDark : Dark(baseColor, 0.5f);

    public static Color DarkDark(Color baseColor) => baseColor == SystemColors.Control ? SystemColors.ControlDarkDark : Dark(baseColor, 1.0f);

    /// <summary>Moves the colour's HLS lightness a fraction of the way towards black, keeping its hue and saturation.</summary>
    public static Color Dark(Color baseColor, float percOfDarkDark)
    {
        var (hue, saturation, lightness) = ToHls(baseColor);

        return FromHls(hue, saturation, lightness * (1f - percOfDarkDark), baseColor.A);
    }

    public static Color Light(Color baseColor) => baseColor == SystemColors.Control ? SystemColors.ControlLight : Light(baseColor, 0.5f);

    public static Color LightLight(Color baseColor) => baseColor == SystemColors.Control ? SystemColors.ControlLightLight : Light(baseColor, 1.0f);

    /// <summary>Moves the colour's HLS lightness a fraction of the way towards white, keeping its hue and saturation.</summary>
    public static Color Light(Color baseColor, float percOfLightLight)
    {
        var (hue, saturation, lightness) = ToHls(baseColor);

        return FromHls(hue, saturation, lightness + (1f - lightness) * percOfLightLight, baseColor.A);
    }

    /// <summary>Hue in degrees and saturation/lightness in 0..1, the way Color itself computes them (HSL, not GDI's HLS order, but the same three numbers).</summary>
    private static (float Hue, float Saturation, float Lightness) ToHls(Color color) =>
        (color.GetHue(), color.GetSaturation(), color.GetBrightness());

    /// <summary>The standard HSL-to-RGB conversion: a colour wheel split into six 60-degree sectors around the achromatic axis.</summary>
    private static Color FromHls(float hue, float saturation, float lightness, int alpha)
    {
        lightness = Math.Clamp(lightness, 0f, 1f);
        saturation = Math.Clamp(saturation, 0f, 1f);

        if (saturation <= 0f)
        {
            var grey = ToByte(lightness);
            return Color.FromArgb(alpha, grey, grey, grey);
        }

        var chroma = (1f - Math.Abs(2f * lightness - 1f)) * saturation;
        var sector = hue / 60f;
        var secondLargest = chroma * (1f - Math.Abs(sector % 2f - 1f));
        var match = lightness - chroma / 2f;

        var (r, g, b) = sector switch
        {
            < 1f => (chroma, secondLargest, 0f),
            < 2f => (secondLargest, chroma, 0f),
            < 3f => (0f, chroma, secondLargest),
            < 4f => (0f, secondLargest, chroma),
            < 5f => (secondLargest, 0f, chroma),
            _ => (chroma, 0f, secondLargest),
        };

        return Color.FromArgb(alpha, ToByte(r + match), ToByte(g + match), ToByte(b + match));
    }

    private static int ToByte(float value) => (int)Math.Round(Math.Clamp(value, 0f, 1f) * 255f);

    /// <summary>Shrinks a rectangle by the same amount on every side, never past its centre.</summary>
    private static Rectangle Shrink(Rectangle rect, int amount) => Rectangle.FromLTRB(
        rect.Left + amount,
        rect.Top + amount,
        Math.Max(rect.Left + amount, rect.Right - amount),
        Math.Max(rect.Top + amount, rect.Bottom - amount));
}
