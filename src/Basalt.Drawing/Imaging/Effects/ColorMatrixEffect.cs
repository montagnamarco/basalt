using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>A straight 5x5 colour matrix applied to every pixel, the same transform <see cref="ImageAttributes.SetColorMatrix"/> compiles.</summary>
public class ColorMatrixEffect : Effect
{
    public ColorMatrixEffect(ColorMatrix matrix)
    {
        ArgumentNullException.ThrowIfNull(matrix);

        Matrix = matrix;
    }

    public ColorMatrix Matrix { get; }

    internal override SKImageFilter CreateFilter() => SKImageFilter.CreateColorFilter(ToColorFilter(Matrix));

    // The same conversion ImageAttributes uses, translation column in 0..1.
    private static SKColorFilter ToColorFilter(ColorMatrix matrix) =>
        ImageAttributesColorFilters.Matrix(matrix, ColorMatrixFlag.Default);
}

/// <summary>Converts every pixel to grey.</summary>
/// <remarks>
/// The weights below (0.299, 0.587, 0.114) are the NTSC luma coefficients
/// commonly published as "the" GDI+ grayscale ColorMatrix; the native
/// GrayScaleEffect's own built-in matrix is not published, so this is read
/// from that common source rather than compared with Windows pixel by pixel.
/// </remarks>
public sealed class GrayScaleEffect : ColorMatrixEffect
{
    public GrayScaleEffect()
        : base(new ColorMatrix(
        [
            [0.299f, 0.299f, 0.299f, 0f, 0f],
            [0.587f, 0.587f, 0.587f, 0f, 0f],
            [0.114f, 0.114f, 0.114f, 0f, 0f],
            [0f, 0f, 0f, 1f, 0f],
            [0f, 0f, 0f, 0f, 1f],
        ]))
    {
    }
}

/// <summary>Inverts every pixel: R' = 1 - R and so on, alpha untouched.</summary>
/// <remarks>Unlike the other three presets here, "invert" has only one sensible matrix, so this needs no disclaimer about matching Windows.</remarks>
public sealed class InvertEffect : ColorMatrixEffect
{
    public InvertEffect()
        : base(new ColorMatrix(
        [
            [-1f, 0f, 0f, 0f, 0f],
            [0f, -1f, 0f, 0f, 0f],
            [0f, 0f, -1f, 0f, 0f],
            [0f, 0f, 0f, 1f, 0f],
            [1f, 1f, 1f, 0f, 1f],
        ]))
    {
    }
}

/// <summary>Tints every pixel a sepia brown.</summary>
/// <remarks>
/// The matrix below is a widely published "sepia tone" transform (popularised
/// for HTML5 canvas filters); GDI+'s native SepiaEffect does not publish its
/// own matrix, so this is the closest documented approximation, not compared
/// with Windows pixel by pixel.
/// </remarks>
public sealed class SepiaEffect : ColorMatrixEffect
{
    public SepiaEffect()
        : base(new ColorMatrix(
        [
            [0.393f, 0.349f, 0.272f, 0f, 0f],
            [0.769f, 0.686f, 0.534f, 0f, 0f],
            [0.189f, 0.168f, 0.131f, 0f, 0f],
            [0f, 0f, 0f, 1f, 0f],
            [0f, 0f, 0f, 0f, 1f],
        ]))
    {
    }
}

/// <summary>Boosts colour saturation.</summary>
/// <remarks>
/// Built from the widely published saturation matrix (Graphica Obscura's
/// luminance weights, with a 50% saturation boost) as the closest documented
/// reading of "vivid": GDI+ publishes neither a formula nor a strength for its
/// native VividEffect, so this is not compared with Windows pixel by pixel.
/// </remarks>
public sealed class VividEffect : ColorMatrixEffect
{
    public VividEffect()
        : base(Saturate(1.5f))
    {
    }

    private static ColorMatrix Saturate(float s)
    {
        const float lr = 0.3086f;
        const float lg = 0.6094f;
        const float lb = 0.0820f;

        return new ColorMatrix(
        [
            [lr * (1f - s) + s, lr * (1f - s), lr * (1f - s), 0f, 0f],
            [lg * (1f - s), lg * (1f - s) + s, lg * (1f - s), 0f, 0f],
            [lb * (1f - s), lb * (1f - s), lb * (1f - s) + s, 0f, 0f],
            [0f, 0f, 0f, 1f, 0f],
            [0f, 0f, 0f, 0f, 1f],
        ]);
    }
}
