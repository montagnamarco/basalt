using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Sharpens a bitmap by unsharp masking. GDI+ documents <see cref="Radius"/>,
/// the radius of the convolution kernel in pixels (0 through 255), and
/// <see cref="Amount"/>, the strength of the sharpening as a percentage (0
/// through 100), but not the exact kernel it builds from them.
/// </summary>
/// <remarks>
/// This blurs by <see cref="Radius"/> and then pushes every pixel away from
/// that blur by <see cref="Amount"/> percent - the standard unsharp-mask
/// reading of the same two parameters - which is not compared with Windows
/// pixel by pixel.
/// </remarks>
public class SharpenEffect : Effect
{
    public SharpenEffect(float radius, float amount)
    {
        if (radius < 0f || radius > 255f) throw new ArgumentException("Parameter is not valid.", nameof(radius));
        if (amount < 0f || amount > 100f) throw new ArgumentException("Parameter is not valid.", nameof(amount));

        Radius = radius;
        Amount = amount;
    }

    public float Radius { get; }

    public float Amount { get; }

    internal override SKImageFilter CreateFilter()
    {
        if (Radius <= 0f || Amount <= 0f) return SKImageFilter.CreateOffset(0f, 0f);

        var strength = Amount / 100f;

        using var blurred = SKImageFilter.CreateBlur(Radius, Radius);
        using var original = SKImageFilter.CreateOffset(0f, 0f);

        // result = (1 + strength) * original - strength * blurred: every
        // pixel is pushed away from its own blurred neighbourhood, which
        // exaggerates edges while leaving flat areas unchanged.
        return SKImageFilter.CreateArithmetic(0f, 1f + strength, -strength, 0f, enforcePMColor: true, blurred, original);
    }
}
