using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Tests;

/// <summary>
/// What the language server answers, checked without a server.
///
/// The handlers are thin wrappers over these; running a protocol conversation
/// would test the OmniSharp library rather than this code.
/// </summary>
public class RazorVbLanguageServerTests
{
    private static OpenDocument Open(string text) => new("file:///Views/Test.vbhtml", text, 1);

    /// <summary>Builds a document from text marking the caret with "|".</summary>
    private static (OpenDocument Document, int Offset) At(string text)
    {
        var caret = text.IndexOf('|');

        Assert.True(caret >= 0, "The test view must mark the caret with |.");

        return (Open(text.Remove(caret, 1)), caret);
    }

    // Documents

    [Fact]
    public void KeepsTheEditorsCopyOfAView()
    {
        var store = new DocumentStore();

        store.Update("file:///a.vbhtml", "<p>hello</p>", 1);

        Assert.Equal("<p>hello</p>", store.Get("file:///a.vbhtml")!.Text);
    }

    [Fact]
    public void ForgetsAViewOnceItIsClosed()
    {
        var store = new DocumentStore();

        store.Update("file:///a.vbhtml", "<p>a</p>", 1);
        store.Remove("file:///a.vbhtml");

        Assert.Null(store.Get("file:///a.vbhtml"));
    }

    [Fact]
    public void ParsesAViewOnceRatherThanPerRequest()
    {
        // Completion, diagnostics and colouring all want the tree.
        var document = Open("@ModelType String\n<p>@Model</p>");

        Assert.Same(document.Parsed, document.Parsed);
    }

    // Diagnostics

    [Fact]
    public void ReportsNothingForAViewThatIsRight()
    {
        var document = Open("@ModelType String\n<p>@Model</p>");

        Assert.Empty(VbHtmlTextDocumentHandler.Describe(document.Parsed));
    }

    [Fact]
    public void CountsLinesFromZeroForTheEditor()
    {
        // The parser counts from one, as a compiler does; the protocol counts
        // from zero. Getting this wrong puts every error on the wrong line.
        var parsed = Basalt.Razor.Vb.VbHtmlParser.Parse("@If x Then\n<p>a</p>");

        Assert.NotEmpty(parsed.Diagnostics);

        var reported = VbHtmlTextDocumentHandler.Describe(parsed).First();
        var parserLine = parsed.Diagnostics[0].Line;

        Assert.Equal(parserLine - 1, reported.Range.Start.Line);
    }

    // Completion

    [Fact]
    public void OffersDirectivesAfterAnAtAtTheStartOfALine()
    {
        var (document, offset) = At("@|");

        var items = VbHtmlCompletionHandler.Suggest(document, offset);

        Assert.Contains(items, i => i.Label == "ModelType");
        Assert.Contains(items, i => i.Label == "For Each");
    }

    [Fact]
    public void OffersTagsInsideMarkup()
    {
        // After the angle bracket, which is where an element can go. It used
        // to offer the tag list anywhere at all, including into running text.
        var (document, offset) = At("<p>text</p>\n<|");

        var items = VbHtmlCompletionHandler.Suggest(document, offset);

        Assert.Contains(items, i => i.Label == "div");
    }

    [Fact]
    public void OffersNoTagsWhereAnElementCannotGo()
    {
        var (document, offset) = At("<p>text</p>\n|");

        Assert.Empty(VbHtmlCompletionHandler.Suggest(document, offset));
    }

    [Fact]
    public void WithoutACompilationItInventsNoModelMembers()
    {
        // Suggest is the fallback: what the server answers while the solution
        // is still loading, or when there is none. With one it answers from
        // Roslyn instead — see LanguageServerCompilationTests.
        //
        // It used to offer ToString, GetType and Equals as though they were
        // the model's own. They were not, and the old test looked for
        // ToString, which the stub provided: it could not tell a stub from a
        // real implementation, the exact blind spot this replaces.
        var (document, offset) = At("@ModelType Customer\n<p>@Model.|</p>");

        var items = VbHtmlCompletionHandler.Suggest(document, offset);

        Assert.DoesNotContain(items, i => i.Label == "ToString");
        Assert.Contains(items, i => i.Label.Contains("Customer", StringComparison.Ordinal));

        // It says what is missing rather than guessing.
        Assert.Contains(items, i =>
            (i.Detail ?? "").Contains("loading", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaysSoWhenTheViewDeclaresNoModel()
    {
        // Better than offering members of a type nobody named.
        var (document, offset) = At("<p>@Model.|</p>");

        var items = VbHtmlCompletionHandler.Suggest(document, offset);

        Assert.Contains(items, i => i.Label.Contains("no model"));
    }

    [Theory]
    [InlineData("abc", 0, 0, 0)]
    [InlineData("abc", 0, 2, 2)]
    [InlineData("ab\ncd", 1, 1, 4)]
    [InlineData("ab\ncd", 9, 0, 5)]
    public void TurnsALineAndCharacterIntoAnOffset(
        string text, int line, int character, int expected)
    {
        // A position past the end is clamped: an editor can send one while a
        // change is in flight.
        Assert.Equal(expected, VbHtmlCompletionHandler.OffsetOf(text, new Position(line, character)));
    }

    // Colouring

    [Fact]
    public void ColoursTheDirectivesAndExpressions()
    {
        var document = Open("@ModelType String\n<p>@Model</p>");

        var tokens = VbHtmlSemanticTokensHandler.Tokens(document);

        Assert.Contains(tokens, t => t.Type == SemanticTokenType.Macro);
        Assert.Contains(tokens, t => t.Type == SemanticTokenType.Variable);
    }

    [Fact]
    public void LeavesPlainMarkupToTheGrammar()
    {
        // A TextMate grammar already colours HTML; saying it again would only
        // be a chance to disagree.
        var document = Open("<p>just markup</p>");

        Assert.Empty(VbHtmlSemanticTokensHandler.Tokens(document));
    }

    [Fact]
    public void ReportsTokensInOrder()
    {
        // The protocol encodes each token as a delta from the one before, so
        // an out-of-order token corrupts everything after it.
        var document = Open("@ModelType String\n@Code\n    Dim a = 1\nEnd Code\n<p>@a</p>");

        var tokens = VbHtmlSemanticTokensHandler.Tokens(document);

        var lines = tokens.Select(t => (t.Line, t.Character)).ToList();

        Assert.Equal(lines.OrderBy(p => p.Line).ThenBy(p => p.Character), lines);
    }

    [Theory]
    [InlineData("abc", 1, 0, 1)]
    [InlineData("ab\ncd", 3, 1, 0)]
    [InlineData("ab\ncd", 4, 1, 1)]
    public void TurnsAnOffsetIntoALineAndCharacter(
        string text, int offset, int line, int character)
    {
        var (readLine, readCharacter) =
            VbHtmlSemanticTokensHandler.LineAndCharacterOf(text, offset);

        Assert.Equal(line, readLine);
        Assert.Equal(character, readCharacter);
    }

    // Navigation

    [Fact]
    public void FindsWhereANameIsDeclared()
    {
        const string view = """
            @Code
                Dim total = 1
            End Code
            <p>@total</p>
            """;

        var position = VbHtmlDefinitionHandler.FindDeclaration(Open(view), "total");

        Assert.NotNull(position);
    }

    [Fact]
    public void DoesNotMatchANameInsideALongerOne()
    {
        // "total" must not be found inside "totalCount".
        const string view = """
            @Code
                Dim totalCount = 1
            End Code
            """;

        Assert.Null(VbHtmlDefinitionHandler.FindDeclaration(Open(view), "total"));
    }

    [Fact]
    public void FindsOnlyDeclarationsNotEveryMention()
    {
        const string view = """
            @Code
                total = total + 1
            End Code
            """;

        Assert.Null(VbHtmlDefinitionHandler.FindDeclaration(Open(view), "total"));
    }

    [Fact]
    public void FindsTheModelTypeDeclaration()
    {
        var view = Open("@ModelType Customer\n<p>@Model.Name</p>");

        Assert.NotNull(VbHtmlDefinitionHandler.FindDeclaration(view, "Customer"));
    }

    // Signature help

    [Fact]
    public void DescribesACallItKnows()
    {
        const string expression = "@Html.Raw(";

        var help = VbHtmlSignatureHelpHandler.Describe(expression, expression.Length);

        Assert.NotNull(help);
        Assert.Contains("Html.Raw", help!.Signatures.First().Label);
    }

    [Fact]
    public void FollowsTheCaretToTheNextArgument()
    {
        const string expression = "@String.Format(\"{0}\", ";

        var help = VbHtmlSignatureHelpHandler.Describe(expression, expression.Length);

        Assert.Equal(1, help!.ActiveParameter);
    }

    [Fact]
    public void SaysNothingAboutACallItCannotCheck()
    {
        // Naming a signature this server does not know would be worse than
        // naming none.
        Assert.Null(VbHtmlSignatureHelpHandler.Describe("@Something.Else(", 16));
    }

    [Fact]
    public void SaysNothingOutsideACall()
    {
        Assert.Null(VbHtmlSignatureHelpHandler.Describe("<p>text</p>", 5));
    }

    [Fact]
    public void SaysNothingAfterAClosedCall()
    {
        const string expression = "@Html.Raw(x) ";

        Assert.Null(VbHtmlSignatureHelpHandler.Describe(expression, expression.Length));
    }
}
