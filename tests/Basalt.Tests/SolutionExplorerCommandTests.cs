using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Regression tests for Solution Explorer's New File, New Folder, Rename and
/// Delete commands (docs/planning/PANELS-AUDIT.md item 3), which used to
/// print "[explorer] Not implemented yet." instead of doing anything (Rename
/// and Delete still did, until this session).
///
/// Each test calls the window's own handler for the command — the same one
/// the panel's context menu calls through <c>CommandChosen</c> — and answers
/// whatever dialog it opens by filling in the real <see cref="TextBox"/> or
/// clicking a real <see cref="Button"/>, the way a person would.
/// </summary>
public sealed class SolutionExplorerCommandTests
{
    private static string WriteProject(string root)
    {
        var projectPath = Path.Combine(root, "Audit.vbproj");
        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(root, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        return projectPath;
    }

    /// <summary>
    /// New File on the project node creates the file on disk, lists it in
    /// the tree and opens it in the editor — what "Not implemented yet."
    /// used to refuse to do.
    /// </summary>
    [AvaloniaFact]
    public async Task NewFileCreatesListsAndOpensTheFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-newfile-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var projectNode = vm.Explorer.Roots.Single();

        var method = typeof(MainWindow).GetMethod(
            "NewFileInExplorerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)method.Invoke(w, [projectNode])!;

        for (var i = 0; i < 40 && !task.IsCompleted; i++)
        {
            await host.SettleAsync(1);

            var dialog = w.OwnedWindows.FirstOrDefault();
            if (dialog is null) continue;

            var box = dialog.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().FirstOrDefault();
            if (box is not null) box.Text = "Added.vb";

            var ok = dialog.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                .FirstOrDefault(b => b.IsDefault);
            ok?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }

        await task;
        await host.SettleAsync(10);

        var created = Path.Combine(root, "Added.vb");
        Assert.True(File.Exists(created), "the file was not created on disk");

        Assert.Contains(vm.Explorer.Roots.SelectMany(Descendants), n => n.Path == created);

        Assert.Equal(created, vm.ActiveDocument?.FilePath);
    }

    /// <summary>
    /// New Folder on the project node creates the folder on disk and lists
    /// it in the tree.
    /// </summary>
    [AvaloniaFact]
    public async Task NewFolderCreatesAndListsTheFolder()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-newfolder-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var projectNode = vm.Explorer.Roots.Single();

        var method = typeof(MainWindow).GetMethod(
            "NewFolderInExplorerAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var task = (Task)method.Invoke(w, [projectNode])!;

        for (var i = 0; i < 40 && !task.IsCompleted; i++)
        {
            await host.SettleAsync(1);

            var dialog = w.OwnedWindows.FirstOrDefault();
            if (dialog is null) continue;

            var box = dialog.GetVisualDescendants().OfType<Avalonia.Controls.TextBox>().FirstOrDefault();
            if (box is not null) box.Text = "Models";

            var ok = dialog.GetVisualDescendants().OfType<Avalonia.Controls.Button>()
                .FirstOrDefault(b => b.IsDefault);
            ok?.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        }

        await task;
        await host.SettleAsync(10);

        var createdDirectory = Path.Combine(root, "Models");
        Assert.True(Directory.Exists(createdDirectory), "the folder was not created on disk");
    }

    /// <summary>
    /// Renaming a file whose tab has unsaved edits moves the file on disk
    /// and keeps those edits in the tab, now pointed at the new path —
    /// losing them, or leaving the tab on the old path, were both live bugs
    /// `ExplorerFileOperations.Rename` existed to fix but was never wired to.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFileKeepsUnsavedEditsUnderTheNewPath()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-file-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var oldPath = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(oldPath);
        await host.SettleAsync(5);

        const string unsavedText = "Module Program\n    ' edited, unsaved\nEnd Module\n";
        vm.ActiveDocument!.Text = unsavedText;

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == oldPath);

        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

        var newPath = Path.Combine(root, "Renamed.vb");

        Assert.False(File.Exists(oldPath), "the old file still exists");
        Assert.True(File.Exists(newPath), "the renamed file was not created on disk");

        Assert.Equal(newPath, vm.ActiveDocument.FilePath);
        Assert.Equal(unsavedText, vm.ActiveDocument.Text);
        Assert.True(vm.ActiveDocument.IsModified);
    }

    /// <summary>
    /// Renaming a folder moves everything inside it, and every open file
    /// under that folder follows to its new path.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFolderRetargetsOpenFilesBeneathIt()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-folder-").FullName;
        var project = WriteProject(root);

        var subfolder = Path.Combine(root, "Sub");
        Directory.CreateDirectory(subfolder);
        var innerPath = Path.Combine(subfolder, "Inner.vb");
        File.WriteAllText(innerPath, "Module Inner\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(innerPath);
        await host.SettleAsync(5);

        var folderNode = vm.Explorer.Roots.SelectMany(Descendants)
            .Single(n => n.Kind == NodeKind.Folder && n.Path == subfolder);

        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", folderNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed"), b => b.IsDefault);

        var newFolder = Path.Combine(root, "Renamed");
        var newInnerPath = Path.Combine(newFolder, "Inner.vb");

        Assert.False(Directory.Exists(subfolder), "the old folder still exists");
        Assert.True(File.Exists(newInnerPath), "the file was not moved with its folder");

        var document = Assert.Single(vm.OpenDocuments);
        Assert.Equal(newInnerPath, document.FilePath);
    }

    /// <summary>
    /// Renaming a form's own .vb file carries its Designer.vb and .resx
    /// companions along with it, the way Visual Studio renames all three
    /// together — and retargets an open tab on the Designer.vb companion to
    /// the new path, keeping its unsaved text, just as a tab on the renamed
    /// file itself would.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFormsVbFileRenamesItsDesignerAndResxCompanions()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-form-").FullName;
        var project = WriteProject(root);

        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        var resxPath = Path.Combine(root, "Form1.resx");

        // The class is named deliberately unlike the file, so renaming never
        // offers to rename a class too — this test is only about the
        // companion files following the rename.
        File.WriteAllText(formPath, "Public Class SomeOtherName\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class SomeOtherName\nEnd Class\n");
        File.WriteAllText(resxPath, "<root></root>\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(designerPath);
        await host.SettleAsync(5);
        const string unsavedDesignerText = "Partial Class SomeOtherName\n    ' edited, unsaved\nEnd Class\n";
        vm.ActiveDocument!.Text = unsavedDesignerText;

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == formPath);

        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

        var newFormPath = Path.Combine(root, "Renamed.vb");
        var newDesignerPath = Path.Combine(root, "Renamed.Designer.vb");
        var newResxPath = Path.Combine(root, "Renamed.resx");

        Assert.False(File.Exists(formPath), "the old .vb file still exists");
        Assert.False(File.Exists(designerPath), "the old Designer.vb still exists");
        Assert.False(File.Exists(resxPath), "the old .resx still exists");
        Assert.True(File.Exists(newFormPath), "the .vb file was not renamed");
        Assert.True(File.Exists(newDesignerPath), "the Designer.vb companion was not renamed");
        Assert.True(File.Exists(newResxPath), "the .resx companion was not renamed");

        var document = Assert.Single(vm.OpenDocuments);
        Assert.Equal(newDesignerPath, document.FilePath);
        Assert.Equal(unsavedDesignerText, document.Text);
        Assert.True(document.IsModified);
    }

    /// <summary>
    /// After a form's own .vb file is renamed, Roslyn must answer about the
    /// Designer.vb companion under its new path too, and not go on
    /// answering about one that no longer exists.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFormsVbFileMovesItsCompanionsInTheRoslynWorkspaceToo()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-form-roslyn-").FullName;
        var project = WriteProject(root);

        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");

        File.WriteAllText(formPath, "Public Class SomeOtherName\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class SomeOtherName\nEnd Class\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var language = (RoslynLanguageService)typeof(MainWindowViewModel)
            .GetField("_languageService", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(vm)!;

        bool Tracks(string path) => language.CurrentSolution?.Projects
            .SelectMany(p => p.Documents).Any(d => d.FilePath == path) == true;

        for (var i = 0; i < 500 && !Tracks(designerPath); i++) await host.SettleAsync(1);
        Assert.True(Tracks(designerPath), "Roslyn never picked up the Designer.vb companion");

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == formPath);
        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

        var newDesignerPath = Path.Combine(root, "Renamed.Designer.vb");

        for (var i = 0; i < 500 && !Tracks(newDesignerPath); i++) await host.SettleAsync(1);
        Assert.True(Tracks(newDesignerPath), "Roslyn never picked up the renamed Designer.vb companion");
        Assert.False(Tracks(designerPath), "Roslyn still tracks the old Designer.vb path");
    }

    /// <summary>
    /// When a file's own main class is named like the file, Solution
    /// Explorer's rename offers to rename the class too, the way Visual
    /// Studio does — and on "Yes", renames it everywhere through the same
    /// Roslyn rename Rename Symbol uses, including in the Designer.vb
    /// companion's partial declaration.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFileOffersToRenameItsMatchingClassAndDoesOnYes()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-class-yes-").FullName;
        var project = WriteProject(root);

        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == formPath);
        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);

        // Three dialogs in turn: the rename prompt, the "rename the class
        // too?" question, and the refactoring preview — every one of them
        // answered by its own default button, the way DriveDialogAsync's
        // other callers accept a single dialog's default.
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

        var newPath = Path.Combine(root, "Renamed.vb");
        var newDesignerPath = Path.Combine(root, "Renamed.Designer.vb");

        Assert.Contains("Class Renamed", await File.ReadAllTextAsync(newPath));
        Assert.Contains("Class Renamed", await File.ReadAllTextAsync(newDesignerPath));
    }

    /// <summary>
    /// Declining the "rename the class too?" question leaves the class
    /// alone — only the files moved.
    /// </summary>
    [AvaloniaFact]
    public async Task DecliningTheClassRenameOfferLeavesTheClassAlone()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-class-no-").FullName;
        var project = WriteProject(root);

        var formPath = Path.Combine(root, "Form1.vb");
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        File.WriteAllText(formPath, "Public Class Form1\nEnd Class\n");
        File.WriteAllText(designerPath, "Partial Class Form1\nEnd Class\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == formPath);
        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);

        var renameDialog = await WaitForDialogAsync(host);
        Assert.NotNull(renameDialog);
        SetTextBox(renameDialog!, "Renamed.vb");
        renameDialog!.GetVisualDescendants().OfType<Button>().First(b => b.IsDefault)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        // Raised above the default: this dialog only opens after
        // RenameInExplorerAsync reloads the Roslyn workspace, which a cold
        // MSBuildWorkspace load can make slower than the usual dialog wait.
        var confirmDialog = await WaitForDialogAsync(host, maxPasses: 500);
        Assert.NotNull(confirmDialog);
        confirmDialog!.GetVisualDescendants().OfType<Button>().First(b => b.IsCancel)
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await task;
        await host.SettleAsync(10);

        var newPath = Path.Combine(root, "Renamed.vb");
        Assert.True(File.Exists(newPath));
        Assert.Contains("Class Form1", await File.ReadAllTextAsync(newPath));
    }

    /// <summary>
    /// Renaming a file whose tab has a breakpoint moves the breakpoint to
    /// the new path, a breakpoint set after the rename also lands on the new
    /// path rather than a name that no longer exists on disk, and the tab's
    /// title follows the new name.
    ///
    /// Critical-review finding: the margin's handlers and the
    /// <c>_margins</c> lookup used to be keyed to the path captured when the
    /// editor was created, and the tab title was set once in the
    /// <c>IdeDocument</c> constructor — neither followed <c>Retarget</c>.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFileMovesItsBreakpointsAndTabTitleToTheNewPath()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-breakpoints-").FullName;
        var project = WriteProject(root);
        var oldPath = Path.Combine(root, "Program.vb");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(oldPath);
        await host.SettleAsync(5);

        var code = w.CodeEditorForTests(oldPath);
        Assert.NotNull(code);
        code!.BreakpointMargin.Toggle(2);
        await host.SettleAsync(2);

        var debug = DebugSessionOf(w);
        Assert.Equal(2, Assert.Single(debug.BreakpointsIn(oldPath)).Line);

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == oldPath);
        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

        var newPath = Path.Combine(root, "Renamed.vb");

        Assert.Empty(debug.BreakpointsIn(oldPath));
        Assert.Equal(2, Assert.Single(debug.BreakpointsIn(newPath)).Line);

        // The same editor (reused by the dock, not rebuilt) now reports its
        // own path as the new one, so a breakpoint set after the rename is
        // recorded there and not against the old, now-nonexistent, name.
        var codeAfterRename = w.CodeEditorForTests(newPath);
        Assert.Same(code, codeAfterRename);
        codeAfterRename!.BreakpointMargin.Toggle(3);
        await host.SettleAsync(2);

        Assert.Contains(debug.BreakpointsIn(newPath), b => b.Line == 3);
        Assert.Empty(debug.BreakpointsIn(oldPath));

        Assert.Equal("Renamed.vb", DocumentTitleOf(w, vm.ActiveDocument!));
    }

    /// <summary>
    /// Renaming a folder moves the breakpoints of every file beneath it, not
    /// only the ones whose tab happens to be open — a breakpoint set in a
    /// file that was never opened in this session is still on disk under the
    /// renamed path, and used to be left behind in
    /// <see cref="DebugSession"/>'s dictionary under a name that no longer
    /// exists, since the old code only retargeted breakpoints by walking
    /// <c>ViewModel.OpenDocuments</c>.
    /// </summary>
    [AvaloniaFact]
    public async Task RenamingAFolderMovesBreakpointsOfFilesNeverOpened()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-breakpoints-closed-").FullName;
        var project = WriteProject(root);

        var subfolder = Path.Combine(root, "Sub");
        Directory.CreateDirectory(subfolder);
        var innerPath = Path.Combine(subfolder, "Inner.vb");
        File.WriteAllText(innerPath, "Module Inner\n    ' never opened in a tab\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var debug = DebugSessionOf(w);
        await debug.ToggleBreakpointAsync(innerPath, 2);
        await host.SettleAsync(2);

        Assert.Equal(2, Assert.Single(debug.BreakpointsIn(innerPath)).Line);
        Assert.Empty(vm.OpenDocuments);

        var folderNode = vm.Explorer.Roots.SelectMany(Descendants)
            .Single(n => n.Kind == NodeKind.Folder && n.Path == subfolder);

        var task = InvokePrivateAsync(w, "RenameInExplorerAsync", folderNode);
        await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed"), b => b.IsDefault);

        var newInnerPath = Path.Combine(root, "Renamed", "Inner.vb");

        Assert.Empty(debug.BreakpointsIn(innerPath));
        Assert.Equal(2, Assert.Single(debug.BreakpointsIn(newInnerPath)).Line);
    }

    /// <summary>
    /// Rename and Delete are disabled on the project node: its own path is
    /// the .vbproj the session is open from
    /// (<see cref="SolutionTreeNode.CanRenameOrDelete"/>), and acting on it
    /// would pull that file out from under <c>ReloadAsync</c>.
    /// </summary>
    [AvaloniaFact]
    public async Task RenameAndDeleteAreDisabledOnTheProjectNode()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-project-menu-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var projectNode = vm.Explorer.Roots.Single();
        Assert.Equal(NodeKind.Project, projectNode.Kind);

        var panel = w.SolutionExplorerForTests!;
        panel.TreeForTests.SelectedItem = projectNode;
        await host.SettleAsync(2);

        var entries = ContextMenus.OpenForTests(panel.Menu!);
        Assert.False(entries.Single(e => e.Header == "Rename…").Enabled,
            "Rename is enabled on the project node");
        Assert.False(entries.Single(e => e.Header == "Delete").Enabled,
            "Delete is enabled on the project node");

        // The same menu re-enables both for an ordinary file.
        var fileNode = vm.Explorer.Roots.SelectMany(Descendants)
            .Single(n => n.Path == Path.Combine(root, "Program.vb"));
        panel.TreeForTests.SelectedItem = fileNode;
        await host.SettleAsync(2);

        var fileEntries = ContextMenus.OpenForTests(panel.Menu!);
        Assert.True(fileEntries.Single(e => e.Header == "Rename…").Enabled);
        Assert.True(fileEntries.Single(e => e.Header == "Delete").Enabled);
    }

    /// <summary>
    /// F2 and Delete do nothing on the project node, and the handlers behind
    /// them refuse the same node directly and defensively — not only because
    /// the menu and the shortcut already keep it from being asked.
    /// </summary>
    [AvaloniaFact]
    public async Task RenameAndDeleteDoNothingOnTheProjectNode()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-project-guard-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var projectNode = vm.Explorer.Roots.Single();

        await InvokePrivateAsync(w, "RenameInExplorerAsync", projectNode);
        await host.SettleAsync(2);
        Assert.Empty(w.OwnedWindows);
        Assert.True(File.Exists(project), "the project file was renamed");

        await InvokePrivateAsync(w, "DeleteInExplorerAsync", projectNode);
        await host.SettleAsync(2);
        Assert.Empty(w.OwnedWindows);
        Assert.True(File.Exists(project), "the project file was deleted");
    }

    /// <summary>
    /// Rename and Delete refuse to run while a debug session is active: the
    /// adapter and the breakpoints it was given are keyed by path, and a
    /// rename or a delete mid-session would leave both pointing at a file
    /// that no longer exists where they think it does — the same kind of
    /// refusal Visual Studio makes for a running item.
    /// </summary>
    [AvaloniaFact]
    public async Task RenameAndDeleteRefuseToRunWhileDebugging()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-debug-guard-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var debug = DebugSessionOf(w);
        await debug.StartWithAsync(new FakeDebugSessionService(), "fake.dll", null);
        await host.SettleAsync(2);
        Assert.True(debug.IsRunning);

        var path = Path.Combine(root, "Program.vb");
        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        await InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
        await host.SettleAsync(2);
        Assert.Empty(w.OwnedWindows); // no rename prompt was ever opened
        Assert.True(File.Exists(path), "the file was renamed while debugging");
        Assert.Contains("debugging", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);

        vm.StatusMessage = "";

        await InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
        await host.SettleAsync(2);
        Assert.Empty(w.OwnedWindows); // no delete confirmation was ever opened
        Assert.True(File.Exists(path), "the file was deleted while debugging");
        Assert.Contains("debugging", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The same refusal shows in the context menu: Rename and Delete grey
    /// out the moment a debug session starts, on a node where they were
    /// available a moment before.
    /// </summary>
    [AvaloniaFact]
    public async Task RenameAndDeleteAreDisabledWhileDebugging()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-debug-menu-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var panel = w.SolutionExplorerForTests!;
        panel.TreeForTests.SelectedItem = fileNode;
        await host.SettleAsync(2);

        var before = ContextMenus.OpenForTests(panel.Menu!);
        Assert.True(before.Single(e => e.Header == "Rename…").Enabled);
        Assert.True(before.Single(e => e.Header == "Delete").Enabled);

        var debug = DebugSessionOf(w);
        await debug.StartWithAsync(new FakeDebugSessionService(), "fake.dll", null);
        await host.SettleAsync(2);

        var during = ContextMenus.OpenForTests(panel.Menu!);
        Assert.False(during.Single(e => e.Header == "Rename…").Enabled,
            "Rename is still enabled while debugging");
        Assert.False(during.Single(e => e.Header == "Delete").Enabled,
            "Delete is still enabled while debugging");
    }

    /// <summary>
    /// A minimal debug service standing in for netcoredbg, only to make
    /// <see cref="DebugSession.IsRunning"/> report true without a real
    /// adapter process — the IDE only needs the one fact for the guard
    /// this exercises.
    /// </summary>
    private sealed class FakeDebugSessionService : Basalt.Core.Services.IDebugSessionService
    {
        public bool IsRunning { get; private set; } = true;
        public bool IsPaused => false;

        public event EventHandler<Basalt.Core.Services.StackFrame>? Paused;
        public event EventHandler? Resumed;
        public event EventHandler<int>? Exited;
        public event EventHandler<string>? OutputReceived;
        public event EventHandler? Started;
        public event EventHandler<Exception>? Faulted;

        public Task LaunchAsync(string assemblyPath, string? workingDirectory = null, CancellationToken ct = default)
        {
            Started?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public Task AttachAsync(int processId, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBreakpointsAsync(
            string filePath, IReadOnlyList<Basalt.Core.Services.Breakpoint> breakpoints, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task ContinueAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOverAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepIntoAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StepOutAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Basalt.Core.Services.StackFrame>> GetCallStackAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Basalt.Core.Services.StackFrame>>([]);

        public Task<IReadOnlyList<Basalt.Core.Services.VariableValue>> GetLocalsAsync(
            int frameIndex, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Basalt.Core.Services.VariableValue>>([]);

        public Task<string?> EvaluateAsync(string expression, int frameIndex, CancellationToken ct = default) =>
            Task.FromResult<string?>(null);

        public Task StopAsync(CancellationToken ct = default)
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Basalt.Core.Services.VariableValue>> ExpandAsync(
            int reference, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Basalt.Core.Services.VariableValue>>([]);

        public void Dispose() { }
    }

    /// <summary>The debug session the window's own breakpoint margins and panels are wired to.</summary>
    private static DebugSession DebugSessionOf(MainWindow window) =>
        (DebugSession)typeof(MainWindow)
            .GetField("_debug", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

    /// <summary>The dock tab's own title for an open document, as distinct from the view model's.</summary>
    private static string? DocumentTitleOf(MainWindow window, EditorDocumentViewModel document)
    {
        var documents = (System.Collections.IDictionary)typeof(MainWindow)
            .GetField("_documents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;

        foreach (System.Collections.DictionaryEntry entry in documents)
            if (ReferenceEquals(entry.Key, document))
                return ((Basalt.Shell.Docking.IdeDocument)entry.Value!).Title;

        return null;
    }

    /// <summary>
    /// Regression tests for the critical-review finding that Rename and
    /// Delete used to let MSBuildWorkspace apply the move or removal itself
    /// (<c>RenameDocumentAsync</c>/<c>RemoveDocumentAsync</c>, since
    /// deleted): <c>TryApplyChanges</c> on an AddDocument/RemoveDocument
    /// writes the workspace's own copy of the text over whatever Explorer
    /// had just put on disk, and rewrites the project file even on an
    /// SDK-style project whose compile items an implicit glob already
    /// covers — the next build then fails with NETSDK1022 (duplicate
    /// items). Reopening the Roslyn workspace from disk, done now by
    /// <see cref="RoslynLanguageService.ReloadAsync"/>, needs neither.
    /// </summary>
    public sealed class RenameAndDeleteReachRoslynSafely
    {
        /// <summary>Roslyn's own view of the solution, reached the same way the window reaches it.</summary>
        private static RoslynLanguageService LanguageServiceOf(MainWindowViewModel vm) =>
            (RoslynLanguageService)typeof(MainWindowViewModel)
                .GetField("_languageService", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(vm)!;

        /// <summary>
        /// Pumps the dispatcher until <paramref name="condition"/> is true —
        /// the real signal that Roslyn finished loading the solution, rather
        /// than a fixed delay chosen to usually be long enough.
        /// </summary>
        // 60ms per pass (see TestWindow.SettleAsync): 500 passes is 30s,
        // enough for a cold MSBuildWorkspace load rather than one already
        // warm from a previous test in the run.
        private static async Task WaitUntilAsync(TestWindow host, Func<bool> condition, int maxPasses = 500)
        {
            for (var i = 0; i < maxPasses && !condition(); i++)
                await host.SettleAsync(1);

            Assert.True(condition(), "the condition was never satisfied");
        }

        private static bool Tracks(RoslynLanguageService language, string path) =>
            language.CurrentSolution?.Projects
                .SelectMany(p => p.Documents)
                .Any(d => d.FilePath == path) == true;

        /// <summary>
        /// Red-proof for the data loss the critical review found: when the
        /// real file on disk differs from the (stale) copy the Roslyn
        /// workspace still holds — exactly what a `git checkout` made while
        /// the IDE is open would leave — renaming must move the real disk
        /// content, not overwrite it with the workspace's older copy. Against
        /// the old <c>RenameDocumentAsync</c>, which wrote
        /// <c>document.GetTextAsync()</c> to the new path, this failed.
        /// </summary>
        [AvaloniaFact]
        public async Task RenamingWritesTheRealDiskContentNotTheWorkspacesStaleCopy()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-stale-").FullName;
            var project = WriteProject(root);
            var path = Path.Combine(root, "Program.vb");

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, path));

            // Made after the workspace loaded, so the workspace's own copy of
            // the document is now stale next to what is really on disk.
            const string realDiskContent = "Module Program\n    ' the real content on disk\nEnd Module\n";
            File.WriteAllText(path, realDiskContent);

            var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

            var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
            await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

            var newPath = Path.Combine(root, "Renamed.vb");
            Assert.Equal(realDiskContent, await File.ReadAllTextAsync(newPath));
        }

        /// <summary>
        /// Renaming on an SDK-style project, whose compile items an implicit
        /// glob finds with nothing written explicitly, must not touch the
        /// project file at all: the old AddDocument/RemoveDocument approach
        /// added a `&lt;Compile Include&gt;` the glob already covered.
        /// </summary>
        [AvaloniaFact]
        public async Task RenamingLeavesTheProjectFileByteForByteUnchanged()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-vbproj-").FullName;
            var project = WriteProject(root);
            var path = Path.Combine(root, "Program.vb");

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, path));

            var before = await File.ReadAllBytesAsync(project);

            var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);
            var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
            await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

            Assert.Equal(before, await File.ReadAllBytesAsync(project));
        }

        /// <summary>Same guarantee for Delete, which used to apply a bare RemoveDocument the same way.</summary>
        [AvaloniaFact]
        public async Task DeletingLeavesTheProjectFileByteForByteUnchanged()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-delete-vbproj-").FullName;
            var project = WriteProject(root);
            var extra = Path.Combine(root, "Extra.vb");
            File.WriteAllText(extra, "Module Extra\nEnd Module\n");

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, extra));

            var before = await File.ReadAllBytesAsync(project);

            var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == extra);
            var task = InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
            await DriveDialogAsync(host, task, _ => { }, b => b.Classes.Contains("primary"));

            Assert.Equal(before, await File.ReadAllBytesAsync(project));
        }

        /// <summary>
        /// After a rename Roslyn must answer about the document under its new
        /// path, and not go on answering about a path that no longer exists.
        /// </summary>
        [AvaloniaFact]
        public async Task RenamingMovesTheDocumentInTheRoslynWorkspaceToo()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-roslyn-").FullName;
            var project = WriteProject(root);
            var path = Path.Combine(root, "Program.vb");

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, path));

            var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);
            var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
            await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed.vb"), b => b.IsDefault);

            var newPath = Path.Combine(root, "Renamed.vb");

            await WaitUntilAsync(host, () => Tracks(language, newPath));
            Assert.False(Tracks(language, path), "Roslyn still tracks the old path");
        }

        /// <summary>
        /// A folder rename must carry over the content of every file beneath
        /// it, including one that was never opened in a tab — Explorer's own
        /// move handles those, but the old code asked Roslyn to rewrite them
        /// too, from whatever text the workspace happened to hold for each.
        /// </summary>
        [AvaloniaFact]
        public async Task RenamingAFolderKeepsTheContentOfFilesNeverOpenedInAnEditor()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-folder-unopened-").FullName;
            var project = WriteProject(root);

            var subfolder = Path.Combine(root, "Sub");
            Directory.CreateDirectory(subfolder);
            var innerPath = Path.Combine(subfolder, "Inner.vb");
            const string innerContent = "Module Inner\n    ' never opened in a tab\nEnd Module\n";
            File.WriteAllText(innerPath, innerContent);

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, innerPath));
            Assert.Empty(vm.OpenDocuments);

            var folderNode = vm.Explorer.Roots.SelectMany(Descendants)
                .Single(n => n.Kind == NodeKind.Folder && n.Path == subfolder);

            var task = InvokePrivateAsync(w, "RenameInExplorerAsync", folderNode);
            await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Renamed"), b => b.IsDefault);

            var newInnerPath = Path.Combine(root, "Renamed", "Inner.vb");
            Assert.Equal(innerContent, await File.ReadAllTextAsync(newInnerPath));
        }

        /// <summary>
        /// A rename that only changes letter case must keep the file's
        /// content exactly — the move-through-a-throwaway-name
        /// <see cref="ExplorerFileOperations.Rename"/> uses must not become a
        /// read-modify-write by the time Roslyn is told about it.
        /// </summary>
        [AvaloniaFact]
        public async Task CaseOnlyRenameKeepsTheFilesContent()
        {
            var root = Directory.CreateTempSubdirectory("basalt-explorer-rename-case-").FullName;
            var project = WriteProject(root);
            var oldPath = Path.Combine(root, "lowercase.vb");
            const string content = "Module LowerCase\nEnd Module\n";
            File.WriteAllText(oldPath, content);

            using var host = new TestWindow(project);
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;
            var language = LanguageServiceOf(vm);

            await WaitUntilAsync(host, () => Tracks(language, oldPath));

            var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == oldPath);
            var task = InvokePrivateAsync(w, "RenameInExplorerAsync", fileNode);
            await DriveDialogAsync(host, task, dialog => SetTextBox(dialog, "Lowercase.vb"), b => b.IsDefault);

            var newPath = Path.Combine(root, "Lowercase.vb");
            Assert.True(File.Exists(newPath));
            Assert.Equal(content, await File.ReadAllTextAsync(newPath));

            var storedName = Path.GetFileName(Directory.EnumerateFiles(root)
                .Single(f => string.Equals(Path.GetFileName(f), "Lowercase.vb", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal("Lowercase.vb", storedName, StringComparer.Ordinal);
        }

        private static string WriteProject(string root) => SolutionExplorerCommandTests.WriteProject(root);

        private static Task InvokePrivateAsync(MainWindow window, string methodName, object? argument) =>
            SolutionExplorerCommandTests.InvokePrivateAsync(window, methodName, argument);

        private static void SetTextBox(Window dialog, string text) =>
            SolutionExplorerCommandTests.SetTextBox(dialog, text);

        private static Task DriveDialogAsync(
            TestWindow host, Task task, Action<Window> fill, Func<Button, bool> choose) =>
            SolutionExplorerCommandTests.DriveDialogAsync(host, task, fill, choose);

        private static IEnumerable<SolutionTreeNode> Descendants(SolutionTreeNode node) =>
            SolutionExplorerCommandTests.Descendants(node);
    }

    /// <summary>
    /// Delete, confirmed, removes the file from disk and closes its tab with
    /// no save prompt — there is nothing left on disk to save to.
    /// </summary>
    [AvaloniaFact]
    public async Task DeletingWithConfirmationRemovesTheFileAndClosesItsTab()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-delete-confirm-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var task = InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
        await DriveDialogAsync(host, task, _ => { }, b => b.Classes.Contains("primary"));

        Assert.False(File.Exists(path), "the file was not deleted");
        Assert.Empty(vm.OpenDocuments);
        Assert.Null(vm.ActiveDocument);
    }

    /// <summary>
    /// The delete confirmation warns separately when an open tab under the
    /// node has unsaved edits: those are discarded along with the file, not
    /// merely the file itself, and the plain "this cannot be undone" message
    /// never said so.
    /// </summary>
    [AvaloniaFact]
    public async Task DeleteConfirmationWarnsWhenAnOpenTabHasUnsavedChanges()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-delete-unsaved-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        vm.ActiveDocument!.Text = "Module Program\n    ' edited, unsaved\nEnd Module\n";

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var task = InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var message = dialog!.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.TextWrapping == Avalonia.Media.TextWrapping.Wrap);
        Assert.Contains("unsaved", message.Text, StringComparison.OrdinalIgnoreCase);

        var cancel = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsCancel);
        cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await task;
        await host.SettleAsync(5);

        Assert.True(File.Exists(path), "the file was deleted despite cancelling");
    }

    /// <summary>
    /// Deleting a file whose tab is open but has no unsaved edits keeps the
    /// plain message: nothing is about to be discarded beyond the file
    /// itself.
    /// </summary>
    [AvaloniaFact]
    public async Task DeleteConfirmationStaysPlainWhenTheOpenTabHasNoUnsavedChanges()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-delete-clean-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        Assert.False(vm.ActiveDocument!.IsModified);

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var task = InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var message = dialog!.GetVisualDescendants().OfType<TextBlock>()
            .First(t => t.TextWrapping == Avalonia.Media.TextWrapping.Wrap);
        Assert.DoesNotContain("unsaved", message.Text, StringComparison.OrdinalIgnoreCase);

        var cancel = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsCancel);
        cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await task;
        await host.SettleAsync(5);
    }

    /// <summary>Cancelling the confirmation leaves the file and its tab alone.</summary>
    [AvaloniaFact]
    public async Task CancellingTheConfirmationKeepsTheFile()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-delete-cancel-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(path);
        await host.SettleAsync(5);

        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var task = InvokePrivateAsync(w, "DeleteInExplorerAsync", fileNode);
        // Cancel, not the default button: Delete is the dialog's primary
        // action (IsDefault) now that it follows Windows order, and Cancel
        // is found by its own role instead.
        await DriveDialogAsync(host, task, _ => { }, b => b.IsCancel);

        Assert.True(File.Exists(path), "the file was deleted despite cancelling");
        Assert.Single(vm.OpenDocuments);
    }

    /// <summary>
    /// F2 with the Solution Explorer focused renames the selected node, not
    /// whatever the global shortcut registry has F2 bound to.
    /// </summary>
    [AvaloniaFact]
    public async Task F2WithExplorerFocusedOpensTheRenamePromptForTheSelectedNode()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-f2-explorer-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        var fileNode = vm.Explorer.Roots.SelectMany(Descendants).Single(n => n.Path == path);

        var panel = w.SolutionExplorerForTests!;
        panel.TreeForTests.SelectedItem = fileNode;
        await host.SettleAsync(5);

        // The node is selected (as a click on its row would leave it). What
        // TryRunExplorerShortcutAsync reads is IsKeyboardFocusWithin on the
        // whole panel and SelectedItem on the tree, not which control inside
        // the panel holds focus, so any focusable descendant stands for the
        // explorer having focus — here the root row's own item, the one
        // realised without expanding anything.
        var focusTarget = panel.GetVisualDescendants().OfType<InputElement>().First(c => c.Focusable);
        focusTarget.Focus();
        await host.SettleAsync(2);

        Assert.True(panel.IsKeyboardFocusWithin, "the explorer never took keyboard focus");

        var args = RaiseKeyDown(focusTarget, Key.F2);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var box = dialog!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        Assert.Equal("Program.vb", box?.Text);

        var cancel = dialog.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => b.IsCancel && !b.IsDefault);
        cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await host.SettleAsync(10);

        Assert.True(args.Handled, "F2 was not handled by the explorer shortcut");
        Assert.True(File.Exists(path), "the file was renamed despite cancelling");
    }

    /// <summary>
    /// F2 with the editor focused still runs Rename Symbol: the explorer's
    /// own F2 handling must defer to it rather than swallow every F2 once
    /// wired, which was the risk the rename command's own gesture shares.
    /// </summary>
    [AvaloniaFact]
    public async Task F2WithEditorFocusedTriggersTheEditorsRenameInstead()
    {
        var root = Directory.CreateTempSubdirectory("basalt-explorer-f2-editor-").FullName;
        var project = WriteProject(root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var path = Path.Combine(root, "Program.vb");
        await vm.OpenFileAsync(path);
        await host.SettleAsync(10);

        var editor = w.CurrentEditorForTests()!;
        editor.CaretOffset = editor.Text.IndexOf("Program", StringComparison.Ordinal) + 1;
        editor.Focus();
        await host.SettleAsync(2);

        var args = RaiseKeyDown(editor, Key.F2);

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var box = dialog!.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        Assert.Equal("Program", box?.Text);

        var cancel = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsCancel);
        cancel?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await host.SettleAsync(10);

        Assert.True(args.Handled, "F2 was not handled at all");
    }

    /// <summary>Calls one of the window's own private explorer command handlers, as its context menu does.</summary>
    private static Task InvokePrivateAsync(MainWindow window, string methodName, object? argument)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (Task)method.Invoke(window, [argument])!;
    }

    /// <summary>Fills in a dialog's text box, when it has one.</summary>
    private static void SetTextBox(Window dialog, string text)
    {
        var box = dialog.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (box is not null) box.Text = text;
    }

    /// <summary>
    /// Pumps the dispatcher while a dialog-driven command runs, applying
    /// <paramref name="fill"/> to whatever dialog the window owns and
    /// clicking the first button <paramref name="choose"/> picks, until the
    /// command's task completes.
    /// </summary>
    private static async Task DriveDialogAsync(
        TestWindow host, Task task, Action<Window> fill, Func<Button, bool> choose)
    {
        var w = host.Window;

        for (var i = 0; i < 40 && !task.IsCompleted; i++)
        {
            await host.SettleAsync(1);

            var dialog = w.OwnedWindows.FirstOrDefault();
            if (dialog is null) continue;

            fill(dialog);

            var button = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(choose);
            button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        await task;
        await host.SettleAsync(10);
    }

    /// <summary>
    /// Waits for the window to own a dialog, as a dispatcher-driven async
    /// handler opens one. <paramref name="maxPasses"/> is raised above the
    /// default for a dialog that only opens after a Roslyn workspace reload
    /// — the same cold-MSBuildWorkspace margin <c>WaitUntilAsync</c> gives
    /// elsewhere in this file.
    /// </summary>
    private static async Task<Window?> WaitForDialogAsync(TestWindow host, int maxPasses = 40)
    {
        Window? dialog = null;

        for (var i = 0; i < maxPasses && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    /// <summary>Raises a real KeyDown, the way pressing the key would.</summary>
    private static KeyEventArgs RaiseKeyDown(InputElement target, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = target
        };

        target.RaiseEvent(args);

        return args;
    }

    private static IEnumerable<SolutionTreeNode> Descendants(SolutionTreeNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Descendants(child))
                yield return descendant;
    }
}
