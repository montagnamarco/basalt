using System.Globalization;
using System.Text;
using SkiaSharp;

namespace System.Drawing.Imaging;

/// <summary>
/// Builds the SkiaSharp colour filters behind <see cref="ImageAttributes.ToColorFilter"/>.
/// Most adjustments translate to one of SkiaSharp's built-in filters; a remap
/// table and a colour key need a per-pixel decision a single built-in filter
/// cannot express, and so does the gray-preserving matrix
/// (<see cref="ColorMatrixFlag.SkipGrays"/>): those three are written as a
/// small SkSL program, compiled once per call since the colours involved are
/// baked into the source rather than passed as uniforms.
/// </summary>
internal static class ImageAttributesColorFilters
{
    // Half the gap between two adjacent 8-bit levels (1/255): a pixel at the
    // exact boundary must compare equal even after the float round-trip
    // through half precision inside the shader, but a pixel one level away
    // must not.
    private const float Epsilon = 1f / 255f / 2f;

    /// <summary>Applies <paramref name="first"/> and then <paramref name="second"/>, skipping whichever of the two is absent.</summary>
    public static SKColorFilter? Then(SKColorFilter? first, SKColorFilter? second)
    {
        if (first is null) return second;
        if (second is null) return first;

        // CreateCompose(outer, inner) runs inner first: first is our inner
        // step. The composition holds its own references to both, so the two
        // wrappers can go now instead of waiting for the finalizer.
        using (first)
        using (second)
        {
            return SKColorFilter.CreateCompose(second, first);
        }
    }

    public static SKColorFilter RemapTable(IReadOnlyList<ColorMap> map)
    {
        var sksl = new StringBuilder();
        sksl.Append("half4 adjust(half4 c) {\n");

        foreach (var entry in map)
        {
            sksl.Append("    if (");
            AppendEquals(sksl, "c.r", entry.OldColor.R);
            sksl.Append(" && ");
            AppendEquals(sksl, "c.g", entry.OldColor.G);
            sksl.Append(" && ");
            AppendEquals(sksl, "c.b", entry.OldColor.B);
            sksl.Append(" && ");
            AppendEquals(sksl, "c.a", entry.OldColor.A);
            sksl.Append(") return ").Append(Half4Literal(entry.NewColor)).Append(";\n");
        }

        sksl.Append("    return c;\n");
        sksl.Append('}');

        return Compile(sksl.ToString());
    }

    public static SKColorFilter ColorKey(Color low, Color high)
    {
        var sksl = new StringBuilder();
        sksl.Append("half4 adjust(half4 c) {\n");
        sksl.Append("    if (");
        AppendRangeTest(sksl, "c.r", low.R, high.R);
        sksl.Append(" && ");
        AppendRangeTest(sksl, "c.g", low.G, high.G);
        sksl.Append(" && ");
        AppendRangeTest(sksl, "c.b", low.B, high.B);
        sksl.Append(") return half4(0.0, 0.0, 0.0, 0.0);\n");
        sksl.Append("    return c;\n");
        sksl.Append('}');

        return Compile(sksl.ToString());
    }

    /// <summary>
    /// <see cref="ColorMatrixFlag.Default"/> is SkiaSharp's own 4x5 matrix
    /// filter (see SkiaRowMajor for the translation column). It works on the straight colour, unlike a
    /// SkSL filter: measured by halving red on a pixel of alpha 128, which
    /// came out at 100 from 200. <see cref="ColorMatrixFlag.SkipGrays"/> needs
    /// the per-pixel decision only a shader gives; a GDI+ colour matrix is
    /// already the row-vector [R G B A 1] &#215; M, the exact formula written out below.
    /// </summary>
    public static SKColorFilter Matrix(ColorMatrix matrix, ColorMatrixFlag flag)
    {
        if (flag == ColorMatrixFlag.AltGrays)
        {
            throw new NotImplementedException(
                "ColorMatrixFlag.AltGrays (a second matrix for pixels where R=G=B) is not implemented yet.");
        }

        if (flag == ColorMatrixFlag.Default) return SKColorFilter.CreateColorMatrix(SkiaRowMajor(matrix));

        var sksl = new StringBuilder();
        sksl.Append("half4 adjust(half4 c) {\n");
        sksl.Append("    if (abs(c.r - c.g) < ").Append(Literal(Epsilon)).Append(" && abs(c.g - c.b) < ").Append(Literal(Epsilon)).Append(") return c;\n");

        for (var output = 0; output < 4; output++)
        {
            sksl.Append("    half out").Append(output).Append(" = ");
            sksl.Append("c.r * ").Append(Literal(matrix.Coefficient(0, output))).Append(" + ");
            sksl.Append("c.g * ").Append(Literal(matrix.Coefficient(1, output))).Append(" + ");
            sksl.Append("c.b * ").Append(Literal(matrix.Coefficient(2, output))).Append(" + ");
            sksl.Append("c.a * ").Append(Literal(matrix.Coefficient(3, output))).Append(" + ");
            sksl.Append(Literal(matrix.Coefficient(4, output))).Append(";\n");
        }

        sksl.Append("    return half4(clamp(out0, 0.0, 1.0), clamp(out1, 0.0, 1.0), clamp(out2, 0.0, 1.0), clamp(out3, 0.0, 1.0));\n");
        sksl.Append('}');

        return Compile(sksl.ToString());
    }

    /// <summary>Each of R, G and B rounds to black or white; alpha is untouched, as GDI+ documents.</summary>
    public static SKColorFilter Threshold(float threshold)
    {
        var cut = (byte)Math.Clamp(MathF.Round(threshold * 255f), 0, 255);

        var identity = new byte[SKColorFilter.TableMaxLength];
        var cutTable = new byte[SKColorFilter.TableMaxLength];

        for (var i = 0; i < identity.Length; i++)
        {
            identity[i] = (byte)i;
            cutTable[i] = i >= cut ? (byte)255 : (byte)0;
        }

        return SKColorFilter.CreateTable(identity, cutTable, cutTable, cutTable);
    }

    /// <summary>Each of R, G and B is raised to the power 1/gamma; alpha is untouched.</summary>
    public static SKColorFilter Gamma(float gamma)
    {
        var identity = new byte[SKColorFilter.TableMaxLength];
        var table = new byte[SKColorFilter.TableMaxLength];
        var exponent = 1f / gamma;

        for (var i = 0; i < identity.Length; i++)
        {
            identity[i] = (byte)i;
            var normalized = i / 255f;
            table[i] = (byte)Math.Clamp(MathF.Round(MathF.Pow(normalized, exponent) * 255f), 0, 255);
        }

        return SKColorFilter.CreateTable(identity, table, table, table);
    }

    /// <summary>GDI+'s row-vector matrix as Skia's row-major 4×5 matrix.</summary>
    /// <remarks>
    /// Both take the translation column in 0..1: Skia's matrix filter works on
    /// normalised colours. Measured: scaling it to 0..255, as older SkiaSharp
    /// needed, turned a translation of 0.5 red into full red.
    /// </remarks>
    private static float[] SkiaRowMajor(ColorMatrix matrix)
    {
        var result = new float[20];

        for (var output = 0; output < 4; output++)
        {
            for (var input = 0; input < 4; input++) result[output * 5 + input] = matrix.Coefficient(input, output);

            result[output * 5 + 4] = matrix.Coefficient(4, output);
        }

        return result;
    }

    private static void AppendEquals(StringBuilder sksl, string channel, byte level) =>
        sksl.Append("abs(").Append(channel).Append(" - ").Append(Literal(level / 255f)).Append(") < ").Append(Literal(Epsilon));

    private static void AppendRangeTest(StringBuilder sksl, string channel, byte low, byte high) =>
        sksl.Append(channel).Append(" >= ").Append(Literal(low / 255f - Epsilon))
            .Append(" && ").Append(channel).Append(" <= ").Append(Literal(high / 255f + Epsilon));

    private static string Half4Literal(Color color) =>
        $"half4({Literal(color.R / 255f)}, {Literal(color.G / 255f)}, {Literal(color.B / 255f)}, {Literal(color.A / 255f)})";

    private static string Literal(float value) => value.ToString("0.0###########", CultureInfo.InvariantCulture);

    /// <summary>
    /// Compiles an adjust function written for straight colours into a
    /// filter.
    /// </summary>
    /// <remarks>
    /// Skia hands a colour filter premultiplied colours. GDI+ compares and
    /// transforms the colours themselves: measured before this wrapper, a
    /// colour key on (200, 100, 50) missed that very colour at alpha 128,
    /// which reached the filter as (100, 50, 25).
    /// </remarks>
    private static SKColorFilter Compile(string adjust)
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
}
