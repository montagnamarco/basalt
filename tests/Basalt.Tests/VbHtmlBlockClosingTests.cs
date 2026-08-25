using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Block keywords that close without an at sign.
/// </summary>
/// <remarks>
/// Razor writes a block's closing keyword bare, on its own line, and so does
/// anyone used to Visual Basic. The parser only recognised "@End If", so the
/// bare form was swallowed into the markup: the page rendered the words
/// "End If" and the block ran on to the end of the file. It compiled, which
/// is why it survived — the generated code was valid, just wrong.
/// </remarks>
public sealed class VbHtmlBlockClosingTests
{
    private static string Generate(string template)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return VbHtmlCodeWriter.Write(document, "View", "Generated");
    }

    [Fact]
    public void BareEndIfClosesTheBlock()
    {
        var code = Generate("@If ok Then\n<p>yes</p>\nEnd If\n");

        Assert.Contains("If ok Then", code);
        Assert.Contains("End If", code);

        // The words must not reach the page.
        Assert.DoesNotContain("\"End If\"", code);
    }

    [Fact]
    public void BareNextClosesALoop()
    {
        var code = Generate("@For Each x In items\n<li>ok</li>\nNext\n");

        Assert.Contains("For Each x In items", code);
        Assert.DoesNotContain("\"Next\"", code);
    }

    [Fact]
    public void NextNamingItsVariableStillCloses()
    {
        var code = Generate("@For i = 1 To 3\n<li>ok</li>\nNext i\n");

        Assert.Contains("Next i", code);
        Assert.DoesNotContain("\"Next i\"", code);
    }

    [Theory]
    [InlineData("Next steps are listed below.")]
    [InlineData("Loop the tape twice.")]
    public void ProseThatOpensWithAKeywordStaysProse(string sentence)
    {
        // Only a keyword alone on its line closes a block. A sentence that
        // happens to start with one is text, and reading it as a closing
        // keyword would truncate the page at that paragraph.
        var document = VbHtmlParser.Parse($"<p>{sentence}</p>\n");
        Assert.Empty(document.Diagnostics);

        var code = VbHtmlCodeWriter.Write(document, "View", "Generated");
        Assert.Contains(sentence, code);
    }

    [Fact]
    public void TheAtSignFormStillWorks()
    {
        // The form that already worked must keep working: a project written
        // against it cannot break because the bare form was added.
        var code = Generate("@If ok Then\n<p>yes</p>\n@End If\n");

        Assert.Contains("End If", code);
        Assert.DoesNotContain("\"End If\"", code);
    }
}
