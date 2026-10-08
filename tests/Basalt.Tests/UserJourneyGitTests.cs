using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// End-to-end user journeys through a real <see cref="MainWindow"/>, driving
/// the Git panels and the AI Assistant panel the way a person at the IDE
/// would: open a solution that lives inside a real git repository (made with
/// a real <c>git init</c>, in a temp folder, with no remote), edit and save a
/// file, stage it, commit it, look at its history and its diff, make a
/// branch, and open the assistant.
///
/// Every test works against a real git executable in a throwaway repository;
/// none call a real AI provider or touch <c>ApiKeyStore</c>.
/// </summary>
public sealed class UserJourneyGitTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-git-journey-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void RunGit(string arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        // Only this repository's own settings: a machine's global signing key or hooks path would
        // otherwise make a commit here prompt, hang or fail.
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root, ".no-global-gitconfig");
        foreach (var part in arguments.Split(' ')) info.ArgumentList.Add(part);

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {arguments} failed: {process.StandardError.ReadToEnd()}");
    }

    /// <summary>What git itself says, to check a journey against the real repository
    /// rather than only against what the panels show.</summary>
    private string RunGitOutput(string arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root, ".no-global-gitconfig");
        foreach (var part in arguments.Split(' ')) info.ArgumentList.Add(part);

        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {arguments} failed: {process.StandardError.ReadToEnd()}");

        return output;
    }

    /// <summary>
    /// A real git repository, with a local identity (so a commit never
    /// depends on whatever is configured on the machine running the test),
    /// no remote, and one commit already in it: a console project just
    /// written and nothing in Git Changes yet.
    /// </summary>
    private string MakeRepoWithConsoleProject()
    {
        RunGit("init -q");
        RunGit("config user.email test@example.com");
        RunGit("config user.name Basalt-Test");

        var project = Path.Combine(_root, "App.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(_root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        // Without it, opening the project makes Roslyn write obj/ as a side
        // effect of loading it, and that shows up as an untracked change no
        // journey here is about.
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n");

        RunGit("add -A");
        RunGit("commit -q -m Initial-commit");

        return project;
    }

    private static string L(string key) => Localizer.Get(key);

    /// <summary>
    /// Picks a menu entry by its path of localized headers and runs it, the
    /// way <c>UserJourneyToolsTests</c> does: real menu dispatch rather than a
    /// method called directly, since showing a tool through the menu is how a
    /// person actually gets to the Git panels.
    /// </summary>
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
        Assert.True(condition());
    }

    // ------------------------------------------------------------------
    // Journey 1: opening the solution shows the repo; editing and saving a
    // file lists it in Git Changes.
    //
    // Regression: MainWindowViewModel.SaveAsync/SaveAllAsync wrote the file
    // and told Roslyn about it, but nothing told the git panels. A user who
    // edited and saved a file, then opened Git Changes to commit it, found
    // an empty list until some unrelated git command happened to refresh it.
    // ------------------------------------------------------------------
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task OpeningTheSolutionShowsTheRepoAndSavingAFileListsItInGitChanges()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;

        // The repo already there when the solution opened is detected
        // without the user asking for anything.
        await WaitUntilAsync(() => vm.Repository is not null);

        var programPath = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text =
            "Module Program\n    Sub Main()\n        Console.WriteLine(\"Hi\")\n    End Sub\nEnd Module\n";
        await host.SettleAsync(5);
        Assert.True(vm.ActiveDocument!.IsModified);

        await vm.SaveAllAsync();

        await WaitUntilAsync(() => host.Window.GitChangesForTests!.Unstaged
            .Any(r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
    }

    /// <summary>
    /// A file saved by something other than Save or Save All - the designer writing a form's code
    /// when an event is wired, a QuickBASIC run saving first - shows in Git Changes all the same.
    /// </summary>
    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task AFileSavedOutsideTheSaveCommandsStillListsInGitChanges()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        var programPath = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        var document = vm.ActiveDocument!;
        document.Text = "Module Program\n    Sub Main()\n        Console.WriteLine(\"Saved\")\n    End Sub\nEnd Module\n";
        await document.SaveAsync();

        await WaitUntilAsync(() => host.Window.GitChangesForTests!.Unstaged
            .Any(r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public async Task ADocumentClosedWithItsSolutionNoLongerReportsSaves()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(Path.Combine(_root, "Program.vb"));
        await host.SettleAsync(3);
        var closed = vm.ActiveDocument!;
        var reported = 0;
        vm.DocumentSaved += (_, _) => reported++;

        // What opening another solution does with the documents of this one.
        vm.OpenDocuments.Clear();
        await closed.SaveAsync();

        Assert.Equal(0, reported);
    }

    /// <summary>
    /// Opens the solution, edits Program.vb and saves it, leaving one
    /// unstaged change in Git Changes for a journey to stage or commit.
    /// </summary>
    private async Task<MainWindowViewModel> OpenSolutionWithAnUnstagedChangeAsync(
        TestWindow host, string newBody)
    {
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        var programPath = Path.Combine(_root, "Program.vb");
        await vm.OpenFileAsync(programPath);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text = newBody;
        await host.SettleAsync(5);

        await vm.SaveAllAsync();

        await WaitUntilAsync(() => host.Window.GitChangesForTests!.Unstaged
            .Any(r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));

        return vm;
    }

    // ------------------------------------------------------------------
    // Journey 2: staging the changed file from Git Changes - double-tapping
    // its row in the unstaged list - and committing with a message typed
    // into the panel's own message box, sent with its own Commit button,
    // makes a real commit: `git log` in the temp repository sees it and the
    // changes list goes back to empty.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task StagingAndCommittingFromThePanelMakesARealCommit()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        await OpenSolutionWithAnUnstagedChangeAsync(host,
            "Module Program\n    Sub Main()\n        Console.WriteLine(\"Staged and committed\")\n    End Sub\nEnd Module\n");

        // Opened the way a person reaches it: the Git menu, not a reference
        // kept from building the panel. A panel that is never the active tab
        // never gets its own content realized in the visual tree.
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);

        var panel = host.Window.GitChangesForTests!;

        var lists = panel.GetVisualDescendants().OfType<ListBox>().ToList();
        Assert.True(lists.Count >= 2, $"found {lists.Count} list boxes");
        var unstagedList = lists[1];

        var row = panel.Unstaged.Single(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal));
        unstagedList.SelectedItem = row;
        unstagedList.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));

        await WaitUntilAsync(() => panel.Staged.Any(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
        Assert.DoesNotContain(panel.Unstaged,
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal));

        var message = panel.GetVisualDescendants().OfType<TextBox>().First();
        message.Text = "Describe what changed";
        await host.SettleAsync(2);

        var commit = panel.GetVisualDescendants().OfType<Button>()
            .First(b => Equals(b.Content, Localizer.Get(StringKeys.GitCommit)));

        commit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await WaitUntilAsync(() => panel.IsShowingEmptyState);

        var log = RunGitOutput("log --oneline -1");
        Assert.Contains("Describe what changed", log);
    }
}
