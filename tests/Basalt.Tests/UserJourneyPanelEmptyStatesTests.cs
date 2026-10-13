using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.ObjectBrowser;
using Dock.Model.Core;
using System.Reflection;

namespace Basalt.Tests;

/// <summary>
/// What a person sees in the tool panels when there is nothing to show yet:
/// a Visual Basic project opened alone, with its one source file open and one
/// compile error in it, and no solution at all. Each panel shows one empty
/// state at a time, and a panel that has something to list lists it.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyPanelEmptyStatesTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-empty-states-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-empty-states-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyPanelEmptyStatesTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private static string L(string key, params object[] args) => Localizer.Get(key, args);

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + string.Join(" > ", path));
            level = hit.Children ?? [];
        }

        if (hit!.Action is { } action) action();
        else hit.Command!.Execute(null);
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 200)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }
        return condition();
    }

    /// <summary>The text a person can read under a panel: visible, non-blank text blocks.</summary>
    private static List<string> VisibleTextsOf(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
            .Select(text => text.Text!)
            .ToList();

    /// <summary>
    /// A console project with one namespace, one class and one compile error:
    /// the state the defects were seen in. The project is opened alone, so it
    /// is not a solution and the folder is not a Git repository.
    /// </summary>
    private string WriteTourProject()
    {
        var projectPath = Path.Combine(_root, "Tour.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), """
            Namespace Tour
                Public Class Greeter
                    Public Sub Hello()
                        Dim count As Integer = "one"
                    End Sub
                End Class
            End Namespace

            Module Program
                Sub Main()
                End Sub
            End Module
            """);
        return projectPath;
    }

    /// <summary>Waits until the project is open, as the status bar reports it.</summary>
    private static async Task WaitForProjectToOpenAsync(TestWindow host)
    {
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.True(await WaitUntilAsync(host,
                () => vm.StatusMessage == L(StringKeys.StatusOpened, "Tour.vbproj"), maxPasses: 1200),
            "the project did not finish opening.");
    }

    /// <summary>Shows the Object Browser from the View menu, as a person does the first time.</summary>
    private static async Task<ObjectBrowserPanel> ShowObjectBrowserAsync(TestWindow host)
    {
        InvokeMenu(host.Window, L(StringKeys.MenuView), L(StringKeys.ToolObjectBrowser));
        await host.SettleAsync(10);

        return host.Window.GetVisualDescendants().OfType<ObjectBrowserPanel>().Single();
    }

    /// <summary>
    /// Shows the Object Browser the way the dock shows a tool that the View
    /// command did not open: the tool is made visible in its area, as a
    /// restored layout or a click on its tab does, and the View command is
    /// never used.
    /// </summary>
    private static async Task<ObjectBrowserPanel> ShowObjectBrowserInItsAreaAsync(TestWindow host)
    {
        var window = host.Window;
        var factory = typeof(MainWindow)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        var tool = (IDockable)factory.GetType().GetProperty("ObjectBrowser")!.GetValue(factory)!;

        typeof(MainWindow)
            .GetMethod("ShowTool", BindingFlags.Instance | BindingFlags.NonPublic, [typeof(IDockable)])!
            .Invoke(window, [tool]);
        await host.SettleAsync(10);

        return window.GetVisualDescendants().OfType<ObjectBrowserPanel>().Single();
    }

    // ------------------------------------------------------------------
    // Object Browser. The project is loaded before the browser is first
    // shown: it must list the project's types, and show no empty state.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheObjectBrowserListsTheTypesOfTheProjectOpenedAloneWhenItIsFirstShownAfterLoading()
    {
        using var host = new TestWindow(WriteTourProject());
        await host.SettleAsync(10);
        await WaitForProjectToOpenAsync(host);

        var browser = await ShowObjectBrowserInItsAreaAsync(host);

        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserNamespaceRow>().Any(ns => ns.Name == "Tour"), maxPasses: 400),
            "the Object Browser does not list the namespace Tour of the open project.");

        // Expanding the namespace lists the class the project declares.
        var tour = browser.TypeRows.OfType<BrowserNamespaceRow>().Single(ns => ns.Name == "Tour");
        browser.TypeList.SelectedItem = tour;
        browser.TypeList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        Assert.True(await WaitUntilAsync(host,
                () => browser.TypeRows.OfType<BrowserTypeRow>().Any(row => row.Symbol.Name == "Greeter")),
            "the Object Browser does not list the class Greeter under Tour.");

        await host.SettleAsync(5);

        var visible = VisibleTextsOf(browser);
        Assert.DoesNotContain(L(StringKeys.ObjectBrowserEmpty), visible);
        Assert.Contains(browser.Libraries, library =>
            library.Kind == BrowserLibraryKind.Project && library.Name == "Tour");
    }

    // ------------------------------------------------------------------
    // Object Browser with no solution: one empty state, not two side by side.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task WithNoSolutionTheObjectBrowserShowsOneEmptyStateAtATime()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        var browser = await ShowObjectBrowserAsync(host);
        await host.SettleAsync(5);

        var visible = VisibleTextsOf(browser);
        Assert.Single(visible, text => text == L(StringKeys.ObjectBrowserEmpty));
        Assert.DoesNotContain(L(StringKeys.ObjectBrowserSelectType), visible);
    }

    // ------------------------------------------------------------------
    // Git Changes in a folder that is not a repository: one empty state with
    // the button that starts a repository, and none of the repository's boxes.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task GitChangesInAFolderThatIsNotARepositoryOffersOnlyToStartOne()
    {
        using var host = new TestWindow(WriteTourProject());
        await host.SettleAsync(10);
        await WaitForProjectToOpenAsync(host);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);
        var panel = host.Window.GitChangesForTests!;

        var visible = VisibleTextsOf(panel);
        Assert.Single(visible, text => text == L(StringKeys.GitNotARepository));

        Assert.Empty(VisibleButtonsOf(panel).Where(text => text == L(StringKeys.GitCommit)));
        Assert.Empty(VisibleButtonsOf(panel).Where(text => text == L(StringKeys.GitStageAll)));
        Assert.Empty(VisibleButtonsOf(panel).Where(text => text == L(StringKeys.GitUnstageAll)));
        Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible));
        Assert.DoesNotContain(L(StringKeys.GitStaged), visible);
        Assert.DoesNotContain(L(StringKeys.GitChanges), visible);

        Assert.Contains(L(StringKeys.MenuGitInitialize), VisibleButtonsOf(panel));
    }

    [AvaloniaFact]
    public async Task StartingARepositoryFromTheGitChangesPanelCreatesOneInTheFolder()
    {
        using var host = new TestWindow(WriteTourProject());
        await host.SettleAsync(10);
        await WaitForProjectToOpenAsync(host);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);
        var panel = host.Window.GitChangesForTests!;

        var start = panel.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Content as string == L(StringKeys.MenuGitInitialize));
        start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await WaitUntilAsync(host, () => Directory.Exists(Path.Combine(_root, ".git"))),
            "the button did not start a repository in the folder.");

        // The panel becomes the repository's at once, without reopening the solution.
        // Reintroduced once (no new detection after the start): the panel kept its empty state.
        Assert.True(await WaitUntilAsync(host, () => !VisibleTextsOf(panel).Contains(L(StringKeys.GitNotARepository))
                && VisibleButtonsOf(panel).Contains(L(StringKeys.GitCommit))),
            "the panel still says the folder is not a repository.");
    }

    /// <remarks>
    /// Reintroduced once (detection kept the previous solution's repository and
    /// reported nothing when it found none): the panel kept the first
    /// solution's repository.
    /// </remarks>
    [AvaloniaFact]
    public async Task OpeningASolutionWithoutARepositoryAfterOneWithShowsTheEmptyState()
    {
        var withRepository = Directory.CreateDirectory(Path.Combine(_root, "WithGit")).FullName;
        var withoutRepository = Directory.CreateDirectory(Path.Combine(_root, "WithoutGit")).FullName;
        string Project(string folder)
        {
            var path = Path.Combine(folder, "App.vbproj");
            File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(folder, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
            return path;
        }
        var first = Project(withRepository);
        var second = Project(withoutRepository);
        Assert.True(await Basalt.Workspace.GitSourceControlService.InitializeAsync(withRepository, "First commit"),
            "git could not start the first repository.");

        using var host = new TestWindow(first);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.True(await WaitUntilAsync(host, () => vm.StatusMessage == L(StringKeys.StatusOpened, "App.vbproj"), maxPasses: 1200),
            "the first project did not finish opening.");
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);
        var panel = host.Window.GitChangesForTests!;
        Assert.True(await WaitUntilAsync(host, () => vm.Repository is not null), "the first solution's repository was not found.");

        await vm.OpenSolutionAsync(second);

        Assert.True(await WaitUntilAsync(host, () => vm.Repository is null
                && VisibleTextsOf(panel).Contains(L(StringKeys.GitNotARepository))),
            "the panel kept the first solution's repository.");
    }

    [AvaloniaFact]
    public async Task WithNoSolutionTheGitPanelsAskForOneAndOfferNothingToStart()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);
        var panel = host.Window.GitChangesForTests!;

        Assert.Contains(L(StringKeys.GitNoSolution), VisibleTextsOf(panel));
        Assert.DoesNotContain(L(StringKeys.GitNotARepository), VisibleTextsOf(panel));
        Assert.DoesNotContain(L(StringKeys.MenuGitInitialize), VisibleButtonsOf(panel));
    }

    // ------------------------------------------------------------------
    // Branches in a folder that is not a repository: the same empty state.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task BranchesInAFolderThatIsNotARepositoryOffersOnlyToStartOne()
    {
        using var host = new TestWindow(WriteTourProject());
        await host.SettleAsync(10);
        await WaitForProjectToOpenAsync(host);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitBranches));
        await host.SettleAsync(5);
        var panel = host.Window.BranchesForTests!;

        var visible = VisibleTextsOf(panel);
        Assert.Single(visible, text => text == L(StringKeys.GitNotARepository));

        var buttons = VisibleButtonsOf(panel);
        Assert.DoesNotContain(L(StringKeys.BranchCreate), buttons);
        Assert.DoesNotContain(L(StringKeys.BranchMerge), buttons);
        Assert.DoesNotContain(L(StringKeys.MenuGitFetch), buttons);
        Assert.DoesNotContain(L(StringKeys.MenuGitPull), buttons);
        Assert.DoesNotContain(L(StringKeys.MenuGitPush), buttons);
        Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>().Where(box => box.IsEffectivelyVisible));
        Assert.Contains(L(StringKeys.MenuGitInitialize), buttons);
    }

    // ------------------------------------------------------------------
    // Tests: a project with no tests says "No tests discovered." once.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheTestsPanelSaysNoTestsWereDiscoveredOnce()
    {
        using var host = new TestWindow(WriteTourProject());
        await host.SettleAsync(10);
        await WaitForProjectToOpenAsync(host);

        InvokeMenu(host.Window, L(StringKeys.MenuTest), L(StringKeys.ToolTests));
        await host.SettleAsync(10);
        var panel = host.Window.GetVisualDescendants().OfType<TestExplorerPanel>().Single();

        var visible = VisibleTextsOf(panel);
        Assert.Single(visible, text => text == L(StringKeys.TestsNoneDiscovered));
    }

    // ------------------------------------------------------------------
    // Database: nothing open says what to do once, in the status line.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheDatabasePanelSaysToOpenAFileOnce()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        InvokeMenu(host.Window, L(StringKeys.MenuView), L(StringKeys.ToolDatabase));
        await host.SettleAsync(10);
        var panel = host.Window.GetVisualDescendants().OfType<DatabasePanel>().Single();

        var visible = VisibleTextsOf(panel);
        Assert.Single(visible, text => text == L(StringKeys.DatabaseNothingOpen));
    }

    // ------------------------------------------------------------------
    // Assistant with no assistant chosen: "Settings" is a button that opens
    // the Settings window. The window cannot open on the assistant page, so
    // it opens on whatever page it shows first.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheAssistantOffersASettingsButtonWhenNoAssistantIsChosen()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);

        InvokeMenu(host.Window, L(StringKeys.MenuTools), L(StringKeys.MenuToolsAssistant));
        await host.SettleAsync(10);
        var panel = host.Window.AssistantForTests!;

        var visible = VisibleTextsOf(panel);
        Assert.Contains(L(StringKeys.AiNoAssistantLead), visible);
        Assert.DoesNotContain(L(StringKeys.AiNoAssistant), visible);

        var settings = panel.GetVisualDescendants().OfType<Button>()
            .Single(button => button.IsEffectivelyVisible && button.Content as string == L(StringKeys.AiOpenSettings));
        settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await WaitUntilAsync(host,
                () => host.Window.OwnedWindows.OfType<SettingsWindow>().Any()),
            "the Settings button did not open the Settings window.");

        host.Window.OwnedWindows.OfType<SettingsWindow>().First().Close();
    }

    /// <summary>The captions of the buttons a person can press under a control.</summary>
    private static List<string> VisibleButtonsOf(Visual root) =>
        root.GetVisualDescendants().OfType<Button>()
            .Where(button => button.IsEffectivelyVisible && button.Content is string)
            .Select(button => (string)button.Content!)
            .ToList();
}
