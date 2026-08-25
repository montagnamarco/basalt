using Basalt.Workspace.Completion;

namespace Basalt.Tests;

/// <summary>
/// Closing brackets and quotes as they are opened.
///
/// The rules exist to stay out of the way, so most of these check that
/// nothing happens where a closing character would be wrong.
/// </summary>
public class BracketCompletionTests
{
    private static BracketAction Act(string text, int caret, char typed) =>
        BracketCompletion.ForTypedCharacter(text, caret, typed);

    [Theory]
    [InlineData('(', ")")]
    [InlineData('[', "]")]
    [InlineData('{', "}")]
    public void ClosesABracketAsItIsOpened(char typed, string expected)
    {
        Assert.Equal(expected, Act("", 0, typed).Insert);
    }

    [Fact]
    public void LeavesTheCaretBetweenThePair()
    {
        Assert.Equal(-1, Act("", 0, '(').CaretOffset);
    }

    [Fact]
    public void ClosesAQuote()
    {
        Assert.Equal("\"", Act("Dim s = ", 8, '"').Insert);
    }

    [Fact]
    public void StepsOverAClosingBracketAlreadyThere()
    {
        // Typing ")" where one sits is the user finishing the call, not asking
        // for a second bracket.
        Assert.Equal(1, Act("Greet()", 6, ')').SkipLength);
    }

    [Fact]
    public void StepsOverAClosingQuoteAlreadyThere()
    {
        Assert.Equal(1, Act("\"\"", 1, '"').SkipLength);
    }

    [Fact]
    public void DoesNotCloseBeforeAWord()
    {
        // "(" before "abc" is usually wrapping what follows.
        Assert.True(Act("abc", 0, '(').IsNone);
    }

    [Fact]
    public void DoesNotTreatAnApostropheAsAnOpeningQuote()
    {
        // A quote right after a word is closing one or is an apostrophe.
        Assert.True(Act("name", 4, '"').IsNone);
    }

    [Fact]
    public void DoesNotOpenAQuoteInsideAString()
    {
        Assert.True(Act("Dim s = \"hello ", 15, '"').IsNone);
    }

    [Fact]
    public void OpensAQuoteAfterAClosedString()
    {
        // The string on this line is finished, so this quote starts a new one.
        Assert.Equal("\"", Act("Dim s = \"a\" & ", 14, '"').Insert);
    }

    [Fact]
    public void CountsAnEscapedQuoteAsOneCharacter()
    {
        // "" inside a VB literal is an escaped quote, so the string is still open.
        Assert.True(Act("Dim s = \"say \"\" ", 16, '"').IsNone);
    }

    [Fact]
    public void JudgesEachLineOnItsOwn()
    {
        // A Visual Basic string cannot span lines, so an unclosed quote above
        // does not leave the line below inside a string.
        Assert.Equal("\"", Act("Dim a = \"open\nDim b = ", 22, '"').Insert);
    }

    [Fact]
    public void DoesNothingForAnOrdinaryCharacter()
    {
        Assert.True(Act("", 0, 'x').IsNone);
    }

    [Fact]
    public void DeletesThePairWhenTheOpeningIsBackspaced()
    {
        Assert.True(BracketCompletion.ShouldDeletePair("()", 1));
    }

    [Fact]
    public void DeletesAQuotePair()
    {
        Assert.True(BracketCompletion.ShouldDeletePair("\"\"", 1));
    }

    [Fact]
    public void LeavesAClosingBracketThatIsNotAdjacent()
    {
        // Only the pair the completion itself made should vanish together.
        Assert.False(BracketCompletion.ShouldDeletePair("(a)", 2));
    }

    [Fact]
    public void CopesWithACaretOutsideTheText()
    {
        Assert.True(Act("abc", 99, '(').IsNone);
        Assert.False(BracketCompletion.ShouldDeletePair("abc", 99));
    }
}
