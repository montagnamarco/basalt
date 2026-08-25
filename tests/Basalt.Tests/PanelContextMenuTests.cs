using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Services;
using Basalt.Extensibility;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using TestResult = Basalt.Core.Services.TestResult;

namespace Basalt.Tests;

/// <summary>The shared menu builder.</summary>
public class ContextMenuBuilderTests
{
    [AvaloniaFact]
    public void BuildsAnEntryForEachAction()
    {
        var menu = ContextMenus.Build([
            new MenuAction("First", () => { }),
            MenuAction.Separator,
            new MenuAction("Second", () => { })
        ]);

        var items = menu.ItemsSource!.OfType<MenuItem>().ToList();

        Assert.Equal(2, items.Count);
        Assert.Single(menu.ItemsSource!.OfType<Separator>());
    }

    [AvaloniaFact]
    public void ReportsWhatWasChosen()
    {
        var chosen = 0;

        var menu = ContextMenus.Build([new MenuAction("Do it", () => chosen++)]);

        ContextMenus.ChooseForTests(menu, "Do it");

        Assert.Equal(1, chosen);
    }

    [AvaloniaFact]
    public void AsksWhetherAnEntryAppliesWhenTheMenuOpens()
    {
        // What a panel can do depends on what is selected, and that changes
        // between openings: deciding once when the menu was built would leave
        // it permanently wrong.
        var available = false;

        var menu = ContextMenus.Build([
            new MenuAction("Sometimes", () => { }) { IsAvailable = () => available }
        ]);

        var closed = ContextMenus.OpenForTests(menu);
        Assert.False(closed[0].Enabled);

        available = true;

        var opened = ContextMenus.OpenForTests(menu);
        Assert.True(opened[0].Enabled);
    }

    [AvaloniaFact]
    public void ShowsTheShortcutBesideAnEntry()
    {
        var menu = ContextMenus.Build([
            new MenuAction("Save", () => { }) { Gesture = "Ctrl+S" }
        ]);

        Assert.NotNull(menu.ItemsSource!.OfType<MenuItem>().Single().InputGesture);
    }
}

/// <summary>The menus on the panels.</summary>
public sealed class PanelContextMenuTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-panelmenus", Guid.NewGuid().ToString("N"));

    public PanelContextMenuTests() => Directory.CreateDirectory(_root);

    // Test window

    [AvaloniaFact]
    public void TheTestWindowOffersRunAndDebug()
    {
        var panel = new TestExplorerPanel();

        var headers = panel.Menu!.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.Contains("Run", headers);
        Assert.Contains("Debug", headers);
        Assert.Contains("Copy Name", headers);
    }

    [AvaloniaFact]
    public void DebuggingIsUnavailableWithNothingSelected()
    {
        // Debugging a class rather than a test means nothing.
        var panel = new TestExplorerPanel();

        var state = ContextMenus.OpenForTests(panel.Menu!);

        Assert.False(state.Single(s => s.Header == "Debug").Enabled);
    }

    [AvaloniaFact]
    public void DebuggingBecomesAvailableForASingleTest()
    {
        var panel = new TestExplorerPanel();

        panel.Show([new TestCase("A.B.One", "/a/Tests.vbproj")]);
        panel.SelectForTests(panel.Roots[0].Leaves.Single());

        var state = ContextMenus.OpenForTests(panel.Menu!);

        Assert.True(state.Single(s => s.Header == "Debug").Enabled);
    }

    [AvaloniaFact]
    public void CopyingAFailureNeedsATestThatFailed()
    {
        var panel = new TestExplorerPanel();

        panel.Show([new TestCase("A.B.One", "/a/Tests.vbproj")]);
        panel.SelectForTests(panel.Roots[0].Leaves.Single());

        var before = ContextMenus.OpenForTests(panel.Menu!);
        Assert.False(before.Single(s => s.Header == "Copy Failure").Enabled);

        panel.ShowResults([new TestResult("A.B.One", TestOutcome.Failed, TimeSpan.Zero)
        {
            Message = "It went wrong."
        }]);

        panel.SelectForTests(panel.Roots[0].Leaves.Single());

        var after = ContextMenus.OpenForTests(panel.Menu!);
        Assert.True(after.Single(s => s.Header == "Copy Failure").Enabled);
    }

    // Git changes

    [AvaloniaFact]
    public void TheStagedListOffersUnstagingAndTheOtherOffersStaging()
    {
        // Offering both everywhere would leave half the menu inert.
        var panel = new GitChangesPanel();

        var staged = panel.StagedMenu!.ItemsSource!.OfType<MenuItem>()
            .Select(i => i.Header?.ToString()).ToList();

        var unstaged = panel.UnstagedMenu!.ItemsSource!.OfType<MenuItem>()
            .Select(i => i.Header?.ToString()).ToList();

        Assert.Contains("Unstage", staged);
        Assert.DoesNotContain("Stage", staged);

        Assert.Contains("Stage", unstaged);
        Assert.DoesNotContain("Unstage", unstaged);
    }

    [AvaloniaFact]
    public void DiscardingIsOfferedOnlyForUnstagedChanges()
    {
        // Discarding a staged change would throw away work the user has
        // already chosen to keep.
        var panel = new GitChangesPanel();

        var staged = ContextMenus.OpenForTests(panel.StagedMenu!);

        Assert.False(staged.Single(s => s.Header == "Discard Changes…").Enabled);
    }

    // Outline

    [AvaloniaFact]
    public void TheOutlineOffersGoToAndCopy()
    {
        var panel = new OutlinePanel();

        var headers = panel.Menu!.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.Contains("Go to Symbol", headers);
        Assert.Contains("Copy Name", headers);
    }

    [AvaloniaFact]
    public void GoingToASymbolNeedsOneSelected()
    {
        var panel = new OutlinePanel();

        var state = ContextMenus.OpenForTests(panel.Menu!);

        Assert.False(state.Single(s => s.Header == "Go to Symbol").Enabled);
    }

    // Solution explorer

    [AvaloniaFact]
    public async Task TheExplorerOffersTheFileCommands()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var explorer = host.Window.GetVisualDescendants()
            .OfType<SolutionExplorerPanel>()
            .Single();

        var headers = explorer.Menu!.ItemsSource!
            .OfType<MenuItem>()
            .Select(i => i.Header?.ToString())
            .ToList();

        Assert.Contains("Open", headers);
        Assert.Contains("Copy Path", headers);
        Assert.Contains("Refresh", headers);

        // Named for the platform's own file manager.
        Assert.Contains(headers, h => h is "Reveal in Finder" or "Show in Explorer"
                                        or "Show in File Manager");
    }

    [AvaloniaFact]
    public async Task OpeningIsUnavailableWithNothingSelected()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var explorer = host.Window.GetVisualDescendants()
            .OfType<SolutionExplorerPanel>()
            .Single();

        // Said rather than assumed: this ran green alone and failed once in a
        // full run, so the precondition is asserted instead of being taken on
        // trust. A failure here now names the cause instead of pointing at
        // the menu.
        Assert.Null(explorer.SelectedNode);

        var state = ContextMenus.OpenForTests(explorer.Menu!);

        Assert.False(state.Single(s => s.Header == "Open").Enabled);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [AvaloniaFact]
    public async Task NoPanelIconStandsForTwoUnrelatedCommands()
    {
        // Replaces a rule that demanded an icon on every entry. An icon
        // shared by unrelated commands reads as noise; one left out reads as
        // a command whose name is enough.
        using var host = new TestWindow();
        await host.SettleAsync();

        var clashing = new List<string>();

        foreach (var menu in host.Window.GetVisualDescendants()
                     .OfType<Control>()
                     .Select(c => c.ContextMenu)
                     .OfType<ContextMenu>()
                     .Distinct())
        {
            // Copy Path and Copy Relative Path are one verb on one object,
            // so they share an icon on purpose.
            var byIcon = menu.Items
                .OfType<MenuItem>()
                .Where(i => i.Icon is IconView)
                .GroupBy(i => ((IconView)i.Icon!).Kind)
                .Where(g => g.Key != IconKind.Copy && g.Count() > 1)
                .Select(g => $"{g.Key}: {string.Join(", ", g.Select(i => i.Header))}");

            clashing.AddRange(byIcon);
        }

        Assert.True(clashing.Count == 0,
            "These icons stand for unrelated commands:\n"
            + string.Join("\n", clashing.Distinct()));
    }
}
