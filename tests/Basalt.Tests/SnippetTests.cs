using Basalt.Workspace.Snippets;

namespace Basalt.Tests;

/// <summary>Expanding code snippets.</summary>
public class SnippetExpanderTests
{
    private static ExpandedSnippet Expand(string body, string indent = "") =>
        SnippetExpander.Expand(new CodeSnippet("x", "X", body), indent);

    [Fact]
    public void WritesTheBodyWithThePlaceholdersInPlace()
    {
        var expanded = Expand("If $condition$ Then\n\nEnd If");

        Assert.Equal("If condition Then\n\nEnd If", expanded.Text);
    }

    [Fact]
    public void ReportsWhereEachPlaceholderIs()
    {
        // The stops are what tabbing between fields moves through.
        var expanded = Expand("For $i$ As Integer = 0 To $count$");

        Assert.Equal(2, expanded.Stops.Count);
        Assert.Equal("i", expanded.Stops[0].Placeholder);
        Assert.Equal("count", expanded.Stops[1].Placeholder);
    }

    [Fact]
    public void PointsEachStopAtItsTextInTheResult()
    {
        var expanded = Expand("If $condition$ Then");

        var stop = expanded.Stops[0];

        Assert.Equal("condition", expanded.Text.Substring(stop.Start, stop.Length));
    }

    [Fact]
    public void PutsTheCaretWhereTheSnippetSaysTo()
    {
        var expanded = Expand("If $condition$ Then\n    $end$\nEnd If");

        Assert.Equal(expanded.Text.IndexOf("\n    ", StringComparison.Ordinal) + 5, expanded.Caret);
    }

    [Fact]
    public void PutsTheCaretAtTheFirstFieldWhenNoEndIsGiven()
    {
        var expanded = Expand("If $condition$ Then");

        Assert.Equal(expanded.Stops[0].Start, expanded.Caret);
    }

    [Fact]
    public void PutsTheCaretAtTheEndOfASnippetWithNothingToFillIn()
    {
        var expanded = Expand("Console.WriteLine()");

        Assert.Equal(expanded.Text.Length, expanded.Caret);
    }

    [Fact]
    public void KeepsTheIndentationOfTheLineItIsWrittenOn()
    {
        // Without this the user reindents by hand, which is most of the work
        // the snippet was meant to save.
        var expanded = Expand("If a Then\nEnd If", indent: "        ");

        Assert.Equal("If a Then\n        End If", expanded.Text);
    }

    [Fact]
    public void DoesNotIndentTheFirstLine()
    {
        // The first line is already positioned by the text around it.
        var expanded = Expand("If a Then\nEnd If", indent: "    ");

        Assert.StartsWith("If a Then", expanded.Text);
    }

    [Fact]
    public void TreatsADoubledMarkerAsALiteralDollar()
    {
        Assert.Equal("cost: $", Expand("cost: $$").Text);
    }

    [Fact]
    public void LeavesAnUnpairedMarkerAlone()
    {
        Assert.Equal("cost: $5", Expand("cost: $5").Text);
    }

    [Fact]
    public void ReadsWindowsLineEndings()
    {
        var expanded = Expand("If a Then\r\nEnd If");

        Assert.DoesNotContain("\r", expanded.Text);
    }

    [Fact]
    public void ReadsTheIndentationOfALine()
    {
        const string text = "Module A\n        Dim x = 1\n";

        var indent = SnippetExpander.IndentOfLineAt(
            text, text.IndexOf("Dim", StringComparison.Ordinal));

        Assert.Equal("        ", indent);
    }

    [Fact]
    public void ReadsNoIndentationOnALineThatHasNone()
    {
        Assert.Equal("", SnippetExpander.IndentOfLineAt("Module A", 3));
    }
}

/// <summary>The snippets Visual Basic users expect.</summary>
public class VbSnippetsTests
{
    [Theory]
    [InlineData("for")]
    [InlineData("foreach")]
    [InlineData("if")]
    [InlineData("try")]
    [InlineData("class")]
    [InlineData("property")]
    [InlineData("?")]
    public void OffersTheShortcutsVisualBasicShipsWith(string shortcut)
    {
        Assert.NotNull(VbSnippets.ByShortcut(shortcut));
    }

    [Fact]
    public void MatchesAShortcutWhateverItsCase()
    {
        // Visual Basic is case-insensitive; its snippets should be too.
        Assert.NotNull(VbSnippets.ByShortcut("FOR"));
        Assert.NotNull(VbSnippets.ByShortcut("For"));
    }

    [Fact]
    public void HasNoSnippetForAnUnknownShortcut()
    {
        Assert.Null(VbSnippets.ByShortcut("zzz"));
    }

    [Fact]
    public void NarrowsToShortcutsBeginningWithWhatWasTyped()
    {
        var matches = VbSnippets.Matching("fo");

        Assert.Contains(matches, s => s.Shortcut == "for");
        Assert.Contains(matches, s => s.Shortcut == "foreach");
        Assert.DoesNotContain(matches, s => s.Shortcut == "class");
    }

    [Fact]
    public void OffersEverythingBeforeAnythingIsTyped()
    {
        Assert.Equal(VbSnippets.All.Count, VbSnippets.Matching("").Count);
    }

    [Fact]
    public void ClosesEveryBlockItOpens()
    {
        // A snippet that opens a block without closing it would leave the file
        // uncompilable, which is worse than typing it by hand.
        foreach (var snippet in VbSnippets.All)
        {
            var text = SnippetExpander.Expand(snippet).Text;

            if (text.Contains("If ", StringComparison.Ordinal) && text.StartsWith("If", StringComparison.Ordinal))
                Assert.Contains("End If", text);

            if (text.StartsWith("Public Sub", StringComparison.Ordinal))
                Assert.Contains("End Sub", text);

            if (text.StartsWith("Public Class", StringComparison.Ordinal))
                Assert.Contains("End Class", text);
        }
    }

    [Fact]
    public void GivesEverySnippetAShortcutAndATitle()
    {
        Assert.All(VbSnippets.All, snippet =>
        {
            Assert.NotEmpty(snippet.Shortcut);
            Assert.NotEmpty(snippet.Title);
        });
    }

    [Fact]
    public void HasNoDuplicateShortcuts()
    {
        var shortcuts = VbSnippets.All.Select(s => s.Shortcut.ToLowerInvariant()).ToList();

        Assert.Equal(shortcuts.Count, shortcuts.Distinct().Count());
    }
}
