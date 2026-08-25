using Basalt.Core.Model;
using Basalt.Workspace.Completion;

namespace Basalt.Tests;

/// <summary>Narrowing and ordering completions as the user types.</summary>
public class CompletionMatcherTests
{
    private static CompletionItem Item(string name) =>
        new(name, name, CompletionKind.Method);

    private static IReadOnlyList<string> Filter(string query, params string[] names) =>
        [.. CompletionMatcher
            .Filter([.. names.Select(Item)], query)
            .Select(m => m.Item.DisplayText)];

    [Fact]
    public void OffersEverythingBeforeAnythingIsTyped()
    {
        Assert.Equal(3, Filter("", "One", "Two", "Three").Count);
    }

    [Fact]
    public void SortsAlphabeticallyWhenNothingIsTyped()
    {
        Assert.Equal(["Alpha", "Beta", "Gamma"], Filter("", "Gamma", "Alpha", "Beta"));
    }

    [Fact]
    public void PutsAnExactMatchFirst()
    {
        Assert.Equal("Write", Filter("Write", "WriteLine", "Write", "WriteAll")[0]);
    }

    [Fact]
    public void PutsAPrefixMatchAboveALooserOne()
    {
        // "Wr" starts "WriteLine"; it only appears scattered in "WithRetry".
        var results = Filter("Wr", "WithRetry", "WriteLine");

        Assert.Equal("WriteLine", results[0]);
    }

    [Fact]
    public void FindsANameByItsCapitals()
    {
        // Typing the initials is how a reader picks a long name from a list.
        Assert.Contains("WriteLine", Filter("wl", "WriteLine", "Wait"));
    }

    [Fact]
    public void PutsAnInitialsMatchAboveAScatteredOne()
    {
        var results = Filter("wl", "WriteLine", "WaitForListener");

        Assert.Equal("WriteLine", results[0]);
    }

    [Fact]
    public void MatchesRegardlessOfCase()
    {
        // Visual Basic is case-insensitive, so completion must be too.
        Assert.Contains("WriteLine", Filter("writeline", "WriteLine"));
        Assert.Contains("WriteLine", Filter("WRITELINE", "WriteLine"));
    }

    [Fact]
    public void PrefersTheCandidateThatAlsoAgreesOnCase()
    {
        var results = Filter("Wr", "wrapper", "Written");

        Assert.Equal("Written", results[0]);
    }

    [Fact]
    public void LeavesOutWhatDoesNotMatch()
    {
        Assert.Empty(Filter("zzz", "WriteLine", "Read"));
    }

    [Fact]
    public void LeavesOutACandidateShorterThanTheQuery()
    {
        Assert.Empty(Filter("WriteLine", "Wr"));
    }

    [Fact]
    public void MatchesLettersInOrderWithGapsBetween()
    {
        Assert.Contains("WriteAllText", Filter("wat", "WriteAllText"));
    }

    [Fact]
    public void PrefersTheShorterOfTwoEquallyGoodMatches()
    {
        // Both start with "Wr"; the shorter is likelier to be what was meant.
        var results = Filter("Wr", "WriteLineIndentedFormatted", "Write");

        Assert.Equal("Write", results[0]);
    }

    [Fact]
    public void FindsANameByAWordAfterAnUnderscore()
    {
        Assert.Contains("total_count", Filter("tc", "total_count", "tally"));
    }

    [Fact]
    public void ReportsWhichLettersMatched()
    {
        // The letters are what an editor underlines in the list.
        var match = CompletionMatcher.Match(Item("WriteLine"), "Wr");

        Assert.Equal([0, 1], match.MatchedIndices);
    }

    [Fact]
    public void ReportsNoMatchAsAZeroScore()
    {
        Assert.False(CompletionMatcher.Match(Item("Read"), "zzz").IsMatch);
    }

    [Fact]
    public void FiltersALargeListWithoutTrouble()
    {
        var many = Enumerable.Range(0, 5000).Select(i => Item($"Member{i}")).ToList();

        var results = CompletionMatcher.Filter(many, "member42");

        Assert.NotEmpty(results);
        Assert.Equal("Member42", results[0].Item.DisplayText);
    }
}
