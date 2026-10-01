using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Pushes red against cyan, green against magenta and blue against yellow.
/// GDI+ documents all three levels in the range -100 through 100.
/// </summary>
/// <remarks>
/// Each channel is pushed toward 255 (a positive level) or toward 0 (a
/// negative level) by that percentage of its remaining headroom - a commonly
/// published reading of "colour balance" - which is not compared with Windows
/// pixel by pixel.
/// </remarks>
public class ColorBalanceEffect : Effect
{
    public ColorBalanceEffect(int cyanRed, int magentaGreen, int yellowBlue)
    {
        Check(cyanRed, nameof(cyanRed));
        Check(magentaGreen, nameof(magentaGreen));
        Check(yellowBlue, nameof(yellowBlue));

        CyanRed = cyanRed;
        MagentaGreen = magentaGreen;
        YellowBlue = yellowBlue;
    }

    public int CyanRed { get; }

    public int MagentaGreen { get; }

    public int YellowBlue { get; }

    internal override SKImageFilter CreateFilter() =>
        SKImageFilter.CreateColorFilter(SKColorFilter.CreateTable(
            EffectColorFilters.Identity(),
            Table(CyanRed),
            Table(MagentaGreen),
            Table(YellowBlue)));

    private static byte[] Table(int balance)
    {
        var table = new byte[256];

        for (var i = 0; i < 256; i++)
        {
            var pushed = balance >= 0 ? i + (255 - i) * balance / 100f : i + i * balance / 100f;
            table[i] = EffectColorFilters.Clamp(pushed);
        }

        return table;
    }

    private static void Check(int value, string name)
    {
        if (value < -100 || value > 100) throw new ArgumentException("Parameter is not valid.", name);
    }
}
