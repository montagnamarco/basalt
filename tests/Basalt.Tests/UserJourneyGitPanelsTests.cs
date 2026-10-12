using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Git panels - Git Changes, Branches and the diff view - driven the way a
/// person would through a real <see cref="MainWindow"/>: a solution opened in a
/// real repository made with <c>git init</c> in a temp folder, files edited and
/// saved in the editor, rows double-tapped, buttons pressed, menus chosen. The
/// outcome is checked against what git itself reports and against what the
/// panels and the status bar show.
///
/// Each test is one journey. Where a journey turned out to be broken, the fix
/// lives next to the code it changed and the journey here is its regression
/// test. None of these tests call a real AI provider or a remote.
/// </summary>
public sealed class UserJourneyGitPanelsTests : IDisposable
{
    private const string OriginalProgram =
        "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n";

    private const string EditedProgram =
        "Module Launcher\n    Sub Main()\n        Console.WriteLine(\"Hi\")\n    End Sub\nEnd Module\n";

    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-git-panels-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // ------------------------------------------------------------------
    // Repository helpers. Every git call runs in the temp repository with
    // only its own settings: a machine's global identity, signing key or
    // hooks path must never decide what a journey does.
    // ------------------------------------------------------------------

    /// <summary>Runs git in the temp repository and returns what it printed; throws on failure.</summary>
    private string Git(params string[] arguments)
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
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(info)!;

        // Output is read to its end before the exit code is asked for; the
        // error text is only read on failure, so it is never what git waits on.
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");

        return output;
    }

    /// <summary>
    /// A repository with a local identity, commit signing off and a .gitignore
    /// that keeps build output out of Git Changes. Nothing is committed yet.
    /// </summary>
    private void InitRepository()
    {
        Git("init", "-q");
        Git("config", "user.email", "test@example.com");
        Git("config", "user.name", "Basalt-Test");
        Git("config", "commit.gpgsign", "false");

        // Without it, opening the project makes Roslyn write obj/ and that shows up as an
        // untracked change no journey here is about.
        File.WriteAllText(Path.Combine(_root, ".gitignore"), "bin/\nobj/\n");
    }

    /// <summary>A console project with Program.vb in <paramref name="folder"/>; returns the project file.</summary>
    private static string CreateSolution(string folder)
    {
        Directory.CreateDirectory(folder);

        var project = Path.Combine(folder, "App.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(folder, "Program.vb"), OriginalProgram);

        return project;
    }

    /// <summary>A repository whose one commit holds a console project at its root.</summary>
    private string MakeRepoWithConsoleProject()
    {
        InitRepository();
        var project = CreateSolution(_root);

        Git("add", "-A");
        Git("commit", "-q", "-m", "Initial-commit");

        return project;
    }

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    // ------------------------------------------------------------------
    // Window helpers: the way a person reaches the panels and drives them.
    // ------------------------------------------------------------------

    private static string L(string key) => Localizer.Get(key);

    /// <summary>Chooses a menu entry by its path of localized headers, the way a person does.</summary>
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

    private static async Task WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }

        Assert.True(condition(), because);
    }

    /// <summary>
    /// Opens the solution's repository the way the window does at start-up,
    /// and waits until the view model has found it.
    /// </summary>
    private static async Task<MainWindowViewModel> OpenedRepositoryAsync(TestWindow host)
    {
        await host.SettleAsync(10);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await WaitUntilAsync(() => vm.Repository is not null, "the solution's repository was not detected");

        return vm;
    }

    /// <summary>Shows Git Changes through the Git menu, as a person reaches it.</summary>
    private static async Task<GitChangesPanel> ShowGitChangesAsync(TestWindow host)
    {
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitCommit));
        await host.SettleAsync(5);

        return host.Window.GitChangesForTests!;
    }

    /// <summary>Shows the Branches panel through the Git menu.</summary>
    private static async Task<BranchPanel> ShowBranchesAsync(TestWindow host)
    {
        InvokeMenu(host.Window, L(StringKeys.MenuGit), L(StringKeys.MenuGitBranches));
        await host.SettleAsync(5);

        return host.Window.BranchesForTests!;
    }

    /// <summary>
    /// Opens a file in the editor, types new text into it and saves it with
    /// Save All, which is what a person does after editing.
    /// </summary>
    private static async Task EditAndSaveAsync(
        TestWindow host, MainWindowViewModel vm, string path, string newText)
    {
        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text = newText;
        await host.SettleAsync(3);

        await vm.SaveAllAsync();
    }

    /// <summary>
    /// The two lists of Git Changes in the order they are laid out: the staged
    /// list above the unstaged one.
    /// </summary>
    private static (ListBox Staged, ListBox Unstaged) ChangeListsOf(GitChangesPanel panel)
    {
        var lists = panel.GetVisualDescendants().OfType<ListBox>().ToList();
        Assert.True(lists.Count >= 2, $"found {lists.Count} list boxes");

        return (lists[0], lists[1]);
    }

    /// <summary>The two lists of the Branches panel: local branches above remote ones.</summary>
    private static ListBox LocalListOf(BranchPanel panel) =>
        panel.GetVisualDescendants().OfType<ListBox>().First();

    /// <summary>
    /// What a person does to move a row across: select it, then double-tap it.
    /// Staging and checkout are both double-taps in the panels.
    /// </summary>
    private static void DoubleTap(ListBox list, object row)
    {
        list.SelectedItem = row;
        list.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
    }

    private static void PressButton(Visual root, string label) => ClickButton(
        root, button => Equals(button.Content, label));

    private static void ClickButton(Visual root, Func<Button, bool> choose)
    {
        var button = root.GetVisualDescendants().OfType<Button>().First(choose);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
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

    /// <summary>Whether a panel shows a piece of text anywhere in its visual tree.</summary>
    private static bool Shows(Visual root, string text) =>
        root.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == text);

    /// <summary>
    /// The branch name the status bar shows: its button's last text, after the
    /// branch glyph.
    /// </summary>
    private static string? StatusBarBranch(TestWindow host)
    {
        var button = host.Window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.Name == "BranchButton");

        return button?.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).LastOrDefault();
    }

    // ------------------------------------------------------------------
    // Journey 1: a changed file is listed in Git Changes as modified, and
    // opening its diff shows the removed and the added lines.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task EditingAFileListsItInGitChangesAndItsDiffShowsTheChangedLines()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await EditAndSaveAsync(host, vm, ProgramPath, EditedProgram);

        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the saved file was not listed under Git Changes");

        var row = changes.Unstaged.Single(r => r.Change.Path == "Program.vb");
        Assert.Equal("M", row.Marker);
        Assert.Equal("Program.vb", row.Name);
        Assert.Equal("", row.Folder);

        // Selecting the row is how a person opens its diff.
        ChangeListsOf(changes).Unstaged.SelectedItem = row;

        var diff = host.Window.GitDiffForTests!;
        await WaitUntilAsync(() => diff.Lines.Count > 0, "selecting the file did not show its diff");
        await host.SettleAsync(3);

        var removed = diff.Lines.Single(l => l.Kind == DiffLineKind.Removed && l.Text == "-Module Program");
        Assert.Equal(1, removed.OldLine);

        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "+Module Launcher");
        Assert.Contains(diff.Lines,
            l => l.Kind == DiffLineKind.Added && l.Text == "+        Console.WriteLine(\"Hi\")");
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Context && l.Text.Contains("Sub Main()"));
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Header && l.Text.StartsWith("@@", StringComparison.Ordinal));

        Assert.True(diff.IsEffectivelyVisible, "the diff was not brought to the front");
    }

    // ------------------------------------------------------------------
    // Journey 2: staging moves a file into the staged list, unstaging moves
    // it back, Commit stays off until something is staged and a message is
    // typed, and committing records exactly the staged file with the message.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task StagingUnstagingAndCommittingWithAMessageRecordsExactlyTheStagedFile()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await EditAndSaveAsync(host, vm, ProgramPath, EditedProgram);
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the saved file was not listed under Git Changes");

        // Nothing staged and no message: Commit is not offered.
        Assert.False(changes.CanCommit);

        var (stagedList, unstagedList) = ChangeListsOf(changes);

        DoubleTap(unstagedList, changes.Unstaged.Single(r => r.Change.Path == "Program.vb"));
        await WaitUntilAsync(() => changes.Staged.Any(r => r.Change.Path == "Program.vb"),
            "double-tapping an unstaged file did not stage it");
        Assert.Equal("Program.vb", Git("diff", "--cached", "--name-only").Trim());

        TypeCommitMessage(changes, "Rename the entry point");
        await host.SettleAsync(2);
        Assert.True(changes.CanCommit);

        // Unstaging: the file goes back to Changes and nothing is left in the index.
        DoubleTap(stagedList, changes.Staged.Single(r => r.Change.Path == "Program.vb"));
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "double-tapping a staged file did not unstage it");
        Assert.Equal("", Git("diff", "--cached", "--name-only").Trim());

        // The message survives, but with nothing staged the commit is refused in the panel.
        Assert.Equal("Rename the entry point", changes.CommitMessage);
        Assert.False(changes.CanCommit);

        DoubleTap(unstagedList, changes.Unstaged.Single(r => r.Change.Path == "Program.vb"));
        await WaitUntilAsync(() => changes.Staged.Any(r => r.Change.Path == "Program.vb"),
            "the file could not be staged again");
        await host.SettleAsync(2);
        Assert.True(changes.CanCommit);

        // Commit with the panel's own button.
        PressButton(changes, L(StringKeys.GitCommit));

        await WaitUntilAsync(() => changes.IsShowingEmptyState,
            "the panel still lists changes after the commit");

        Assert.Equal("Rename the entry point", Git("log", "-1", "--format=%s").Trim());
        Assert.Equal("Basalt-Test", Git("log", "-1", "--format=%an").Trim());
        Assert.Equal("Program.vb", Git("show", "--name-only", "--format=", "HEAD").Trim());
        Assert.Equal("", Git("status", "--porcelain=v1").Trim());
        Assert.Equal("", changes.CommitMessage);
    }

    /// <summary>Types into the commit message box, which is the first text box of the panel.</summary>
    private static void TypeCommitMessage(GitChangesPanel changes, string message) =>
        changes.GetVisualDescendants().OfType<TextBox>().First().Text = message;

    // ------------------------------------------------------------------
    // Journey 3: create a branch from the Branches panel, switch to it and
    // back. The branch name in Git Changes, in the status bar and marked in the
    // branch list follows each switch.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CreatingAndSwitchingBranchesMovesTheBranchNameInThePanelsAndTheStatusBar()
    {
        var project = MakeRepoWithConsoleProject();
        var main = Git("branch", "--show-current").Trim();

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);

        var changes = await ShowGitChangesAsync(host);
        await WaitUntilAsync(() => Shows(changes, main), "Git Changes does not name the current branch");
        Assert.Equal(main, StatusBarBranch(host));

        var branches = await ShowBranchesAsync(host);
        await WaitUntilAsync(() => branches.Local.Any(r => r.Branch.Name == main),
            "the Branches panel did not list the current branch");
        await WaitUntilAsync(() => Shows(branches, "● " + main),
            "the Branches panel does not mark the current branch");

        // Type a name and press Create: the new branch is checked out at once.
        branches.GetVisualDescendants().OfType<TextBox>().Single().Text = "feature";
        PressButton(branches, L(StringKeys.BranchCreate));

        await WaitUntilAsync(() => Git("branch", "--show-current").Trim() == "feature",
            "creating the branch did not switch to it");
        await WaitUntilAsync(() => Shows(changes, "feature"), "Git Changes still names the old branch");
        await WaitUntilAsync(() => StatusBarBranch(host) == "feature", "the status bar still names the old branch");
        await WaitUntilAsync(() => Shows(branches, "● feature"), "the Branches panel does not mark the new branch");
        Assert.False(Shows(branches, "● " + main));

        // Back to main: double-tap its row, the same gesture that checks a branch out.
        var mainRow = branches.Local.Single(r => r.Branch.Name == main);
        DoubleTap(LocalListOf(branches), mainRow);

        await WaitUntilAsync(() => Git("branch", "--show-current").Trim() == main,
            "double-tapping the branch did not switch back to it");
        await WaitUntilAsync(() => Shows(changes, main), "Git Changes does not name the branch switched back to");
        await WaitUntilAsync(() => StatusBarBranch(host) == main, "the status bar does not name the branch switched back to");
        await WaitUntilAsync(() => Shows(branches, "● " + main), "the Branches panel does not mark the branch switched back to");
        Assert.False(Shows(branches, "● feature"));
    }

    // ------------------------------------------------------------------
    // Journey 4: discarding a file's change, after confirming it, puts the
    // committed text back in the editor showing that file, which is clean.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task DiscardingAChangeRevertsTheOpenEditorToTheCommittedText()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await EditAndSaveAsync(host, vm, ProgramPath, EditedProgram);
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the saved file was not listed under Git Changes");

        var unstagedList = ChangeListsOf(changes).Unstaged;
        unstagedList.SelectedItem = changes.Unstaged.Single(r => r.Change.Path == "Program.vb");

        ContextMenus.ChooseForTests(changes.UnstagedMenu!, L(StringKeys.GitDiscardChanges));

        var confirm = await WaitForDialogAsync(host);
        Assert.NotNull(confirm);
        ClickButton(confirm!, b => b.Classes.Contains("primary"));
        await host.SettleAsync(5);

        await WaitUntilAsync(() => !changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the discarded file is still listed under Git Changes");
        Assert.Equal(OriginalProgram, await File.ReadAllTextAsync(ProgramPath));

        // The editor showing the file is what a person looks at next: it must
        // show the committed text again, and not be left holding the edit.
        var document = vm.ActiveDocument!;
        Assert.Equal(ProgramPath, document.FilePath);
        Assert.Equal(OriginalProgram, document.Text);
        Assert.False(document.IsModified);
    }

    // ------------------------------------------------------------------
    // Journey 5: in a repository with no commit yet, a staged file goes back
    // to Changes when unstaged. Git refuses "restore --staged" without a HEAD,
    // and the panel must not leave the file staged in silence.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task UnstagingInARepositoryWithNoCommitYetPutsTheFileBackInChanges()
    {
        InitRepository();
        var project = CreateSolution(_root);

        using var host = new TestWindow(project);
        await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the new file was not listed under Git Changes");

        var (stagedList, unstagedList) = ChangeListsOf(changes);

        DoubleTap(unstagedList, changes.Unstaged.Single(r => r.Change.Path == "Program.vb"));
        await WaitUntilAsync(() => changes.Staged.Any(r => r.Change.Path == "Program.vb"),
            "the new file could not be staged");

        DoubleTap(stagedList, changes.Staged.Single(r => r.Change.Path == "Program.vb"));

        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "unstaging the file in a repository with no commit left it staged");
        Assert.DoesNotContain(changes.Staged, r => r.Change.Path == "Program.vb");
        Assert.Equal("", Git("diff", "--cached", "--name-only").Trim());
    }

    // ------------------------------------------------------------------
    // Journey 6: the solution sits in a subfolder of the repository, as
    // many do. Git reports paths from the repository root, so staging and
    // diffing must work from there too.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ASolutionInASubfolderOfTheRepositoryStagesAndDiffsItsFiles()
    {
        InitRepository();
        var project = CreateSolution(Path.Combine(_root, "Sub"));
        Git("add", "-A");
        Git("commit", "-q", "-m", "Initial-commit");

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await EditAndSaveAsync(host, vm, Path.Combine(_root, "Sub", "Program.vb"), EditedProgram);
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Sub/Program.vb"),
            "the saved file was not listed under Git Changes");

        var (_, unstagedList) = ChangeListsOf(changes);
        unstagedList.SelectedItem = changes.Unstaged.Single(r => r.Change.Path == "Sub/Program.vb");

        var diff = host.Window.GitDiffForTests!;
        await WaitUntilAsync(() => diff.Lines.Any(l => l.Kind == DiffLineKind.Added),
            "the diff of a file in a subfolder shows no changed lines");
        Assert.Contains(diff.Lines, l => l.Text == "+Module Launcher");

        DoubleTap(unstagedList, changes.Unstaged.Single(r => r.Change.Path == "Sub/Program.vb"));
        await WaitUntilAsync(() => changes.Staged.Any(r => r.Change.Path == "Sub/Program.vb"),
            "staging a file in a subfolder of the repository did nothing");
        Assert.Equal("Sub/Program.vb", Git("diff", "--cached", "--name-only").Trim());
    }

    // ------------------------------------------------------------------
    // Journey 7: a file moved with a rename is listed once, as renamed. Git
    // writes the old path as a record of its own after the new one, and it
    // must not turn into rows of its own.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task RenamingAFileListsOneRenamedFileAndNothingElse()
    {
        var project = MakeRepoWithConsoleProject();
        Git("mv", "Program.vb", "Launcher.vb");

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        await vm.RefreshRepositoryAsync();

        var changes = await ShowGitChangesAsync(host);
        await WaitUntilAsync(() => changes.Staged.Any(r => r.Change.Kind == FileChangeKind.Renamed),
            "the renamed file was not listed under Git Changes");

        var renamed = Assert.Single(changes.Staged);
        Assert.Equal("Launcher.vb", renamed.Change.Path);
        Assert.Empty(changes.Unstaged);
    }

    // ------------------------------------------------------------------
    // Journey 8: git refuses a stage because another git process holds the
    // index lock. The refusal must reach the output pane, not leave the file
    // where it was with nothing to say why.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AStageGitRefusesIsReportedInTheOutputInsteadOfIgnored()
    {
        var project = MakeRepoWithConsoleProject();

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await EditAndSaveAsync(host, vm, ProgramPath, EditedProgram);
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Program.vb"),
            "the saved file was not listed under Git Changes");

        // A stale or concurrent index lock: git will not write the index.
        var indexLock = Path.Combine(_root, ".git", "index.lock");
        File.WriteAllText(indexLock, "");

        try
        {
            var before = vm.BuildOutput.Count;
            var (_, unstagedList) = ChangeListsOf(changes);
            DoubleTap(unstagedList, changes.Unstaged.Single(r => r.Change.Path == "Program.vb"));

            await WaitUntilAsync(() => vm.BuildOutput.Count > before,
                "a stage git refused left nothing in the output");
            Assert.Contains(vm.BuildOutput.Skip(before),
                line => line.StartsWith("[git]", StringComparison.Ordinal));
            Assert.DoesNotContain(changes.Staged, r => r.Change.Path == "Program.vb");
        }
        finally
        {
            File.Delete(indexLock);
        }
    }

    // ------------------------------------------------------------------
    // Journey 9: a changed line that begins with "--" or "++" is shown as a
    // removed or added line, not mistaken for the "---" and "+++" headers of
    // the file.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ChangedLinesThatBeginWithDashesOrPlusesAreShownAsRemovedAndAdded()
    {
        var project = MakeRepoWithConsoleProject();

        var notes = Path.Combine(_root, "Notes.txt");
        File.WriteAllText(notes, "-- draft note\n++ count\n");
        Git("add", "-A");
        Git("commit", "-q", "-m", "Notes");

        File.WriteAllText(notes, "-- final note\n++ total\n");

        using var host = new TestWindow(project);
        var vm = await OpenedRepositoryAsync(host);
        var changes = await ShowGitChangesAsync(host);

        await vm.RefreshRepositoryAsync();
        await WaitUntilAsync(() => changes.Unstaged.Any(r => r.Change.Path == "Notes.txt"),
            "the edited notes file was not listed under Git Changes");

        ChangeListsOf(changes).Unstaged.SelectedItem =
            changes.Unstaged.Single(r => r.Change.Path == "Notes.txt");

        var diff = host.Window.GitDiffForTests!;
        await WaitUntilAsync(() => diff.Lines.Count > 0, "selecting the notes file did not show its diff");

        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "--- draft note");
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Removed && l.Text == "-++ count");
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "+-- final note");
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "+++ total");
    }
}
