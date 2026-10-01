using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Blurs a bitmap by a Gaussian kernel. GDI+ documents <see cref="Radius"/> as
/// the radius of that kernel in pixels, 0 through 255, and
/// <see cref="ExpandEdge"/> as whether the blur reaches past the bitmap's own
/// edge or is held inside it.
/// </summary>
/// <remarks>
/// The radius-to-standard-deviation conversion GDI+'s native implementation
/// uses is not published, so this reads the radius directly as the kernel's
/// standard deviation, the simplest value consistent with the documented
/// range; not compared with Windows pixel by pixel.
/// </remarks>
public class BlurEffect : Effect
{
    public BlurEffect(float radius, bool expandEdge)
    {
        if (radius < 0f || radius > 255f) throw new ArgumentException("Parameter is not valid.", nameof(radius));

        Radius = radius;
        ExpandEdge = expandEdge;
    }

    public float Radius { get; }

    public bool ExpandEdge { get; }

    internal override SKImageFilter CreateFilter()
    {
        // A radius of 0 asks for no blur at all; CreateBlur with a sigma of 0
        // is not guaranteed to behave as the identity, so this is a plain
        // no-op filter instead.
        if (Radius <= 0f) return SKImageFilter.CreateOffset(0f, 0f);

        // Decal lets the kernel sample transparent past the edge, which reads
        // as the bitmap "expanding" into that transparency; Clamp repeats the
        // edge pixel instead, holding the blur inside the original bounds, as
        // GDI+ documents for ExpandEdge = false.
        var tileMode = ExpandEdge ? SKShaderTileMode.Decal : SKShaderTileMode.Clamp;

        return SKImageFilter.CreateBlur(Radius, Radius, tileMode);
    }
}
