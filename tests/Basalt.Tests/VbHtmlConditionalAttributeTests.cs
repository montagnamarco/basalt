using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Attributes whose whole value is one expression.
/// </summary>
/// <remarks>
/// Nothing or False must remove the attribute rather than write its value:
/// disabled="False" disables the control, because a browser reads a boolean
/// attribute's presence and not what it says.
/// </remarks>
public sealed class VbHtmlConditionalAttributeTests
{
    private static string Generate(string template)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return VbHtmlCodeWriter.Write(document, "View", "Generated");
    }

    [Fact]
    public void OneAttributeIsWrittenAsAUnit()
    {
        var code = Generate("""<button disabled="@locked">go</button>""");

        Assert.Contains("""WriteAttribute("disabled", locked)""", code);
    }

    [Fact]
    public void TwoAttributesOnTheSamePageAreBothRecognised()
    {
        // The literal that closes the first attribute opens the second. It
        // used to be consumed by the first, so the second was written as an
        // ordinary expression and rendered disabled="False".
        var code = Generate(
            """<a href="@url">x</a><button disabled="@locked">go</button>""");

        Assert.Contains("""WriteAttribute("href", url)""", code);
        Assert.Contains("""WriteAttribute("disabled", locked)""", code);
    }

    [Fact]
    public void TheMarkupBetweenTwoAttributesSurvives()
    {
        // Dropping the wrong character while re-reading the shared literal
        // would eat the tag between them.
        var code = Generate(
            """<a href="@url">first</a><button disabled="@locked">go</button>""");

        Assert.Contains(">first</a>", code);
    }

    [Fact]
    public void AComposedValueIsNotTreatedAsConditional()
    {
        // Only a whole value counts: "a@b" is a composed string, and removing
        // it on False would drop the part the template wrote by hand.
        var code = Generate("""<a href="/x/@id">go</a>""");

        Assert.DoesNotContain("WriteAttribute", code);
    }

    [Fact]
    public void DataAttributesKeepTheirValue()
    {
        var code = Generate("""<div data-open="@isOpen">x</div>""");

        Assert.DoesNotContain("WriteAttribute", code);
    }
}
