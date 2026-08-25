using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// <c>@&lt;p&gt;</c>: markup written inside a code block.
/// </summary>
/// <remarks>
/// Razor needs the mark because inside a block a statement and an element
/// cannot otherwise be told apart. It was not implemented, and a template
/// using it failed to parse: "'If' is not closed by a matching 'End If'",
/// which points at the block rather than at the line that broke it.
/// </remarks>
public sealed class VbHtmlMarkupTransitionTests
{
    private static string Generate(string template)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return VbHtmlCodeWriter.Write(document, "View", "Generated");
    }

    [Fact]
    public void AnElementInsideABlockIsMarkup()
    {
        var code = Generate("@If ok Then\n    @<p>yes</p>\nEnd If\n");

        Assert.Contains("If ok Then", code);
        Assert.Contains("""WriteLiteral("<p>yes</p>")""", code);
    }

    [Fact]
    public void NestingOfTheSameElementIsCounted()
    {
        // A div holding a div must not end at the inner closing tag, which
        // would spill the rest of the block into the page as text.
        var code = Generate("@If ok Then\n    @<div><div>in</div></div>\nEnd If\n");

        Assert.Contains("""WriteLiteral("<div><div>in</div></div>")""", code);
    }

    [Fact]
    public void ASelfClosingElementEndsAtItself()
    {
        var code = Generate("@If ok Then\n    @<br />\nEnd If\n");

        Assert.Contains("""WriteLiteral("<br />")""", code);
    }

    [Fact]
    public void TheTextElementStillDisappears()
    {
        // @<text> wraps content without the tag reaching the page, and adding
        // the general form must not have changed that.
        var code = Generate("@If ok Then\n    @<text>bare</text>\nEnd If\n");

        Assert.Contains("bare", code);
        Assert.DoesNotContain("<text>", code);
    }

    [Fact]
    public void AComparisonIsNotAnElement()
    {
        // "@If a < b Then" has a less-than sign where an element could start.
        var code = Generate("@If a < b Then\n<p>x</p>\nEnd If\n");

        Assert.Contains("If a < b Then", code);
    }
}
