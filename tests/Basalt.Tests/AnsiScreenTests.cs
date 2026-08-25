using Basalt.Workspace.Terminal;

namespace Basalt.Tests;

/// <summary>
/// A pseudo-terminal emits escape sequences; rendering them literally would
/// fill the panel with noise like "ESC[0m" instead of the program's output.
/// </summary>
public class AnsiScreenTests
{
    /// <summary>ESC, written as an escape so the source stays readable.</summary>
    private const string Esc = "\u001b";

    [Fact]
    public void KeepsPlainTextUnchanged()
    {
        var screen = new AnsiScreen();
        screen.Append("hello world");

        Assert.Equal("hello world", screen.Text);
    }

    [Fact]
    public void StripsColourSequences()
    {
        var screen = new AnsiScreen();
        screen.Append($"{Esc}[31merror{Esc}[0m: something failed");

        Assert.Equal("error: something failed", screen.Text);
    }

    [Fact]
    public void SplitsOnNewlines()
    {
        var screen = new AnsiScreen();
        screen.Append("first\nsecond\nthird");

        Assert.Equal("first\nsecond\nthird", screen.Text);
    }

    [Fact]
    public void TreatsCarriageReturnAsRewritingTheLine()
    {
        // How progress indicators update in place.
        var screen = new AnsiScreen();
        screen.Append("Downloading 10%\rDownloading 99%");

        Assert.Equal("Downloading 99%", screen.Text);
    }

    [Fact]
    public void HandlesCarriageReturnFollowedByShorterText()
    {
        var screen = new AnsiScreen();
        screen.Append("aaaaaaaaaa\rbbb");

        // The tail of the previous text survives, exactly as on a real terminal.
        Assert.Equal("bbbaaaaaaa", screen.Text);
    }

    [Fact]
    public void AppliesBackspace()
    {
        var screen = new AnsiScreen();
        screen.Append("abcX\b!");

        Assert.Equal("abc!", screen.Text);
    }

    [Fact]
    public void ExpandsTabsToEightColumnStops()
    {
        var screen = new AnsiScreen();
        screen.Append("ab\tc");

        Assert.Equal("ab      c", screen.Text);
    }

    [Fact]
    public void ClearsTheScreenOnEraseDisplay()
    {
        var screen = new AnsiScreen();
        screen.Append("old content\n");
        screen.Append($"{Esc}[2Jfresh");

        Assert.Equal("fresh", screen.Text);
    }

    [Fact]
    public void ErasesFromTheCursorToTheEndOfTheLine()
    {
        var screen = new AnsiScreen();
        screen.Append("keep this remove that\r");
        screen.Append($"keep this {Esc}[K");

        Assert.Equal("keep this ", screen.Text);
    }

    [Fact]
    public void DiscardsWindowTitleSequences()
    {
        // Shells set the window title on every prompt.
        var screen = new AnsiScreen();
        screen.Append($"{Esc}]0;user@host: ~\a$ ls");

        Assert.Equal("$ ls", screen.Text);
    }

    [Fact]
    public void DropsCursorMovementWithoutLeavingResidue()
    {
        var screen = new AnsiScreen();
        screen.Append($"{Esc}[2;5Htext{Esc}[1;1H");

        Assert.Equal("text", screen.Text);
    }

    [Fact]
    public void LimitsScrollbackToTheConfiguredNumberOfLines()
    {
        var screen = new AnsiScreen { Scrollback = 10 };
        for (var i = 0; i < 50; i++) screen.Append($"line {i}\n");

        var lines = screen.Text.Split('\n');
        Assert.True(lines.Length <= 10, $"expected at most 10 lines, found {lines.Length}");
        Assert.Contains("line 49", screen.Text);
        Assert.DoesNotContain("line 0\n", screen.Text);
    }

    [Fact]
    public void HandlesSequencesSplitAcrossAppends()
    {
        // Reads from a terminal cut escape sequences at arbitrary points.
        var screen = new AnsiScreen();
        screen.Append($"value: {Esc}[32m");
        screen.Append($"ok{Esc}[0m");

        Assert.Equal("value: ok", screen.Text);
    }
}
