using System.Globalization;
using SkiaSharp;

namespace System.Drawing.Imaging.Effects;

/// <summary>
/// Shared building blocks for the effects that work through a 256-entry
/// lookup table or a small SkSL program, rather than a straight colour
/// matrix (<see cref="ImageAttributesColorFilters.Matrix"/> already covers
/// that case and <see cref="ColorMatrixEffect"/> calls it directly).
/// </summary>
/// <remarks>
/// <see cref="SKColorFilter.CreateTable"/> runs on Skia's premultiplied
/// colour, unlike GDI+'s own adjustments, which work on the straight colour.
/// The two agree at alpha 255, the only case the tests here exercise; a
/// translucent pixel would read back slightly different from GDI+, which is
/// an acknowledged gap rather than a measured one.
/// </remarks>
internal static class EffectColorFilters
{
    public static byte[] Identity()
    {
        var table = new byte[256];
        for (var i = 0; i < 256; i++) table[i] = (byte)i;
        return table;
    }

    public static byte Clamp(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    /// <summary>Applies a 256-entry table to one channel, or to red, green and blue together for <see cref="CurveChannel.All"/>; alpha is always left alone, as GDI+ documents for its curve adjustments.</summary>
    public static SKColorFilter FromChannelTable(CurveChannel channel, byte[] table)
    {
        var identity = Identity();

        return channel switch
        {
            CurveChannel.All => SKColorFilter.CreateTable(identity, table, table, table),
            CurveChannel.Red => SKColorFilter.CreateTable(identity, table, identity, identity),
            CurveChannel.Green => SKColorFilter.CreateTable(identity, identity, table, identity),
            CurveChannel.Blue => SKColorFilter.CreateTable(identity, identity, identity, table),
            _ => throw new ArgumentException("Parameter is not valid.", nameof(channel)),
        };
    }

    /// <summary>
    /// Compiles an "adjust" function written for straight colours into a
    /// filter, the same wrapper <see cref="ImageAttributesColorFilters"/>
    /// uses for its own SkSL filters (duplicated here since that method is
    /// private to its own class).
    /// </summary>
    public static SKColorFilter CompileStraight(string adjust)
    {
        var sksl = adjust + """

            half4 main(half4 color) {
                half4 straight = color.a > 0.0 ? half4(color.rgb / color.a, color.a) : half4(0.0);
                half4 result = adjust(straight);
                return half4(result.rgb * result.a, result.a);
            }
            """;

        using var effect = SKRuntimeEffect.CreateColorFilter(sksl, out var errors);

        if (effect is null)
        {
            throw new InvalidOperationException($"The generated SkSL colour filter did not compile: {errors}\n{sksl}");
        }

        return effect.ToColorFilter();
    }

    public static string Literal(float value) => value.ToString("0.0###########", CultureInfo.InvariantCulture);
}
