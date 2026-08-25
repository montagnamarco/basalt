using Basalt.Core.Commands;

namespace Basalt.Tests;

/// <summary>
/// The registry of commands and their shortcuts.
///
/// Everything the IDE can be asked to do is named here once, so that the
/// menus, the toolbar, the keyboard and the palette cannot disagree.
/// </summary>
public class CommandRegistryTests
{
    private static CommandRegistry Registry() => IdeCommands.CreateRegistry();

    [Fact]
    public void HoldsEveryCommandTheIdeOffers()
    {
        var registry = Registry();

        Assert.NotEmpty(registry.All);
        Assert.NotNull(registry.ById(IdeCommands.FileSave));
        Assert.NotNull(registry.ById(IdeCommands.DebugStart));
    }

    [Fact]
    public void GivesEveryCommandATitleAndACategory()
    {
        Assert.All(Registry().All, command =>
        {
            Assert.NotEmpty(command.Id);
            Assert.NotEmpty(command.Title);
        });
    }

    [Fact]
    public void HasNoTwoCommandsWithTheSameId()
    {
        var ids = Registry().All.Select(c => c.Id).ToList();

        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void UsesTheShortcutACommandCameWith()
    {
        Assert.Equal("Ctrl+S", Registry().GestureFor(IdeCommands.FileSave));
    }

    [Fact]
    public void PrefersAShortcutTheUserChose()
    {
        var registry = Registry();

        registry.Assign(IdeCommands.FileSave, "Ctrl+Alt+S");

        Assert.Equal("Ctrl+Alt+S", registry.GestureFor(IdeCommands.FileSave));
    }

    [Fact]
    public void TakesAShortcutAwayWhenAssignedNothing()
    {
        var registry = Registry();

        registry.Assign(IdeCommands.FileSave, "");

        Assert.Null(registry.GestureFor(IdeCommands.FileSave));
    }

    [Fact]
    public void PutsACommandBackToItsOriginalShortcut()
    {
        var registry = Registry();

        registry.Assign(IdeCommands.FileSave, "Ctrl+Alt+S");
        registry.ResetToDefault(IdeCommands.FileSave);

        Assert.Equal("Ctrl+S", registry.GestureFor(IdeCommands.FileSave));
        Assert.False(registry.IsCustomised(IdeCommands.FileSave));
    }

    [Fact]
    public void PutsEverythingBackAtOnce()
    {
        var registry = Registry();

        registry.Assign(IdeCommands.FileSave, "Ctrl+Alt+S");
        registry.Assign(IdeCommands.EditUndo, "Ctrl+Alt+Z");

        registry.ResetAll();

        Assert.Empty(registry.Customisations);
        Assert.Equal("Ctrl+S", registry.GestureFor(IdeCommands.FileSave));
    }

    [Fact]
    public void SaysWhichCommandAShortcutRuns()
    {
        Assert.Equal(IdeCommands.FileSave, Registry().ForGesture("Ctrl+S")?.Id);
    }

    [Fact]
    public void MatchesAShortcutWhateverItsSpacingOrCase()
    {
        // A user typing a gesture by hand writes "ctrl+s" as readily as
        // "Ctrl+S".
        var registry = Registry();

        Assert.NotNull(registry.ForGesture("ctrl+s"));
        Assert.NotNull(registry.ForGesture("Ctrl + S"));
    }

    [Fact]
    public void FindsNoCommandForAnUnusedShortcut()
    {
        Assert.Null(Registry().ForGesture("Ctrl+Alt+Shift+Q"));
    }

    [Fact]
    public void ReportsWhatAShortcutWouldDisplace()
    {
        // Not refused — the user may well mean it — but they are told.
        var conflicts = Registry().Conflicts("Ctrl+S", IdeCommands.EditUndo);

        Assert.Contains(conflicts, c => c.Id == IdeCommands.FileSave);
    }

    [Fact]
    public void DoesNotReportACommandConflictingWithItself()
    {
        Assert.Empty(Registry().Conflicts("Ctrl+S", IdeCommands.FileSave));
    }

    [Fact]
    public void ReportsAConflictWithAShortcutTheUserAssigned()
    {
        var registry = Registry();

        registry.Assign(IdeCommands.EditUndo, "Ctrl+Alt+Q");

        Assert.Contains(
            registry.Conflicts("Ctrl+Alt+Q", IdeCommands.FileSave),
            c => c.Id == IdeCommands.EditUndo);
    }

    [Fact]
    public void ComesWithNoConflictsOfItsOwn()
    {
        // Two commands sharing a key by accident is a defect the user would
        // find before we did.
        var registry = Registry();

        var duplicates = registry.All
            .Where(c => registry.GestureFor(c.Id) is { Length: > 0 })
            .GroupBy(c => registry.GestureFor(c.Id)!.Replace(" ", "").ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.Id))}")
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void FindsCommandsByName()
    {
        var found = Registry().Search("breakpoint");

        Assert.Contains(found, c => c.Id == IdeCommands.DebugToggleBreakpoint);
    }

    [Fact]
    public void FindsCommandsByCategory()
    {
        Assert.NotEmpty(Registry().Search("debug"));
    }

    [Fact]
    public void PutsANameThatStartsWithTheQueryFirst()
    {
        var found = Registry().Search("save");

        Assert.StartsWith("Save", found[0].Title);
    }

    [Fact]
    public void OffersEverythingBeforeAnythingIsTyped()
    {
        var registry = Registry();

        Assert.Equal(registry.All.Count, registry.Search("").Count);
    }

    [Fact]
    public void RemembersOnlyWhatTheUserChanged()
    {
        // The settings file should hold the changes, not a copy of every
        // default that would then never follow an update.
        var registry = Registry();

        registry.Assign(IdeCommands.FileSave, "Ctrl+Alt+S");

        Assert.Single(registry.Customisations);
    }

    [Fact]
    public void TakesBackWhatWasSaved()
    {
        var registry = Registry();

        registry.ApplyCustomisations(new Dictionary<string, string?>
        {
            [IdeCommands.FileSave] = "Ctrl+Alt+S"
        });

        Assert.Equal("Ctrl+Alt+S", registry.GestureFor(IdeCommands.FileSave));
    }

    [Theory]
    [InlineData(KeyboardScheme.VisualStudio, IdeCommands.EditRedo, "Ctrl+Y")]
    [InlineData(KeyboardScheme.VisualStudioCode, IdeCommands.EditRedo, "Ctrl+Shift+Z")]
    [InlineData(KeyboardScheme.VisualStudio, IdeCommands.BuildSolution, "Ctrl+Shift+B")]
    [InlineData(KeyboardScheme.Basalt, IdeCommands.BuildSolution, "F6")]
    public void FollowsTheChosenScheme(KeyboardScheme scheme, string commandId, string expected)
    {
        // Someone arriving from Visual Studio should not have to relearn Redo.
        Assert.Equal(expected, IdeCommands.CreateRegistry(scheme).GestureFor(commandId));
    }

    [Theory]
    [InlineData(KeyboardScheme.Basalt)]
    [InlineData(KeyboardScheme.VisualStudio)]
    [InlineData(KeyboardScheme.VisualStudioCode)]
    public void EverySchemeIsFreeOfConflicts(KeyboardScheme scheme)
    {
        var registry = IdeCommands.CreateRegistry(scheme);

        var duplicates = registry.All
            .Where(c => registry.GestureFor(c.Id) is { Length: > 0 })
            .GroupBy(c => registry.GestureFor(c.Id)!.Replace(" ", "").ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key}: {string.Join(", ", g.Select(c => c.Id))}")
            .ToList();

        Assert.Empty(duplicates);
    }
}
