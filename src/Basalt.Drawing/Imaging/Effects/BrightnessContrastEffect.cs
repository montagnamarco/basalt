using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Raises or lowers brightness and contrast. GDI+ documents
/// <see cref="BrightnessLevel"/> in the range -255 through 255 and
/// <see cref="ContrastLevel"/> in the range -100 through 100.
/// </summary>
/// <remarks>
/// The formula below - contrast as a scale around mid-grey, squared the way a
/// commonly published description of GDI+'s native brightness/contrast
/// adjustment describes it, then brightness added afterwards - is not
/// compared with Windows pixel by pixel.
/// </remarks>
public class BrightnessContrastEffect : Effect
{
    public BrightnessContrastEffect(int brightnessLevel, int contrastLevel)
    {
        if (brightnessLevel < -255 || brightnessLevel > 255) throw new ArgumentException("Parameter is not valid.", nameof(brightnessLevel));
        if (contrastLevel < -100 || contrastLevel > 100) throw new ArgumentException("Parameter is not valid.", nameof(contrastLevel));

        BrightnessLevel = brightnessLevel;
        ContrastLevel = contrastLevel;
    }

    public int BrightnessLevel { get; }

    public int ContrastLevel { get; }

    internal override SKImageFilter CreateFilter()
    {
        var factor = (100f + ContrastLevel) / 100f;
        factor *= factor;

        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var normalized = (i / 255f - 0.5f) * factor + 0.5f;
            table[i] = EffectColorFilters.Clamp(normalized * 255f + BrightnessLevel);
        }

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(CurveChannel.All, table));
    }
}
