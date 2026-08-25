using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// Lighting up the other places a name appears.
///
/// Answerable from the parse tree alone, which is why the standalone server
/// can do it when it cannot do completion. The part that needs care is
/// staying out of the markup: the same letters in a paragraph are prose.
/// </summary>
public class VbHtmlDocumentHighlightTests
{
    private static int Caret(string text, string fragment, int offset = 1) =>
        text.IndexOf(fragment, StringComparison.Ordinal) + offset;

    [Fact]
    public void HighlightsTheOtherUsesOfANameInCode()
    {
        const string view = """
            @Code
                Dim total = 1
            End Code
            <p>@total</p>
            """;

        var found = VbHtmlDocumentHighlightHandler.Highlights(
            view, Caret(view, "@total"));

        Assert.NotNull(found);

        // The declaration and the use.
        Assert.Equal(2, found.Count());
    }

    [Fact]
    public void LeavesTheSameWordInTheMarkupAlone()
    {
        // "total" appears as prose too. Highlighting it would be wrong.
        const string view = """
            @Code
                Dim total = 1
            End Code
            <p>the total is @total</p>
            """;

        var found = VbHtmlDocumentHighlightHandler.Highlights(
            view, Caret(view, "@total"));

        Assert.NotNull(found);
        Assert.Equal(2, found.Count());
    }

    [Fact]
    public void SaysNothingWhenTheCaretIsInTheMarkup()
    {
        const string view = """
            @Code
                Dim total = 1
            End Code
            <p>the total is @total</p>
            """;

        // On the word "total" in the prose, not the expression.
        var at = view.IndexOf("the total", StringComparison.Ordinal) + 5;

        Assert.Null(VbHtmlDocumentHighlightHandler.Highlights(view, at));
    }

    [Fact]
    public void DoesNotMatchALongerNameThatContainsIt()
    {
        const string view = """
            @Code
                Dim total = 1
                Dim totalCount = 2
            End Code
            <p>@total</p>
            """;

        var found = VbHtmlDocumentHighlightHandler.Highlights(
            view, Caret(view, "@total"));

        Assert.NotNull(found);

        // "totalCount" is a different name.
        Assert.Equal(2, found.Count());
    }

    [Fact]
    public void SaysNothingWhereThereIsNoName()
    {
        Assert.Null(VbHtmlDocumentHighlightHandler.Highlights("<p>hello</p>", 0));
    }
}
