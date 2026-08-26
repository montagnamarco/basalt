using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Tidying a .vbhtml.
///
/// The provider was hard-null: a template could not be formatted at all. It
/// is deliberately narrow — the code half gets indented, the markup is left
/// exactly as written, because a formatter that rearranges someone's HTML is
/// a formatter they turn off.
/// </summary>
public sealed class VbHtmlFormattingTests
{
    private static LanguageDocument Document(string text) => new("/a/Page.vbhtml", text);

    [Fact]
    public async Task TheProviderExistsNow()
    {
        Assert.NotNull(WebLanguageProviders.VbRazor.Formatting);
    }

    [Fact]
    public async Task IndentsStatementsInsideACodeBlock()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nDim x = 1\nDim y = 2\nEnd Code"));

        Assert.True(result.Changed);
        Assert.Contains("    Dim x = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Dim y = 2", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndentsANestedBlockOneLevelFurther()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nIf x > 0 Then\nDim y = 1\nEnd If\nEnd Code"));

        Assert.Contains("    If x > 0 Then", result.Text, StringComparison.Ordinal);
        Assert.Contains("        Dim y = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    End If", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinesUpAClosingKeywordWithWhatItCloses()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nFor i = 1 To 3\nDim y = i\nNext\nEnd Code"));

        Assert.Contains("    For i = 1 To 3", result.Text, StringComparison.Ordinal);
        Assert.Contains("        Dim y = i", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Next", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesTheMarkupExactlyAsWritten()
    {
        // Someone laid this out on purpose.
        const string template = "<div>\n      <p>deliberately indented</p>\n</div>";

        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(template));

        Assert.False(result.Changed);
        Assert.Equal(template, result.Text);
    }

    [Fact]
    public async Task LeavesAlreadyTidyCodeAlone()
    {
        // Reporting a change where there is none makes the editor mark the
        // file dirty for nothing.
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\n    Dim x = 1\nEnd Code"));

        Assert.False(result.Changed);
    }

    [Fact]
    public async Task LeavesATemplateThatDoesNotParseAlone()
    {
        // Reformatting something half-understood is how a formatter eats
        // someone's work.
        const string broken = "@Code\n    Dim x = 1";

        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(broken));

        Assert.False(result.Changed);
        Assert.Equal(broken, result.Text);
    }

    [Fact]
    public async Task DoesNotTreatAnIdentifierAsABlockKeyword()
    {
        // "Iffy" starts with "If" and opens nothing.
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nIffy = 1\nDim y = 2\nEnd Code"));

        Assert.Contains("    Iffy = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Dim y = 2", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsBlankLinesWhereTheyWere()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nDim x = 1\n\nDim y = 2\nEnd Code"));

        Assert.Contains("\n\n", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void SpellsTheKeywordsTheWayVisualBasicDoes()
    {
        // Indentation alone is half the job. What made Visual Basic feel like
        // Visual Basic is that a line tidies itself: "end if" becomes "End If"
        // and "x=1" becomes "x = 1". A view used to come back tidily indented
        // and still lower case, which reads as a formatter that half works.
        var result = VbHtmlFormattingProvider.Format(
            "@Code\nDim x=1\nif x=1 then\nx=2\nend if\nEnd Code\n", caret: 0);

        Assert.Contains("Dim x = 1", result.Text);
        Assert.Contains("If x = 1 Then", result.Text);
        Assert.Contains("End If", result.Text);
    }

    [Fact]
    public void LaysOutAPageOfAngleBracketBlocks()
    {
        // A .vbpage page is written with <% %>, which the Razor parser reads as
        // one long run of markup: it found nothing to format and pages were
        // left exactly as typed while views were being tidied.
        var result = VbHtmlFormattingProvider.Format(
            "<% Dim x=1 %>\n<% if x=1 then %>\n<p>hello</p>\n<% end if %>\n", caret: 0);

        Assert.Contains("<% Dim x = 1 %>", result.Text);
        Assert.Contains("<% If x = 1 Then %>", result.Text);
        Assert.Contains("<% End If %>", result.Text);

        // The markup between the blocks is the author's business.
        Assert.Contains("<p>hello</p>", result.Text);
    }

    [Fact]
    public void FormattingTwiceChangesNothingTheSecondTime()
    {
        // Roslyn formats inside a scratch method and hands back lines indented
        // for it. Carried into a <% %> block that showed up as a page drifting
        // two characters right on every save — the kind of defect a single
        // pass looks perfectly correct.
        foreach (var source in new[]
                 {
                     "@Code\nDim x=1\nif x=1 then\nx=2\nend if\nEnd Code\n",
                     "<% Dim x=1 %>\n<% if x=1 then %>\n<p>hello</p>\n<% end if %>\n"
                 })
        {
            var once = VbHtmlFormattingProvider.Format(source, caret: 0).Text;
            var twice = VbHtmlFormattingProvider.Format(once, caret: 0).Text;

            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void LeavesABlockThatDoesNotParseAlone()
    {
        // A template is unparseable most of the time it is being typed into,
        // and a formatter that rearranges half-written code is one people turn
        // off — taking the working half with it.
        const string halfWritten = "@Code\nDim x = \nEnd Code\n";

        var result = VbHtmlFormattingProvider.Format(halfWritten, caret: 0);

        Assert.Contains("Dim x = ", result.Text);
    }

    [Fact]
    public void TidiesTheLineTheCaretJustLeft()
    {
        // What the editor asks for on Enter, and what FormatLineAsync used to
        // answer "unchanged" to every single time: it was a stub, so typing in
        // a view behaved like a plain text editor and a file was only laid out
        // when someone ran Reformat Code by hand.
        const string source = "@Code\nDim x=1\nif x=1 then\n";

        // The caret sits at the end of the line just finished, which is where
        // the editor reports Enter from.
        var caret = source.IndexOf("if x=1 then", StringComparison.Ordinal) + 5;

        var result = VbHtmlFormattingProvider.FormatLine(source, caret);

        Assert.Contains("If x = 1 Then", result.Text);

        // And only that line: stepping back one line too far corrected the one
        // above and left the line just typed exactly as it was.
        Assert.Contains("Dim x=1", result.Text);
    }

    [Fact]
    public void TidiesALineInsideAPageBlock()
    {
        const string source = "<% if x=1 then %>\n<p>hi</p>\n";

        var result = VbHtmlFormattingProvider.FormatLine(source, caret: 5);

        // Delimiters and their spacing intact: replacing the trimmed code
        // closed them up against it, giving "<%If x = 1 Then%>".
        Assert.Contains("<% If x = 1 Then %>", result.Text);
    }

    [Fact]
    public void LeavesALineOfMarkupAlone()
    {
        // "if x=1 then" inside a paragraph is prose, not code. Rewriting HTML
        // as it is typed is how a formatter gets switched off.
        const string source = "<p>ciao if x=1 then</p>\n";

        var result = VbHtmlFormattingProvider.FormatLine(source, caret: 5);

        Assert.Equal(source, result.Text);
    }

    [Theory]
    [InlineData("if x=1 then", "End If")]
    [InlineData("For Each i In xs", "Next")]
    [InlineData("While ok", "End While")]
    [InlineData("Dim x = 1", null)]
    public async Task ClosesTheBlockALineOpens(string line, string? expected)
    {
        // Enter after "If x Then" puts "End If" below, the way Visual Basic
        // has always done it. A template answered null to this: blocks inside
        // @Code had to be closed by hand while the same block in a .vb file
        // closed itself.
        var document = new LanguageDocument("/t.vbhtml", $"@Code\n{line}\nEnd Code\n");

        var closing = await new VbHtmlFormattingProvider().GetBlockClosingAsync(document, 1);

        Assert.Equal(expected, closing);
    }

    [Fact]
    public async Task ClosesAnIndentedBlockToo()
    {
        // The parser hands a block over trimmed of its leading whitespace, so
        // a caret at the start of an indented line sits before the code and
        // the block was not found at all. Every indented block — which is
        // every block anyone actually writes — silently closed nothing.
        var document = new LanguageDocument("/t.vbhtml", "@Code\n    if x=1 then\nEnd Code\n");

        Assert.Equal("End If", await new VbHtmlFormattingProvider().GetBlockClosingAsync(document, 1));
    }

    [Fact]
    public async Task IndentsByTheDepthOfTheEnclosingBlocks()
    {
        // Answering zero — which the default did — dropped every new line
        // inside a block back to the left margin.
        var text = "@Code\nIf x = 1 Then\n\nEnd If\nEnd Code\n";
        var document = new LanguageDocument("/t.vbhtml", text);
        var provider = new VbHtmlFormattingProvider();

        Assert.Equal(4, await provider.GetIndentationAsync(document, text.IndexOf("If x", StringComparison.Ordinal)));
        Assert.Equal(8, await provider.GetIndentationAsync(document, text.IndexOf("Then", StringComparison.Ordinal) + 4));
    }

    [Fact]
    public async Task WorksOnAPlainVisualBasicFileToo()
    {
        // Rider ships Roslyn's Visual Basic assemblies but wires none of the
        // typing behaviour to them: a block does not close itself, "end if"
        // stays lower case, and a new line lands at the left margin. The
        // formatter used to look for @Code or <% %> and find neither, so a
        // .vb file got nothing at all.
        const string source = "Module M\n    Sub S()\n        if x=1 then\n    End Sub\nEnd Module\n";

        var provider = new VbHtmlFormattingProvider();
        var document = new LanguageDocument("/t.vb", source);

        Assert.Equal("End If", await provider.GetBlockClosingAsync(document, 2));

        var formatted = await provider.FormatLineAsync(
            document, source.IndexOf("then", StringComparison.Ordinal));

        // Cased, spaced, and the indentation the author chose left alone.
        Assert.Contains("        If x = 1 Then", formatted.Text);
    }
}
