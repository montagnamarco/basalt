using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Remaps the black point, mid tone and white point of every channel. GDI+
/// documents <see cref="Highlight"/> and <see cref="Shadow"/> as percentages,
/// 0 through 100, and <see cref="Midtone"/> as -100 through 100.
/// </summary>
/// <remarks>
/// GDI+ does not publish the exact per-pixel formula: this reads Shadow and
/// Highlight as the input levels that become the new black and white points
/// (a "Levels" dialog's usual meaning) and Midtone as a gamma applied in
/// between, the closest documented behaviour, not compared with Windows pixel
/// by pixel.
/// </remarks>
public class LevelsEffect : Effect
{
    public LevelsEffect(int highlight, int midtone, int shadow)
    {
        if (highlight < 0 || highlight > 100) throw new ArgumentException("Parameter is not valid.", nameof(highlight));
        if (midtone < -100 || midtone > 100) throw new ArgumentException("Parameter is not valid.", nameof(midtone));
        if (shadow < 0 || shadow > 100) throw new ArgumentException("Parameter is not valid.", nameof(shadow));

        Highlight = highlight;
        Midtone = midtone;
        Shadow = shadow;
    }

    public int Highlight { get; }

    public int Midtone { get; }

    public int Shadow { get; }

    internal override SKImageFilter CreateFilter()
    {
        // Shadow lifts the black point up from 0; Highlight brings the white
        // point down from 255; what remains between them is stretched to fill
        // 0..255 again.
        var black = Shadow / 100f * 255f;
        var white = 255f - Highlight / 100f * 255f;
        var span = MathF.Max(1f, white - black);

        // Midtone is a gamma around that stretched range: positive brightens
        // the middle, negative darkens it.
        var gamma = Midtone >= 0 ? 1f / (1f + Midtone / 100f) : 1f - Midtone / 100f;

        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var stretched = Math.Clamp((i - black) / span, 0f, 1f);
            var adjusted = MathF.Pow(stretched, gamma);
            table[i] = EffectColorFilters.Clamp(adjusted * 255f);
        }

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(CurveChannel.All, table));
    }
}
