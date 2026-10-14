using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The welcome screen sits on top of the docking area, so whenever it stays
/// visible the whole IDE looks empty even though the panels loaded correctly.
/// </summary>
public sealed class WelcomeScreenTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-welcome", Guid.NewGuid().ToString("N"));

    public WelcomeScreenTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void IsVisibleOnlyBeforeAnythingIsLoaded()
    {
        using var vm = new MainWindowViewModel();

        Assert.True(vm.IsWelcomeVisible);
    }

    [Fact]
    public async Task HidesAsSoonAsASolutionIsOpenedEvenWithNoDocuments()
    {
        // The regression: opening a solution populates the explorer but opens
        // no document, and the welcome screen used to keep covering the panels.
        var created = await SolutionTemplates.CreateAsync(
            _root, "OpenedSolution", ProjectTemplate.ClassLibrary);

        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(created.SolutionPath);

        Assert.Empty(vm.OpenDocuments);
        Assert.NotEmpty(vm.Explorer.Roots);
        Assert.False(vm.IsWelcomeVisible);
    }

    [Fact]
    public async Task HidesWhenADocumentIsOpenedWithoutASolution()
    {
        var file = Path.Combine(_root, "Loose.cs");
        await File.WriteAllTextAsync(file, "class Loose { }");

        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(file);

        Assert.Null(vm.SolutionPath);
        Assert.False(vm.IsWelcomeVisible);
    }

    [AvaloniaFact]
    public async Task TheWelcomeBorderStopsCoveringTheDockAfterOpeningASolution()
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, "RenderedSolution", ProjectTemplate.ClassLibrary);

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        var welcome = window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible);

        await vm.OpenSolutionAsync(created.SolutionPath);
        for (var i = 0; i < 5; i++) { window.UpdateLayout(); await Task.Delay(60); }

        Assert.False(welcome.IsVisible);
    }

    /// <remarks>
    /// The database client works without a solution, and the welcome screen covered its tabs: a query opened
    /// from the Database menu was there, running, and could not be seen.
    /// </remarks>
    [AvaloniaFact]
    public async Task ADatabaseTabWithoutASolutionIsNotCoveredAndClosingItBringsTheWelcomeBack()
    {
        using var host = new TestWindow();
        await host.SettleAsync(5);
        var welcome = host.Window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible, "The welcome screen is not shown at start.");

        Invoke(host.Window, Basalt.Core.Localization.StringKeys.DbNewQuery);
        await host.SettleAsync(5);

        var document = Assert.Single(host.Window.DatabaseDocumentsForTests);
        Assert.False(welcome.IsVisible, "The welcome screen covers the query tab.");

        host.Window.CloseDatabaseDocumentForTests(document);
        await host.SettleAsync(5);

        Assert.Empty(host.Window.DatabaseDocumentsForTests);
        Assert.True(welcome.IsVisible, "Closing the last tab did not bring the welcome screen back.");
    }

    [AvaloniaFact]
    public async Task APanelAskedForWithoutASolutionIsNotCovered()
    {
        using var host = new TestWindow();
        await host.SettleAsync(5);
        var welcome = host.Window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible);

        Invoke(host.Window, Basalt.Core.Localization.StringKeys.DbExplorerTitle);
        await host.SettleAsync(5);

        Assert.False(welcome.IsVisible, "The welcome screen covers the Database Explorer the user asked for.");
    }

    [AvaloniaFact]
    public async Task ATerminalOpenedWithoutASolutionIsNotCovered()
    {
        // View > New Terminal opens its panel through the dock factory, not through ShowTool.
        using var host = new TestWindow();
        await host.SettleAsync(5);
        var welcome = host.Window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible);

        Invoke(host.Window, Basalt.Core.Localization.StringKeys.MenuViewNewTerminal);
        await host.SettleAsync(5);

        Assert.False(welcome.IsVisible, "The welcome screen covers the terminal the user opened.");
    }

    /// <summary>Runs the first menu entry with this text, wherever it sits in the menus.</summary>
    private static void Invoke(MainWindow window, string key)
    {
        var header = Basalt.Core.Localization.Localizer.Get(key);
        var entry = Leaves(window.MenuEntriesForTests()).First(e => e.Header == header);

        if (entry.Action is { } action) action();
        else entry.Command!.Execute(null);
    }

    private static IEnumerable<MenuEntry> Leaves(IReadOnlyList<MenuEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.IsSeparator) continue;

            if (entry.Children is { Count: > 0 } children)
            {
                foreach (var leaf in Leaves(children)) yield return leaf;
            }
            else
            {
                yield return entry;
            }
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
