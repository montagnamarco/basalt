using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Commands;
using Basalt.Core.Settings;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Choosing which buttons the toolbar shows.
///
/// The case that matters is an empty configuration meaning "the ones it comes
/// with": storing the default list instead would freeze the toolbar as it was
/// the day the user first opened the settings.
/// </summary>
public sealed class ToolbarConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-toolbarcfg", Guid.NewGuid().ToString("N"));

    private SettingsStore Store => new(Path.Combine(_root, "settings.json"));

    public ToolbarConfigurationTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ComesWithNothingChosen()
    {
        // Which is what makes the toolbar show its defaults.
        Assert.Empty(new IdeSettings().Toolbar.Buttons);
        Assert.True(new IdeSettings().Toolbar.ShowChoosers);
    }

    [Fact]
    public void KeepsWhatWasChosenAcrossASaveAndLoad()
    {
        var store = Store;
        var settings = store.Load();

        settings.Toolbar.Buttons.Add("file.save");
        settings.Toolbar.Buttons.Add("build.solution");
        settings.Toolbar.ShowChoosers = false;

        store.Save(settings);

        var read = store.Load();

        Assert.Equal(["file.save", "build.solution"], read.Toolbar.Buttons);
        Assert.False(read.Toolbar.ShowChoosers);
    }

    [Fact]
    public void CountsAsChangedOnceAButtonIsChosen()
    {
        // So the settings window marks the page and offers to put it back.
        var settings = new IdeSettings();

        Assert.False(SettingsComparison.HasChangesIn(settings, "Toolbar"));

        settings.Toolbar.Buttons.Add("file.save");

        Assert.True(SettingsComparison.HasChangesIn(settings, "Toolbar"));
    }

    [AvaloniaFact]
    public void OffersAToolbarPage()
    {
        var window = new SettingsWindow(Store, null);

        Assert.Contains(window.VisiblePagesForTests(),
            p => p.Name.StartsWith("Toolbar", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void FindsTheToolbarPageBySearchingForAButton()
    {
        var window = new SettingsWindow(Store, null);

        window.SearchForTests("button");

        Assert.Contains(window.VisiblePagesForTests(),
            p => p.Name.StartsWith("Toolbar", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void TicksEveryButtonWhenNothingHasBeenChosen()
    {
        // Nothing chosen means the toolbar shows them all, so the page has to
        // show them all ticked rather than none.
        var window = new SettingsWindow(Store, null);

        window.SelectCategoryForTests(
            window.Categories.ToList().FindIndex(c => c.StartsWith("Toolbar", StringComparison.Ordinal)));

        var boxes = window.CurrentPage!.GetVisualDescendants().OfType<CheckBox>().ToList();

        Assert.NotEmpty(boxes);
        Assert.All(boxes.Take(boxes.Count - 1), box => Assert.True(box.IsChecked));
    }

    [Fact]
    public void NamesEveryButtonItOffers()
    {
        // A candidate with no title would be an unlabelled tick box.
        Assert.All(SettingsWindow.ToolbarCandidatesForTests(), candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.Id));
            Assert.False(string.IsNullOrWhiteSpace(candidate.Title));
        });
    }

    [Fact]
    public void OffersOnlyCommandsTheRegistryKnows()
    {
        // A candidate whose id is not a command could never be shown.
        var registry = Basalt.Core.Commands.IdeCommands.CreateRegistry(
            Basalt.Core.Commands.KeyboardScheme.Basalt);

        Assert.All(SettingsWindow.ToolbarCandidatesForTests(),
            candidate => Assert.NotNull(registry.ById(candidate.Id)));
    }

    [AvaloniaFact]
    public void MovesAButtonAlongTheToolbar()
    {
        var store = Store;
        var window = new SettingsWindow(store, null);

        var candidates = SettingsWindow.ToolbarCandidatesForTests();
        var second = candidates[1].Id;

        window.MoveToolbarButtonForTests(second, -1);

        // Moving one writes the list out in full, since an empty list means
        // the defaults rather than nothing.
        var order = window.Settings.Toolbar.Buttons;

        Assert.Equal(candidates.Count, order.Count);
        Assert.Equal(second, order[0]);
        Assert.Equal(candidates[0].Id, order[1]);
    }

    [AvaloniaFact]
    public void LeavesTheFirstButtonWhereItIs()
    {
        // Moving the first one up would take it off the list.
        var window = new SettingsWindow(Store, null);

        var first = SettingsWindow.ToolbarCandidatesForTests()[0].Id;

        window.MoveToolbarButtonForTests(first, -1);

        var order = window.Settings.Toolbar.Buttons;

        Assert.True(order.Count == 0 || order[0] == first);
    }

    [AvaloniaFact]
    public void LeavesTheLastButtonWhereItIs()
    {
        var window = new SettingsWindow(Store, null);

        var candidates = SettingsWindow.ToolbarCandidatesForTests();
        var last = candidates[^1].Id;

        window.MoveToolbarButtonForTests(last, 1);

        var order = window.Settings.Toolbar.Buttons;

        Assert.True(order.Count == 0 || order[^1] == last);
    }

    [AvaloniaFact]
    public void KeepsTheOrderAcrossASaveAndLoad()
    {
        var store = Store;
        var window = new SettingsWindow(store, null);

        var second = SettingsWindow.ToolbarCandidatesForTests()[1].Id;

        window.MoveToolbarButtonForTests(second, -1);

        Assert.Equal(second, store.Load().Toolbar.Buttons[0]);
    }

    [Fact]
    public void GroupsTheButtonsByTheKindOfWork()
    {
        var groups = SettingsWindow.ToolbarGroups.Select(g => g.Name).ToList();

        Assert.Equal(
            ["File", "Edit", "Navigate", "Build", "Debug", "Test", "Git", "Tools"],
            groups);
    }

    [Fact]
    public void PutsEveryButtonInExactlyOneGroup()
    {
        // A button in two groups would draw two separators; one in none
        // would have nowhere to sit.
        var all = SettingsWindow.ToolbarGroups
            .SelectMany(g => g.Buttons.Select(b => b.Id))
            .ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, id => Assert.NotNull(SettingsWindow.ToolbarGroupOf(id)));
    }

    [Fact]
    public void SaysNothingAboutACommandThatIsNotOnTheToolbar()
    {
        Assert.Null(SettingsWindow.ToolbarGroupOf("refactor.rename"));
    }

    [Fact]
    public void OffersAButtonForEveryKindOfWorkThePlanNames()
    {
        // file, modifica, naviga, compila, debug, test, git.
        foreach (var group in new[] { "File", "Edit", "Navigate", "Build", "Debug", "Test", "Git" })
        {
            Assert.NotEmpty(
                SettingsWindow.ToolbarGroups.Single(g => g.Name == group).Buttons);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [AvaloniaFact]
    public async Task OffersTheCommandsAToolbarIsExpectedToCarry()
    {
        // It came with thirteen and read as sparse. Named rather than
        // counted: a number says nothing about which one went missing.
        using var host = new TestWindow();
        await host.SettleAsync();

        var ids = host.Window.AvailableToolbarButtons().Select(b => b.Id).ToList();

        string[] expected =
        [
            IdeCommands.FileNewSolution, IdeCommands.FileOpenSolution, IdeCommands.FileSave,
            IdeCommands.EditUndo, IdeCommands.EditRedo, IdeCommands.EditFind,
            IdeCommands.NavigateBack, IdeCommands.NavigateForward,
            IdeCommands.BuildSolution, IdeCommands.DebugStartWithout, IdeCommands.DebugStop,
            IdeCommands.DebugStart, IdeCommands.DebugStepOver, IdeCommands.DebugStepInto,
            IdeCommands.DebugStepOut,
            IdeCommands.TestRunAll, IdeCommands.GitCommit,
            IdeCommands.ViewTerminal, IdeCommands.ToolsSettings
        ];

        var missing = expected.Where(id => !ids.Contains(id)).ToList();

        Assert.True(missing.Count == 0,
            "The toolbar does not offer:\n" + string.Join("\n", missing));
    }

    [AvaloniaFact]
    public async Task ShowsThemAllUntilTheUserChoosesOtherwise()
    {
        // An empty configuration means everything, so a user who has never
        // opened the settings still gets the buttons added in a new version.
        using var host = new TestWindow();
        await host.SettleAsync();

        Assert.True(host.Window.AvailableToolbarButtons().Count >= 19);
    }
}
