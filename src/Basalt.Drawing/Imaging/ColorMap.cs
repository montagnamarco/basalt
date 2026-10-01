namespace System.Drawing.Imaging;

/// <summary>One entry of a remap table: a pixel of exactly <see cref="OldColor"/> becomes <see cref="NewColor"/>.</summary>
public sealed class ColorMap
{
    public ColorMap()
    {
    }

    public Color OldColor { get; set; }

    public Color NewColor { get; set; }
}
