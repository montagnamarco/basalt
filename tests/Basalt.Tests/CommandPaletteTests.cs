using Avalonia.Headless.XUnit;
using Basalt.Core.Commands;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// The palette that finds and runs any command.
///
/// It matters most for the commands with no shortcut that sit three menus
/// deep, so what these check is that everything is reachable and that typing
/// narrows sensibly.
/// </summary>
public class CommandPaletteTests
{
    private static CommandPalette Palette() => new(IdeCommands.CreateRegistry());

    [AvaloniaFact]
    public void OffersEveryCommandBeforeAnythingIsTyped()
    {
        var palette = Palette();

        Assert.Equal(IdeCommands.CreateRegistry().All.Count, palette.Matches.Count);
    }

    [AvaloniaFact]
    public void NarrowsToWhatWasTyped()
    {
        var palette = Palette();

        palette.QueryText = "breakpoint";

        Assert.NotEmpty(palette.Matches);
        Assert.All(palette.Matches, c =>
            Assert.Contains("breakpoint", c.SearchText, StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void SelectsTheFirstMatchSoEnterRunsIt()
    {
        // Typing then pressing Enter is the whole point; needing an arrow key
        // in between would defeat it.
        var palette = Palette();

        palette.QueryText = "save";

        Assert.Equal(0, palette.SelectedIndex);
    }

    [AvaloniaFact]
    public void SelectsNothingWhenNothingMatches()
    {
        var palette = Palette();

        palette.QueryText = "zzzzz";

        Assert.Empty(palette.Matches);
        Assert.Equal(-1, palette.SelectedIndex);
    }

    [AvaloniaFact]
    public void ReportsTheCommandThatWasChosen()
    {
        var palette = Palette();

        palette.QueryText = "Toggle Breakpoint";
        palette.AcceptForTests();

        Assert.Equal(IdeCommands.DebugToggleBreakpoint, palette.Chosen?.Id);
    }

    [AvaloniaFact]
    public void ReportsNothingWhenNothingWasChosen()
    {
        var palette = Palette();

        palette.QueryText = "zzzzz";
        palette.AcceptForTests();

        Assert.Null(palette.Chosen);
    }

    [AvaloniaFact]
    public void FindsACommandByItsCategory()
    {
        var palette = Palette();

        palette.QueryText = "git";

        Assert.Contains(palette.Matches, c => c.Id == IdeCommands.GitPush);
    }
}
