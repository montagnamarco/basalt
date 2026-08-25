using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// Keeping an opening tag and its closing tag in step while typing.
///
/// The hard part is nesting: a div inside a div means the first "&lt;/div&gt;"
/// after the caret is not necessarily the partner.
/// </summary>
public class LinkedEditingRangeTests
{
    /// <summary>The two linked ranges as (line, startCharacter) pairs.</summary>
    private static (int Line, int Character)[] Pair(string text, string marker)
    {
        var offset = text.IndexOf(marker, StringComparison.Ordinal) + marker.Length;

        var found = VbHtmlLinkedEditingRangeHandler.Pair(text, offset);

        Assert.NotNull(found);

        return [.. found.Ranges.Select(r => (r.Start.Line, r.Start.Character))];
    }

    private static bool HasPair(string text, int offset) =>
        VbHtmlLinkedEditingRangeHandler.Pair(text, offset) is not null;

    [Fact]
    public void LinksAnOpeningTagToItsClosingTag()
    {
        var ranges = Pair("<div>text</div>", "<di");

        Assert.Equal(2, ranges.Length);
        Assert.Equal((0, 1), ranges[0]);
        Assert.Equal((0, 11), ranges[1]);
    }

    [Fact]
    public void LinksAClosingTagBackToItsOpeningTag()
    {
        var ranges = Pair("<div>text</div>", "</di");

        Assert.Equal(2, ranges.Length);
        Assert.Contains((0, 1), ranges);
        Assert.Contains((0, 11), ranges);
    }

    [Fact]
    public void SkipsTheSameElementNestedInside()
    {
        // The first </div> belongs to the inner one, not to the caret's.
        const string text = "<div><div>x</div></div>";

        var ranges = Pair(text, "<di");

        // The outer closing tag is the second </div>, at character 19.
        Assert.Contains((0, 1), ranges);
        Assert.Contains((0, 19), ranges);
    }

    [Fact]
    public void LinksTheInnerPairWhenTheCaretIsOnTheInnerTag()
    {
        const string text = "<div><span>x</span></div>";

        var offset = text.IndexOf("<span", StringComparison.Ordinal) + 3;

        var found = VbHtmlLinkedEditingRangeHandler.Pair(text, offset);

        Assert.NotNull(found);

        var starts = found.Ranges.Select(r => r.Start.Character).ToList();

        Assert.Contains(6, starts);
        Assert.Contains(14, starts);
    }

    [Fact]
    public void SkipsNestingWhenSearchingBackwards()
    {
        // From the outer closing tag: the nearest <div> going back is the
        // inner one, which is already closed.
        const string text = "<div><div>x</div></div>";

        var offset = text.LastIndexOf("</div>", StringComparison.Ordinal) + 3;

        var found = VbHtmlLinkedEditingRangeHandler.Pair(text, offset);

        Assert.NotNull(found);

        var starts = found.Ranges.Select(r => r.Start.Character).ToList();

        // The outer opening at 1, not the inner one at 6.
        Assert.Contains(1, starts);
        Assert.Contains(19, starts);
        Assert.DoesNotContain(6, starts);
    }

    [Fact]
    public void LinksAcrossLines()
    {
        var ranges = Pair("<div>\n  text\n</div>", "<di");

        Assert.Equal((0, 1), ranges[0]);
        Assert.Equal((2, 2), ranges[1]);
    }

    [Fact]
    public void SaysNothingForAnElementThatNeverCloses()
    {
        // <br> has no closing tag to keep in step.
        Assert.False(HasPair("<br>", 2));
    }

    [Fact]
    public void SaysNothingForASelfClosingTag()
    {
        Assert.False(HasPair("<div/>", 2));
    }

    [Fact]
    public void SaysNothingWhenTheTagIsNeverClosed()
    {
        Assert.False(HasPair("<div>text", 2));
    }

    [Fact]
    public void SaysNothingWhenTheCaretIsNotInATagName()
    {
        const string text = "<div>text</div>";

        // In the content.
        Assert.False(HasPair(text, 7));
    }

    [Fact]
    public void SaysNothingInsideAnAttribute()
    {
        const string text = "<div class=\"x\">t</div>";

        Assert.False(HasPair(text, text.IndexOf("class", StringComparison.Ordinal) + 2));
    }

    [Fact]
    public void DoesNotConfuseATagWhoseNameStartsTheSame()
    {
        // <p> and <pre> are different elements.
        const string text = "<p><pre>x</pre></p>";

        var offset = text.IndexOf("<p>", StringComparison.Ordinal) + 2;

        var found = VbHtmlLinkedEditingRangeHandler.Pair(text, offset);

        Assert.NotNull(found);

        var starts = found.Ranges.Select(r => r.Start.Character).ToList();

        // The </p> at the very end, not the </pre>.
        Assert.Contains(1, starts);
        Assert.Contains(17, starts);
    }
}
