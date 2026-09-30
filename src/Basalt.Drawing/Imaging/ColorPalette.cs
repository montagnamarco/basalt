namespace System.Drawing.Imaging;

/// <summary>
/// The colour table of an indexed image. This library never stores pixels in
/// an indexed format (see <see cref="PixelStorage"/>), so a bitmap's own
/// palette is always empty; the type still exists in full because code that
/// merely passes a <see cref="ColorPalette"/> around, or builds one of the two
/// palettes below, must keep compiling and running.
/// </summary>
public sealed class ColorPalette
{
    public ColorPalette(Color[] entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        Entries = entries;
        Flags = 0;
    }

    public ColorPalette(PaletteType type)
    {
        Entries = type switch
        {
            PaletteType.Custom => [],

            // The only fixed table simple enough to be worth the two colours it
            // has: the halftone tables are real colour-cube generation and are
            // future work.
            PaletteType.FixedBlackAndWhite => [Color.Black, Color.White],

            _ => throw new NotImplementedException(
                $"A fixed palette of type {type} is not implemented yet: Custom and FixedBlackAndWhite are."),
        };

        Flags = 0;
    }

    /// <summary>The empty palette every non-indexed <see cref="Image"/> reports.</summary>
    internal ColorPalette()
    {
        Entries = [];
        Flags = 0;
    }

    public Color[] Entries { get; }

    public int Flags { get; }

    public static ColorPalette CreateOptimalPalette(int colorCount, bool useTransparentColor, Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        throw new NotImplementedException("Computing an optimal palette from a bitmap's own colours is not implemented yet.");
    }
}
