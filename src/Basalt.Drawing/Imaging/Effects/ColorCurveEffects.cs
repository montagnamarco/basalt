using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

// GDI+ does not publish the exact per-pixel formula behind any of these eight
// curves, only the parameter each takes and its documented range; every
// formula below is the closest documented reading of that name and has not
// been compared with Windows pixel by pixel.

/// <summary>Raises or lowers exposure. GDI+ documents <see cref="Exposure"/> in the range -255 through 255.</summary>
public class ExposureCurveEffect : ColorCurveEffect
{
    public ExposureCurveEffect(CurveChannel channel, int exposure)
        : base(channel)
    {
        if (exposure < -255 || exposure > 255) throw new ArgumentException("Parameter is not valid.", nameof(exposure));

        Exposure = exposure;
    }

    public int Exposure { get; }

    internal override SKImageFilter CreateFilter()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp(i + Exposure);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Raises or lowers density, the opposite sense of exposure. GDI+ documents <see cref="Density"/> in the range -255 through 255.</summary>
public class DensityCurveEffect : ColorCurveEffect
{
    public DensityCurveEffect(CurveChannel channel, int density)
        : base(channel)
    {
        if (density < -255 || density > 255) throw new ArgumentException("Parameter is not valid.", nameof(density));

        Density = density;
    }

    public int Density { get; }

    internal override SKImageFilter CreateFilter()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp(i - Density);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Scales values around mid-grey. GDI+ documents <see cref="Contrast"/> in the range -100 through 100.</summary>
public class ContrastCurveEffect : ColorCurveEffect
{
    public ContrastCurveEffect(CurveChannel channel, int contrast)
        : base(channel)
    {
        if (contrast < -100 || contrast > 100) throw new ArgumentException("Parameter is not valid.", nameof(contrast));

        Contrast = contrast;
    }

    public int Contrast { get; }

    internal override SKImageFilter CreateFilter()
    {
        var factor = (100f + Contrast) / 100f;
        factor *= factor;

        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp((i / 255f - 0.5f) * factor * 255f + 127.5f);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Pushes the upper half of the tonal range toward white. GDI+ documents <see cref="Highlight"/> in the range -100 through 100.</summary>
public class HighlightCurveEffect : ColorCurveEffect
{
    public HighlightCurveEffect(CurveChannel channel, int highlight)
        : base(channel)
    {
        if (highlight < -100 || highlight > 100) throw new ArgumentException("Parameter is not valid.", nameof(highlight));

        Highlight = highlight;
    }

    public int Highlight { get; }

    internal override SKImageFilter CreateFilter()
    {
        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var weight = i > 128 ? (i - 128) / 127f : 0f;
            table[i] = EffectColorFilters.Clamp(i + weight * Highlight);
        }

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Pushes the lower half of the tonal range toward black. GDI+ documents <see cref="Shadow"/> in the range -100 through 100.</summary>
public class ShadowCurveEffect : ColorCurveEffect
{
    public ShadowCurveEffect(CurveChannel channel, int shadow)
        : base(channel)
    {
        if (shadow < -100 || shadow > 100) throw new ArgumentException("Parameter is not valid.", nameof(shadow));

        Shadow = shadow;
    }

    public int Shadow { get; }

    internal override SKImageFilter CreateFilter()
    {
        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var weight = i < 128 ? (128 - i) / 128f : 0f;

            // A positive shadow lifts dark pixels toward white; a negative
            // one crushes them further toward black.
            table[i] = EffectColorFilters.Clamp(i - weight * Shadow);
        }

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>A gamma around the middle of the tonal range. GDI+ documents <see cref="Midtone"/> in the range -100 through 100.</summary>
public class MidtoneCurveEffect : ColorCurveEffect
{
    public MidtoneCurveEffect(CurveChannel channel, int midtone)
        : base(channel)
    {
        if (midtone < -100 || midtone > 100) throw new ArgumentException("Parameter is not valid.", nameof(midtone));

        Midtone = midtone;
    }

    public int Midtone { get; }

    internal override SKImageFilter CreateFilter()
    {
        var gamma = Midtone >= 0 ? 1f / (1f + Midtone / 100f) : 1f - Midtone / 100f;

        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp(MathF.Pow(i / 255f, gamma) * 255f);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Brings the white point down, clipping highlights sooner. GDI+ documents <see cref="WhiteSaturation"/> in the range 0 through 255.</summary>
public class WhiteSaturationCurveEffect : ColorCurveEffect
{
    public WhiteSaturationCurveEffect(CurveChannel channel, int whiteSaturation)
        : base(channel)
    {
        if (whiteSaturation < 0 || whiteSaturation > 255) throw new ArgumentException("Parameter is not valid.", nameof(whiteSaturation));

        WhiteSaturation = whiteSaturation;
    }

    public int WhiteSaturation { get; }

    internal override SKImageFilter CreateFilter()
    {
        var span = MathF.Max(1f, 255f - WhiteSaturation);

        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp(i * 255f / span);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}

/// <summary>Brings the black point up, clipping shadows sooner. GDI+ documents <see cref="BlackSaturation"/> in the range 0 through 255.</summary>
public class BlackSaturationCurveEffect : ColorCurveEffect
{
    public BlackSaturationCurveEffect(CurveChannel channel, int blackSaturation)
        : base(channel)
    {
        if (blackSaturation < 0 || blackSaturation > 255) throw new ArgumentException("Parameter is not valid.", nameof(blackSaturation));

        BlackSaturation = blackSaturation;
    }

    public int BlackSaturation { get; }

    internal override SKImageFilter CreateFilter()
    {
        var span = MathF.Max(1f, 255f - BlackSaturation);

        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = EffectColorFilters.Clamp((i - BlackSaturation) * 255f / span);

        return SKImageFilter.CreateColorFilter(EffectColorFilters.FromChannelTable(Channel, table));
    }
}
