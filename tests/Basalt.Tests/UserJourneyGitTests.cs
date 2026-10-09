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

    // ------------------------------------------------------------------
    // Journey 3: the history lists commits newest first, with message,
    // author and date, and selecting one shows its diff - the removed and
    // added lines a real reviewer is there to read.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task HistoryListsCommitsNewestFirstAndSelectingOneShowsItsDiff()
    {
        var project = MakeRepoWithConsoleProject();

        // A second, real commit that both changes an existing line and adds a
        // new one, so the diff has something removed as well as added.
        File.WriteAllText(Path.Combine(_root, "Program.vb"),
            "Module Program ' v2\n    Sub Main()\n        Console.WriteLine(\"v2\")\n    End Sub\nEnd Module\n");
        RunGit("add -A");
        RunGit("commit -q -m Second-commit");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitHistory));
        await host.SettleAsync(5);

        var history = host.Window.GitHistoryForTests!;
        await WaitUntilAsync(() => history.Rows.Count >= 2);

        Assert.Equal("Second-commit", history.Rows[0].Commit.Subject);
        Assert.Equal("Initial-commit", history.Rows[1].Commit.Subject);
        Assert.Equal("Basalt-Test", history.Rows[0].Commit.Author);
        Assert.True(history.Rows[0].Commit.When >= history.Rows[1].Commit.When);

        var listBox = history.GetVisualDescendants().OfType<ListBox>().First();
        listBox.SelectedItem = history.Rows[0];
        await host.SettleAsync(5);

        var diff = host.Window.GitDiffForTests!;
        await WaitUntilAsync(() => diff.Lines.Count > 0);

        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Added);
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Removed);
        Assert.Contains(diff.Lines, l => l.Text.Contains("Program.vb", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------
    // Journey 4: branches - create one, switch to it, commit there, switch
    // back, merge it in, then delete it - all from the Branches panel, each
    // step confirmed against what git itself reports.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CreatesSwitchesCommitsMergesAndDeletesABranchFromThePanel()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitBranches));
        await host.SettleAsync(5);

        var branches = host.Window.BranchesForTests!;
        await WaitUntilAsync(() => branches.Local.Count >= 1);

        Assert.True(branches.Local.Single(r => r.Branch.Name != "feature").Branch.IsCurrent);

        // Create "feature" and switch to it, the way typing a name and
        // pressing the button does.
        branches.NewBranchName = "feature";
        branches.RequestCreate();

        await WaitUntilAsync(
            () => RunGitOutput("branch --show-current").Trim() == "feature");
        await WaitUntilAsync(() => branches.Local.Any(
            r => r.Branch.Name == "feature" && r.Branch.IsCurrent));

        // Commit something on the branch, through Git Changes rather than
        // calling the service directly: the panel is what a person uses.
        await OpenSolutionWithAnUnstagedChangeAsync(host,
            "Module Program\n    Sub Main()\n        Console.WriteLine(\"On feature\")\n    End Sub\nEnd Module\n");

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);

        var changes = host.Window.GitChangesForTests!;
        changes.StageAllForTests();
        await WaitUntilAsync(() => changes.Unstaged.Count == 0 && changes.Staged.Count > 0);

        changes.CommitMessage = "Work on feature";
        changes.RequestCommitForTests();
        await WaitUntilAsync(() => changes.IsShowingEmptyState);

        Assert.Contains("Work on feature", RunGitOutput("log --oneline -1"));

        // Back to main.
        var mainRow = branches.Local.Single(r => r.Branch.Name != "feature");
        branches.CheckoutForTests(mainRow);

        await WaitUntilAsync(
            () => RunGitOutput("branch --show-current").Trim() != "feature");
        var mainName = RunGitOutput("branch --show-current").Trim();

        // Merge feature back in.
        await WaitUntilAsync(() => branches.Local.Any(r => r.Branch.Name == "feature"));
        var featureRow = branches.Local.Single(r => r.Branch.Name == "feature");
        branches.MergeForTests(featureRow);

        await WaitUntilAsync(
            () => RunGitOutput("log --oneline -1").Contains("Work on feature"));

        // Delete it, now that its work lives on main.
        await WaitUntilAsync(() => branches.Local.Any(r => r.Branch.Name == "feature"));
        branches.DeleteForTests(branches.Local.Single(r => r.Branch.Name == "feature"));

        await WaitUntilAsync(() => !branches.Local.Any(r => r.Branch.Name == "feature"));

        Assert.DoesNotContain("feature", RunGitOutput("branch"));
        Assert.True(branches.Local.Single(r => r.Branch.Name == mainName).Branch.IsCurrent);
    }

    /// <summary>Whatever dialog <see cref="MainWindow"/> opened on top of the window, if any.</summary>
    private static async Task<Window?> WaitForDialogAsync(TestWindow host)
    {
        Window? dialog = null;

        for (var attempt = 0; attempt < 60 && dialog is null; attempt++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static void ClickButton(Window dialog, Func<Button, bool> choose)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(choose);
        button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    // ------------------------------------------------------------------
    // Journey 5: unstaging a staged file, discarding a file's changes
    // (after its confirmation), stage all / unstage all over the whole
    // working tree, and amending the last commit - all from the real panel
    // and its own controls, the way a person reaches them.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task UnstagesDiscardsStagesAllUnstagesAllAndAmendsFromThePanel()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        await OpenSolutionWithAnUnstagedChangeAsync(host,
            "Module Program\n    Sub Main()\n        Console.WriteLine(\"Edited\")\n    End Sub\nEnd Module\n");

        // A second file nobody opened in the editor, so stage/unstage all are
        // proven against the whole working tree and not just an open document.
        File.WriteAllText(Path.Combine(_root, "Other.vb"), "Module Other\nEnd Module\n");
        await vm.RefreshRepositoryAsync();

        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);

        var changes = host.Window.GitChangesForTests!;
        await WaitUntilAsync(() => changes.Unstaged.Count == 2);

        // Stage all.
        changes.StageAllForTests();
        await WaitUntilAsync(() => changes.Staged.Count == 2 && changes.Unstaged.Count == 0);

        // Unstage one file by itself.
        var programRow = changes.Staged.Single(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal));
        changes.UnstageForTests(programRow);

        await WaitUntilAsync(() => changes.Unstaged.Any(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));
        Assert.DoesNotContain(changes.Staged,
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal));

        // Unstage all, back to a clean index, through the panel's own link
        // button rather than the test-only hook, since this is the one
        // journey step with no dedicated For-Tests method.
        var unstageAllButton = changes.GetVisualDescendants().OfType<Button>()
            .First(b => b.Content is TextBlock t && t.Text == Localizer.Get(StringKeys.GitUnstageAll));
        unstageAllButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await WaitUntilAsync(() => changes.Staged.Count == 0 && changes.Unstaged.Count == 2);

        // Discard Program.vb's edit: a tracked file, so there is an original
        // to go back to.
        var unstagedList = changes.GetVisualDescendants().OfType<ListBox>().ToList()[1];
        unstagedList.SelectedItem = changes.Unstaged.Single(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal));

        ContextMenus.ChooseForTests(
            changes.UnstagedMenu!, Localizer.Get(StringKeys.GitDiscardChanges));

        var confirm = await WaitForDialogAsync(host);
        Assert.NotNull(confirm);
        ClickButton(confirm!, b => b.Classes.Contains("primary"));
        await host.SettleAsync(5);

        await WaitUntilAsync(() => !changes.Unstaged.Any(
            r => r.Change.Path.Contains("Program.vb", StringComparison.Ordinal)));

        var programText = await File.ReadAllTextAsync(Path.Combine(_root, "Program.vb"));
        Assert.DoesNotContain("Edited", programText);

        // Discard Other.vb too: nobody ever staged this one, so there is
        // nothing in git's index or history to restore it from - the only
        // sensible meaning left for Discard on it is removing the file.
        unstagedList.SelectedItem = changes.Unstaged.Single(
            r => r.Change.Path.Contains("Other.vb", StringComparison.Ordinal));

        ContextMenus.ChooseForTests(
            changes.UnstagedMenu!, Localizer.Get(StringKeys.GitDiscardChanges));

        var confirmNew = await WaitForDialogAsync(host);
        Assert.NotNull(confirmNew);
        ClickButton(confirmNew!, b => b.Classes.Contains("primary"));
        await host.SettleAsync(5);

        await WaitUntilAsync(() => changes.IsShowingEmptyState);
        Assert.False(File.Exists(Path.Combine(_root, "Other.vb")));

        // Amend the last commit: ticking the box loads its message, and
        // committing again replaces it instead of adding a new one.
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);

        changes.IsAmending = true;
        await host.SettleAsync(3);

        Assert.Equal("Initial-commit", changes.CommitMessage);

        changes.CommitMessage = "Initial commit, corrected";
        changes.RequestCommitForTests();

        await WaitUntilAsync(
            () => RunGitOutput("log --oneline").Contains("Initial commit, corrected"));
        Assert.Single(RunGitOutput("log --oneline").Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    // ------------------------------------------------------------------
    // Journey 6: Pull, Push and Fetch when the repository has no remote at
    // all. Each is a real git process that fails fast and says why, rather
    // than a hang or a crash - the three ways this could go wrong for
    // someone who has not yet added "origin".
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task PullPushAndFetchWithNoRemoteFailClearlyWithoutHangingOrCrashing()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null);

        // Pull has nothing to fast-forward from and no upstream configured:
        // git refuses it, and that refusal belongs in the output pane.
        var before = vm.BuildOutput.Count;
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitPull));
        await WaitUntilAsync(() => vm.BuildOutput.Count > before);
        Assert.Contains(vm.BuildOutput.Skip(before), line => line.StartsWith("[git]", StringComparison.Ordinal));

        // Push has nowhere configured to push to.
        before = vm.BuildOutput.Count;
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitPush));
        await WaitUntilAsync(() => vm.BuildOutput.Count > before);
        Assert.Contains(vm.BuildOutput.Skip(before), line => line.StartsWith("[git]", StringComparison.Ordinal));

        // Fetch with no remotes configured is not an error to git - "fetch
        // --all" over an empty list of remotes succeeds doing nothing - but
        // it should still report something rather than sit there silent.
        before = vm.BuildOutput.Count;
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitFetch));
        await WaitUntilAsync(() => vm.BuildOutput.Count > before);
        Assert.Contains(vm.BuildOutput.Skip(before), line => line.StartsWith("[git]", StringComparison.Ordinal));

        // None of the three left the window stuck: it is still the live
        // window the test built, not a dialog or a hang in front of it.
        Assert.True(host.Window.IsVisible);
        Assert.Null(host.Window.OwnedWindows.FirstOrDefault());
    }
}
