using System.Text;
using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Tints every pixel toward one hue while keeping its own lightness. GDI+
/// documents <see cref="Hue"/> in degrees, 0 through 359, and
/// <see cref="Amount"/> as a percentage, 0 through 100.
/// </summary>
/// <remarks>
/// GDI+ does not publish the exact blend: this computes each pixel's
/// luminance, builds the fully saturated colour of that lightness at
/// <see cref="Hue"/>, and mixes toward it by <see cref="Amount"/> percent -
/// the closest documented behaviour, not compared with Windows pixel by
/// pixel.
/// </remarks>
public class TintEffect : Effect
{
    public TintEffect(Color color, int amount)
        : this(HueOf(color), amount)
    {
    }

    public TintEffect(int hue, int amount)
    {
        if (hue < 0 || hue > 359) throw new ArgumentException("Parameter is not valid.", nameof(hue));
        if (amount < 0 || amount > 100) throw new ArgumentException("Parameter is not valid.", nameof(amount));

        Hue = hue;
        Amount = amount;
    }

    public int Hue { get; }

    public int Amount { get; }

    internal override SKImageFilter CreateFilter()
    {
        var (weightR, weightG, weightB) = HueWeights(Hue);
        var amount01 = Amount / 100f;

        var sksl = new StringBuilder();
        sksl.Append("half4 adjust(half4 c) {\n");
        sksl.Append("    half luma = dot(c.rgb, half3(0.299, 0.587, 0.114));\n");
        sksl.Append("    half chroma = 1.0 - abs(2.0 * luma - 1.0);\n");
        sksl.Append("    half3 target = half3(luma) + chroma * half3(")
            .Append(EffectColorFilters.Literal(weightR - 0.5f)).Append(", ")
            .Append(EffectColorFilters.Literal(weightG - 0.5f)).Append(", ")
            .Append(EffectColorFilters.Literal(weightB - 0.5f)).Append(");\n");
        sksl.Append("    half3 mixed = mix(c.rgb, clamp(target, 0.0, 1.0), ").Append(EffectColorFilters.Literal(amount01)).Append(");\n");
        sksl.Append("    return half4(mixed, c.a);\n");
        sksl.Append('}');

        return SKImageFilter.CreateColorFilter(EffectColorFilters.CompileStraight(sksl.ToString()));
    }

    /// <summary>The (R, G, B) coefficients of the hue's chroma, following the standard HSL-to-RGB sector table at full saturation.</summary>
    private static (float R, float G, float B) HueWeights(int hue)
    {
        var hPrime = hue / 60f;
        var hMod2 = hPrime - 2f * MathF.Floor(hPrime / 2f);
        var x = 1f - MathF.Abs(hMod2 - 1f);
        var sector = (int)MathF.Floor(hPrime) % 6;

        return sector switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };
    }

    private static int HueOf(Color color)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;

        if (delta <= 0f) return 0;

        float hue;
        if (max == r) hue = 60f * (((g - b) / delta) % 6f);
        else if (max == g) hue = 60f * (((b - r) / delta) + 2f);
        else hue = 60f * (((r - g) / delta) + 4f);

        if (hue < 0f) hue += 360f;

        return Math.Clamp((int)MathF.Round(hue), 0, 359);
    }
}
