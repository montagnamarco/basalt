using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// A position in the template reaching the right place in the generated code,
/// and coming back.
///
/// This is the test the drift bug should have failed. Span arithmetic put a
/// caret 17 characters short of where it belonged, and nothing noticed:
/// completion still worked because Roslyn looks around the caret, and
/// signature help — which does not — simply answered nothing.
/// </summary>
public class MappingRoundTripTests
{
    private static (string Template, VbHtmlCodeWriter.Generated Generated) Build(string template)
    {
        var generated = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "V", "N", "/v.vbhtml");

        return (template, generated);
    }

    /// <summary>
    /// Where a caret just after a fragment lands in the generated code, and
    /// what sits there.
    /// </summary>
    private static string GeneratedTextAt(string template, string upTo, int extra = 0)
    {
        var (text, generated) = Build(template);

        var caret = text.IndexOf(upTo, StringComparison.Ordinal) + upTo.Length + extra;

        var at = VbHtmlCodeRegions.CaretInGenerated(
            text, generated.Code, generated.Map, caret);

        Assert.NotNull(at);

        return generated.Code.Substring(at.Value);
    }

    [Fact]
    public void ACaretAfterAnOpeningBracketLandsOnTheArgument()
    {
        // The exact case that broke signature help.
        var after = GeneratedTextAt("<p>@p.Greet(\"hi\", 2)</p>", "@p.Greet(");

        Assert.StartsWith("\"hi\", 2)", after, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaretAfterADotLandsAfterTheSameDot()
    {
        var after = GeneratedTextAt("<p>@Model.Name</p>", "@Model.");

        Assert.StartsWith("Name", after, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaretInsideACodeBlockLandsOnTheSameText()
    {
        var after = GeneratedTextAt("""
            @Code
                Dim total = 1
            End Code
            <p>@total</p>
            """, "Dim total = ");

        Assert.StartsWith("1", after, StringComparison.Ordinal);
    }

    [Fact]
    public void ACaretOnTheSecondArgumentLandsThere()
    {
        var after = GeneratedTextAt("<p>@p.Greet(\"hi\", 2)</p>", "@p.Greet(\"hi\", ");

        Assert.StartsWith("2)", after, StringComparison.Ordinal);
    }

    [Fact]
    public void APositionComesBackToTheLineItCameFrom()
    {
        // The other direction: what the diagnostics and rename paths rely on.
        var (text, generated) = Build("""
            @Code
                Dim total = 1
            End Code
            <p>@total</p>
            """);

        var line = generated.Map.ToGeneratedLine(2);

        Assert.NotNull(line);
        Assert.Equal(2, generated.Map.OriginalLineOf(line.Value));
    }

    [Fact]
    public void EveryMappingRoundTripsByLine()
    {
        var (_, generated) = Build("""
            @Code
                Dim total = 1
            End Code
            <p>@total</p>
            <p>@total.ToString()</p>
            """);

        Assert.NotEmpty(generated.Map.Mappings);

        foreach (var mapping in generated.Map.Mappings)
        {
            Assert.Equal(
                mapping.OriginalLine,
                generated.Map.OriginalLineOf(mapping.GeneratedLine));
        }
    }
}
