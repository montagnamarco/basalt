using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// The outline and the folding of a template.
///
/// A .vbhtml had neither: no structure in the breadcrumb, and no way to
/// collapse a long code block. Both are answerable from the parse tree, so
/// they work even in the standalone server, which has no compiler.
/// </summary>
public sealed class RazorStructureTests
{
    // The outline

    [Fact]
    public void ListsTheDirectives()
    {
        var symbols = VbHtmlDocumentSymbolHandler.SymbolsIn(
            "@ModelType Customer\n@Imports System.Text\n<p>a</p>");

        Assert.Contains(symbols, s => s.Name.Contains("ModelType", StringComparison.Ordinal));
        Assert.Contains(symbols, s => s.Name.Contains("Imports", StringComparison.Ordinal));
    }

    [Fact]
    public void ListsASectionByName()
    {
        var symbols = VbHtmlDocumentSymbolHandler.SymbolsIn(
            "@Section Scripts\n<p>a</p>\n@End Section");

        Assert.Contains(symbols, s => s.Name == "@Section Scripts");
    }

    [Fact]
    public void ListsACodeBlock()
    {
        var symbols = VbHtmlDocumentSymbolHandler.SymbolsIn("@Code\n    Dim x = 1\nEnd Code");

        Assert.Contains(symbols, s => s.Name == "@Code");
    }

    [Fact]
    public void NamesABlockByWhatItOpensWith()
    {
        // Which is what someone scanning an outline is looking for.
        var symbols = VbHtmlDocumentSymbolHandler.SymbolsIn(
            "@If x > 0 Then\n<p>a</p>\n@End If");

        Assert.Contains(symbols, s => s.Name.Contains("If x > 0", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainMarkupHasNoStructure()
    {
        Assert.Empty(VbHtmlDocumentSymbolHandler.SymbolsIn("<p>hello</p>"));
    }

    [Fact]
    public void TheLinesAreZeroBasedForTheProtocol()
    {
        // The parser counts from one and the protocol from zero; getting this
        // wrong puts the outline one line off everywhere.
        var symbols = VbHtmlDocumentSymbolHandler.SymbolsIn("@ModelType Customer");

        Assert.Equal(0, symbols[0].Range.Start.Line);
    }

    // Folding

    [Fact]
    public void ACodeBlockCanBeCollapsed()
    {
        var ranges = VbHtmlFoldingRangeHandler.RangesIn("@Code\n    Dim x = 1\nEnd Code");

        var range = Assert.Single(ranges);

        Assert.Equal(0, range.StartLine);
        Assert.Equal(2, range.EndLine);
    }

    [Fact]
    public void ASectionCanBeCollapsed()
    {
        var ranges = VbHtmlFoldingRangeHandler.RangesIn(
            "@Section Scripts\n<p>a</p>\n@End Section");

        Assert.NotEmpty(ranges);
    }

    [Fact]
    public void ABlockCanBeCollapsed()
    {
        var ranges = VbHtmlFoldingRangeHandler.RangesIn("@If x Then\n<p>a</p>\n@End If");

        Assert.NotEmpty(ranges);
    }

    [Fact]
    public void SomethingThatOpensAndClosesOnOneLineIsNotOffered()
    {
        // An arrow beside every line would be noise.
        var ranges = VbHtmlFoldingRangeHandler.RangesIn("@Code Dim x = 1 End Code");

        Assert.Empty(ranges);
    }

    [Fact]
    public void PlainMarkupFoldsNothing()
    {
        Assert.Empty(VbHtmlFoldingRangeHandler.RangesIn("<p>hello</p>"));
    }

    [Fact]
    public void AnUnclosedBlockIsNotOffered()
    {
        // Its end is unknown, and guessing would collapse the rest of the file.
        Assert.Empty(VbHtmlFoldingRangeHandler.RangesIn("@Code\n    Dim x = 1"));
    }

    // Semantic token lengths

    [Fact]
    public void ColoursAnExplicitExpressionToItsClosingBracket()
    {
        // "@(a * b)" is stored paren-stripped, so measuring the node's text
        // coloured two characters short of what the reader sees.
        var document = new Basalt.Razor.Vb.LanguageServer.OpenDocument(
            "file:///a.vbhtml", "<p>@(a * b)</p>", 1);

        var tokens = Basalt.Razor.Vb.LanguageServer.VbHtmlSemanticTokensHandler
            .Tokens(document);

        var expression = Assert.Single(tokens, t => t.Character == 3);

        // "@(a * b)" is eight characters.
        Assert.Equal(8, expression.Length);
    }

    [Fact]
    public void ColoursARawCallIncludingItsArguments()
    {
        var document = new Basalt.Razor.Vb.LanguageServer.OpenDocument(
            "file:///a.vbhtml", "<p>@Html.Raw(x)</p>", 1);

        var tokens = Basalt.Razor.Vb.LanguageServer.VbHtmlSemanticTokensHandler
            .Tokens(document);

        var expression = Assert.Single(tokens, t => t.Character == 3);

        // "@Html.Raw(x)" is twelve characters.
        Assert.Equal(12, expression.Length);
    }

    [Fact]
    public void ColoursAPlainExpressionExactly()
    {
        var document = new Basalt.Razor.Vb.LanguageServer.OpenDocument(
            "file:///a.vbhtml", "<p>@Model.Name</p>", 1);

        var tokens = Basalt.Razor.Vb.LanguageServer.VbHtmlSemanticTokensHandler
            .Tokens(document);

        var expression = Assert.Single(tokens, t => t.Character == 3);

        // "@Model.Name" is eleven characters.
        Assert.Equal(11, expression.Length);
    }
}
