using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The constructs a template silently got wrong.
///
/// Each of these produced a page that differed from what the template said:
/// a comment written into the output, a colon in the text, an email address
/// read as code. They are bugs rather than gaps, which is why they are
/// together and why each has a test that would have caught it.
/// </summary>
public sealed class VbHtmlCorrectnessTests
{
    private static string Render(string template)
    {
        var document = VbHtmlParser.Parse(template);

        Assert.Empty(document.Diagnostics);

        return string.Concat(document.Nodes.Select(Describe));
    }

    /// <summary>What a node contributes to the page, as text.</summary>
    private static string Describe(VbHtmlNode node) => node switch
    {
        HtmlNode html => html.Text,
        ExpressionNode expression => $"[{expression.Expression}]",
        _ => ""
    };

    // Comments

    [Fact]
    public void ACommentProducesNothingAtAll()
    {
        // It used to be written into the page, while the editor coloured it
        // grey as a comment: the reader and the compiler disagreed.
        Assert.Equal("<p></p>", Render("<p>@* hidden *@</p>"));
    }

    [Fact]
    public void ACommentMayContainMarkupAndAtSigns()
    {
        Assert.Equal("ab", Render("a@* <b>@Model.Name</b> *@b"));
    }

    [Fact]
    public void ACommentMaySpanLines()
    {
        Assert.Equal("ab", Render("a@*\nline one\nline two\n*@b"));
    }

    [Fact]
    public void SaysWhenACommentIsNeverClosed()
    {
        // Rather than swallowing the rest of the file in silence.
        var document = VbHtmlParser.Parse("<p>@* never closed</p>");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH003");
    }

    // Email addresses

    [Fact]
    public void AnEmailAddressIsNotCode()
    {
        // "info@example.com" used to become Write(example.com).
        Assert.Equal("info@example.com", Render("info@example.com"));
    }

    [Fact]
    public void AHandleAfterAWordIsNotCode()
    {
        Assert.Equal("write to me@here", Render("write to me@here"));
    }

    [Fact]
    public void AnAtSignStartingAWordIsStillCode()
    {
        // The heuristic must not swallow real transitions: only an at sign
        // inside a word is part of the word.
        Assert.Equal("[Name]", Render("@Name"));
        Assert.Equal("hello [Name]", Render("hello @Name"));
    }

    // @: line output

    [Fact]
    public void ColonOutputsTheRestOfTheLine()
    {
        // The colon itself used to appear in the page.
        Assert.Equal("plain text\n", Render("@:plain text\n"));
    }

    [Fact]
    public void ColonOutputStillWritesExpressions()
    {
        Assert.Equal("hello [Model.Name]\n", Render("@:hello @Model.Name\n"));
    }

    [Fact]
    public void ColonOutputEndsAtTheLine()
    {
        Assert.Equal("first\n<p>after</p>", Render("@:first\n<p>after</p>"));
    }

    // <text> blocks

    [Fact]
    public void TextTagsDoNotAppearInThePage()
    {
        // The tag used to be written into the output as an element.
        Assert.Equal("inside", Render("@<text>inside</text>"));
    }

    [Fact]
    public void TextBlocksStillWriteExpressions()
    {
        Assert.Equal("a[Model.Name]b", Render("@<text>a@Model.Name b</text>").Replace(" b", "b"));
    }

    [Fact]
    public void SaysWhenATextBlockIsNeverClosed()
    {
        var document = VbHtmlParser.Parse("@<text>never closed");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH004");
    }

    // What already worked, so the fixes did not break it

    [Fact]
    public void StillEscapesADoubledAtSign()
    {
        Assert.Equal("@media", Render("@@media"));
    }

    [Fact]
    public void StillWritesAPlainExpression()
    {
        Assert.Equal("<p>[Model.Title]</p>", Render("<p>@Model.Title</p>"));
    }

    // @Inherits

    [Fact]
    public void InheritsIsADirectiveNotAnExpression()
    {
        // The editor's grammar always coloured it as a directive while the
        // parser wrote the word "Inherits" into the page.
        var document = VbHtmlParser.Parse("@Inherits App.Views.BaseView\n<p>a</p>");

        Assert.Equal("App.Views.BaseView", document.Inherits);
        Assert.DoesNotContain(document.Nodes.OfType<ExpressionNode>(),
            e => e.Expression.Contains("Inherits", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGeneratedClassInheritsWhatTheTemplateAskedFor()
    {
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("@Inherits App.Views.BaseView"), "Page", "App.Views");

        Assert.Contains("Inherits App.Views.BaseView", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutInheritsTheViewGetsTheRuntimeBase()
    {
        var code = VbHtmlCodeWriter.Write(VbHtmlParser.Parse("<p>a</p>"), "Page", "App.Views");

        Assert.Contains($"Inherits {VbHtmlCodeWriter.BaseTypeName}", code,
            StringComparison.Ordinal);
    }

    // @Code that contains its own closing keyword

    [Fact]
    public void AStringInsideACodeBlockMayContainTheClosingKeyword()
    {
        // "Dim s = \"End Code\"" used to close the block halfway through.
        var document = VbHtmlParser.Parse("""
            @Code
                Dim s = "End Code"
                Dim n = 1
            End Code
            """);

        Assert.Empty(document.Diagnostics);

        var statements = Assert.Single(document.Nodes.OfType<StatementNode>());

        Assert.Contains("Dim n = 1", statements.Code, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommentInsideACodeBlockMayContainTheClosingKeyword()
    {
        var document = VbHtmlParser.Parse("""
            @Code
                ' this mentions End Code in passing
                Dim n = 1
            End Code
            """);

        Assert.Empty(document.Diagnostics);

        var statements = Assert.Single(document.Nodes.OfType<StatementNode>());

        Assert.Contains("Dim n = 1", statements.Code, StringComparison.Ordinal);
    }

    // Single-line If

    [Fact]
    public void ASingleLineIfIsAStatementNotABlock()
    {
        // It used to be read as a block and swallow the rest of the file
        // looking for an "@End If" that was never coming.
        var document = VbHtmlParser.Parse("@Code\n@If x Then Show()\nEnd Code");

        Assert.DoesNotContain(document.Diagnostics, d => d.Id == "VBH002");
    }

    [Fact]
    public void ASingleLineIfLeavesTheMarkupAfterItAlone()
    {
        var document = VbHtmlParser.Parse("@If x Then Show()\n<p>after</p>");

        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.Nodes.OfType<HtmlNode>(),
            h => h.Text.Contains("after", StringComparison.Ordinal));
    }

    [Fact]
    public void AMultiLineIfIsStillABlock()
    {
        // The fix must not turn every If into a statement.
        var document = VbHtmlParser.Parse("@If x Then\n<p>a</p>\n@End If");

        Assert.Empty(document.Diagnostics);
        Assert.Single(document.Nodes.OfType<BlockNode>());
    }

    // Next with a loop variable

    [Fact]
    public void TheLoopVariableStaysWithTheClosingKeyword()
    {
        // "@Next i" used to drop the i, which reappeared as literal text.
        var document = VbHtmlParser.Parse("@For Each i In items\n<p>@i</p>\n@Next i");

        var block = Assert.Single(document.Nodes.OfType<BlockNode>());

        Assert.Equal("Next i", block.Closing);
    }

    [Fact]
    public void TheLoopVariableDoesNotLeakIntoThePage()
    {
        var document = VbHtmlParser.Parse("@For Each i In items\n<p>a</p>\n@Next i");

        Assert.DoesNotContain(document.Nodes.OfType<HtmlNode>(),
            h => h.Text.Trim() == "i");
    }

    [Fact]
    public void ANextWithoutAVariableIsUnchanged()
    {
        var document = VbHtmlParser.Parse("@For Each i In items\n<p>a</p>\n@Next");

        var block = Assert.Single(document.Nodes.OfType<BlockNode>());

        Assert.Equal("Next", block.Closing);
    }
}
