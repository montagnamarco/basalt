using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Basalt.Tests;

/// <summary>
/// Applying only what changed, rather than the whole document.
///
/// The failure mode is silent: if the server's copy drifts from the editor's,
/// every position it is asked about afterwards is wrong, and nothing says so.
/// So each case here checks the resulting text exactly.
/// </summary>
public class IncrementalSyncTests
{
    private static Range At(int startLine, int startChar, int endLine, int endChar) =>
        new(new Position(startLine, startChar), new Position(endLine, endChar));

    [Fact]
    public void InsertsInTheMiddleOfALine()
    {
        Assert.Equal(
            "hello brave world",
            VbHtmlTextDocumentHandler.Apply("hello world", At(0, 6, 0, 6), "brave "));
    }

    [Fact]
    public void ReplacesARunWithinOneLine()
    {
        Assert.Equal(
            "hello there",
            VbHtmlTextDocumentHandler.Apply("hello world", At(0, 6, 0, 11), "there"));
    }

    [Fact]
    public void DeletesWhenTheReplacementIsEmpty()
    {
        Assert.Equal(
            "hello ",
            VbHtmlTextDocumentHandler.Apply("hello world", At(0, 6, 0, 11), ""));
    }

    [Fact]
    public void EditsTheSecondLine()
    {
        Assert.Equal(
            "one\nTWO\nthree",
            VbHtmlTextDocumentHandler.Apply("one\ntwo\nthree", At(1, 0, 1, 3), "TWO"));
    }

    [Fact]
    public void ReplacesAcrossLines()
    {
        Assert.Equal(
            "one\nX",
            VbHtmlTextDocumentHandler.Apply("one\ntwo\nthree", At(1, 0, 2, 5), "X"));
    }

    [Fact]
    public void InsertsANewLine()
    {
        Assert.Equal(
            "one\ntwo",
            VbHtmlTextDocumentHandler.Apply("one", At(0, 3, 0, 3), "\ntwo"));
    }

    [Fact]
    public void HandlesCarriageReturns()
    {
        // A character position stops at the carriage return, not past it:
        // otherwise an edit at the end of a line eats the line break.
        Assert.Equal(
            "one\r\nTWO",
            VbHtmlTextDocumentHandler.Apply("one\r\ntwo", At(1, 0, 1, 3), "TWO"));
    }

    [Fact]
    public void ClampsACharacterPastTheEndOfTheLine()
    {
        // A client and a server can disagree for a moment about how long a
        // line is. Dropping the edit would leave the copies apart for good.
        Assert.Equal(
            "one!\ntwo",
            VbHtmlTextDocumentHandler.Apply("one\ntwo", At(0, 99, 0, 99), "!"));
    }

    [Fact]
    public void ClampsALinePastTheEndOfTheDocument()
    {
        Assert.Equal(
            "one!",
            VbHtmlTextDocumentHandler.Apply("one", At(99, 0, 99, 0), "!"));
    }

    [Fact]
    public void SurvivesAReversedRange()
    {
        Assert.Equal(
            "hello there",
            VbHtmlTextDocumentHandler.Apply("hello world", At(0, 11, 0, 6), "there"));
    }

    [Fact]
    public void AppliesASequenceOfEditsInOrder()
    {
        // What an editor actually sends: several small changes in one message,
        // each described against the text the previous ones produced.
        var text = "<p>@name</p>";

        text = VbHtmlTextDocumentHandler.Apply(text, At(0, 4, 0, 8), "title");
        text = VbHtmlTextDocumentHandler.Apply(text, At(0, 0, 0, 3), "<h1>");

        Assert.Equal("<h1>@title</p>", text);
    }
}
