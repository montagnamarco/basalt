using Avalonia;
using Avalonia.Threading;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Files in Solution Explorer, driven the way a person at the IDE drives them:
/// a real <see cref="MainWindow"/>, a real VB console project in a temporary
/// folder, the tree's context menu, and the dialogs answered through their own
/// controls. Covers folders and new classes, renaming an open dirty file,
/// renaming a folder with an open file in it, deleting an open file, and the
/// tree following files written or removed outside the IDE.
///
/// Settings-touching tests share <see cref="SettingsFileCollection"/> because
/// BASALT_SETTINGS is process-wide.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneySolutionExplorerFilesTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-files-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-files-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneySolutionExplorerFilesTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ---- helpers ------------------------------------------------------

    private const string Helper = "Module Helper\nEnd Module\n";

    private static MainWindowViewModel ViewModelOf(TestWindow host) =>
        (MainWindowViewModel)host.Window.DataContext!;

    private static SolutionTreeNode NodeAt(MainWindowViewModel vm, string path) =>
        UserJourneyItemsTests.Flatten(vm.Explorer.Roots).Single(n => n.Path == path);

    private static bool HasNode(MainWindowViewModel vm, string path) =>
        UserJourneyItemsTests.Flatten(vm.Explorer.Roots).Any(n => n.Path == path);

    /// <summary>Selects a node in the tree and picks an entry of its context menu, as a right-click does.</summary>
    private static async Task<Window> OpenDialogFromMenuAsync(TestWindow host, SolutionTreeNode node, string header)
    {
        var explorer = host.Window.SolutionExplorerForTests!;
        explorer.TreeForTests.SelectedItem = node;
        UserJourneyItemsTests.ClickContextMenuItem(explorer, header);

        var dialog = await UserJourneyItemsTests.WaitForDialogAsync(host);
        Assert.NotNull(dialog);
        return dialog!;
    }

    private static void TypeInto(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = text;

    /// <summary>Clicks a dialog button by its label and lets the window run the continuation.</summary>
    private static async Task PressAsync(TestWindow host, Window dialog, string label)
    {
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(dialog, label));
        await host.SettleAsync(10);
    }

    private static string Text(string key) => Localizer.Get(key);

    private static MsBuildBuildService Builder() => new();

    // ==================================================================
    // Folder, then a class in it
    // ==================================================================

    [AvaloniaFact]
    public async Task ANewFolderThenANewClassInsideItIsWrittenListedOpenedAndBuilt()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var root = vm.Explorer.Roots.Single();

        var folderDialog = await OpenDialogFromMenuAsync(host, root, Text(StringKeys.SolutionExplorerNewFolder));
        TypeInto(folderDialog, "Models");
        await PressAsync(host, folderDialog, Text(StringKeys.ButtonOk));

        var folder = Path.Combine(_root, "Models");
        Assert.True(Directory.Exists(folder), "New Folder did not create the folder");
        var folderNode = NodeAt(vm, folder);
        Assert.Equal(NodeKind.Folder, folderNode.Kind);

        var itemDialog = await UserJourneyItemsTests.OpenAddNewItemFromExplorerAsync(host, folderNode);
        UserJourneyItemsTests.ChooseTemplate(itemDialog, ItemTemplate.Class);
        UserJourneyItemsTests.TypeName(itemDialog, "Customer");
        await PressAsync(host, itemDialog, Text(StringKeys.AddItemCreate));

        var path = Path.Combine(folder, "Customer.vb");
        Assert.True(File.Exists(path), "the new class was not written into the folder");

        // This project does not use folder namespaces (its RootNamespace is
        // empty, and ItemTemplates declares none), so the class sits at the
        // root of the project and the file declares no Namespace.
        var text = File.ReadAllText(path);
        Assert.Contains("Public Class Customer", text);
        Assert.DoesNotContain("Namespace", text);

        Assert.True(HasNode(vm, path), "the new class is not listed under its folder");
        Assert.Equal(path, vm.ActiveDocument?.FilePath);

        var build = await Builder().BuildAsync(project);
        Assert.True(build.Succeeded, string.Join("\n", build.Diagnostics.Select(d => d.Message)));
    }

    // ==================================================================
    // Rename a file that is open with unsaved edits
    // ==================================================================

    [AvaloniaFact]
    public async Task RenamingAnOpenFileWithUnsavedEditsMovesTheTabAndKeepsTheEdits()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";
        Assert.True(document.IsModified);

        var dialog = await OpenDialogFromMenuAsync(host, NodeAt(vm, helper), Text(StringKeys.SolutionExplorerRename));
        TypeInto(dialog, "Renamed.vb");

        var renamed = Path.Combine(_root, "Renamed.vb");
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(dialog, Text(StringKeys.ButtonOk)));
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => document.FilePath == renamed, TimeSpan.FromSeconds(60)),
            "the open tab did not follow the rename");
        await host.SettleAsync(5);

        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(helper));
        Assert.Same(document, vm.OpenDocuments.Single(d => d.FilePath == renamed));
        Assert.Contains("' unsaved edit", document.Text);
        Assert.True(document.IsModified, "the unsaved state was lost by the rename");
        Assert.Contains("Renamed.vb", document.Title);
    }

    [AvaloniaFact]
    public async Task RenamingToANameThatExistsIsRefusedWithAMessageAndNothingMoves()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        var other = Path.Combine(_root, "Other.vb");
        File.WriteAllText(helper, Helper);
        File.WriteAllText(other, "Module Other\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";

        var dialog = await OpenDialogFromMenuAsync(host, NodeAt(vm, helper), Text(StringKeys.SolutionExplorerRename));
        TypeInto(dialog, "Other.vb");
        await PressAsync(host, dialog, Text(StringKeys.ButtonOk));

        Assert.True(File.Exists(helper), "the refused rename moved the file anyway");
        Assert.Equal("Module Other\nEnd Module\n", File.ReadAllText(other));
        Assert.Equal(helper, document.FilePath);
        Assert.Contains("' unsaved edit", document.Text);
        Assert.Contains(vm.BuildOutput, line => line.Contains("'Other.vb' already exists", StringComparison.Ordinal));
    }

    // ==================================================================
    // Rename a folder that holds an open file
    // ==================================================================

    [AvaloniaFact]
    public async Task RenamingAFolderMovesTheTabOfAnOpenFileInsideIt()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var folder = Path.Combine(_root, "Models");
        Directory.CreateDirectory(folder);
        var customer = Path.Combine(folder, "Customer.vb");
        File.WriteAllText(customer, "Public Class Customer\nEnd Class\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(customer);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == customer);

        var dialog = await OpenDialogFromMenuAsync(host, NodeAt(vm, folder), Text(StringKeys.SolutionExplorerRename));
        TypeInto(dialog, "Entities");

        var movedFolder = Path.Combine(_root, "Entities");
        var movedFile = Path.Combine(movedFolder, "Customer.vb");
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(dialog, Text(StringKeys.ButtonOk)));

        // The tree is read again only after the Roslyn workspace has reloaded,
        // which comes after the tab has moved, so wait for the tree itself.
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, movedFile), TimeSpan.FromSeconds(60)),
            "the tree does not show the file under its new folder");

        Assert.True(Directory.Exists(movedFolder));
        Assert.False(Directory.Exists(folder));
        Assert.Equal("Public Class Customer\nEnd Class\n", File.ReadAllText(movedFile));
        Assert.Equal(movedFile, document.FilePath);
        Assert.Contains(vm.OpenDocuments, d => d == document);
    }

    // ==================================================================
    // Delete a file that is open
    // ==================================================================

    [AvaloniaFact]
    public async Task DeletingAnOpenFileAfterConfirmationClosesItsTabAndRemovesIt()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        Assert.Contains(vm.OpenDocuments, d => d.FilePath == helper);

        var confirm = await OpenDialogFromMenuAsync(host, NodeAt(vm, helper), Text(StringKeys.SolutionExplorerDelete));
        await PressAsync(host, confirm, Text(StringKeys.ExplorerDeleteConfirm));
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => !File.Exists(helper), TimeSpan.FromSeconds(30)),
            "the file was not deleted after confirming");
        await host.SettleAsync(5);

        Assert.DoesNotContain(vm.OpenDocuments, d => d.FilePath == helper);
        Assert.False(HasNode(vm, helper));
    }

    [AvaloniaFact]
    public async Task CancellingTheDeleteKeepsTheFileAndItsUnsavedTab()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";

        var confirm = await OpenDialogFromMenuAsync(host, NodeAt(vm, helper), Text(StringKeys.SolutionExplorerDelete));
        await PressAsync(host, confirm, Text(StringKeys.ButtonCancel));

        Assert.True(File.Exists(helper), "Cancel deleted the file");
        Assert.Equal(Helper, File.ReadAllText(helper));
        Assert.Same(document, vm.OpenDocuments.SingleOrDefault(d => d.FilePath == helper));
        Assert.Contains("' unsaved edit", document.Text);
        Assert.True(HasNode(vm, helper));
    }

    // ==================================================================
    // Files written or removed outside the IDE
    // ==================================================================

    [AvaloniaFact]
    public async Task AFileWrittenOrDeletedOutsideTheIdeAppearsInAndLeavesTheTree()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        var outside = Path.Combine(_root, "FromOutside.vb");
        File.WriteAllText(outside, "Module FromOutside\nEnd Module\n");

        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, outside), TimeSpan.FromSeconds(15)),
            "a file written outside the IDE does not appear in the tree");

        File.Delete(outside);

        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => !HasNode(vm, outside), TimeSpan.FromSeconds(15)),
            "a file deleted outside the IDE is still in the tree");
    }

    // ==================================================================
    // Cut and Paste with Ctrl+X and Ctrl+V while the tree has focus
    // ==================================================================

    /// <summary>
    /// Selects a node in the tree and gives the explorer the keyboard, the way
    /// SolutionExplorerCommandTests does: the first focusable control inside the
    /// panel stands for the explorer having focus.
    /// </summary>
    private static async Task SelectInTreeAsync(TestWindow host, SolutionTreeNode node)
    {
        var panel = host.Window.SolutionExplorerForTests!;
        panel.TreeForTests.SelectedItem = node;
        await host.SettleAsync(5);
        FocusTarget(panel).Focus();
        await host.SettleAsync(2);
        Assert.True(panel.IsKeyboardFocusWithin, "the explorer never took keyboard focus");
    }

    private static InputElement FocusTarget(SolutionExplorerPanel panel) =>
        panel.GetVisualDescendants().OfType<InputElement>().First(c => c.Focusable);

    /// <summary>Ctrl+key pressed while the explorer has the keyboard.</summary>
    private static void PressCtrl(MainWindow window, Key key)
    {
        var target = FocusTarget(window.SolutionExplorerForTests!);
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.Control,
            Source = target
        });
    }

    /// <summary>Selects a node, then chooses one of its context-menu entries by its label key.</summary>
    private static void ChooseInExplorerMenu(TestWindow host, SolutionTreeNode node, string labelKey)
    {
        var explorer = host.Window.SolutionExplorerForTests!;
        explorer.TreeForTests.SelectedItem = node;
        ContextMenus.ChooseForTests(explorer.Menu!, Localizer.Get(labelKey));
    }

    [AvaloniaFact]
    public async Task CutAndPasteFromTheMenuMovesAFileIntoAFolderAndItsOpenTabFollows()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";

        ChooseInExplorerMenu(host, NodeAt(vm, helper), StringKeys.ExplorerCut);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerPaste);

        var moved = Path.Combine(models, "Helper.vb");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, moved), TimeSpan.FromSeconds(60)),
            "the pasted file is not listed under its new folder");
        await host.SettleAsync(5);

        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(helper));
        Assert.Same(document, vm.OpenDocuments.Single(d => d.FilePath == moved));
        Assert.Contains("' unsaved edit", document.Text);
        Assert.False(HasNode(vm, helper));
    }

    [AvaloniaFact]
    public async Task CtrlXAndCtrlVMoveAFileIntoAFolderAndItsOpenTabFollows()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";

        await SelectInTreeAsync(host, NodeAt(vm, helper));
        PressCtrl(host.Window, Key.X);
        await SelectInTreeAsync(host, NodeAt(vm, models));
        PressCtrl(host.Window, Key.V);

        var moved = Path.Combine(models, "Helper.vb");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, moved), TimeSpan.FromSeconds(60)),
            "the pasted file is not listed under its new folder");
        await host.SettleAsync(5);

        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(helper));
        Assert.Equal(moved, document.FilePath);
        Assert.Same(document, vm.OpenDocuments.Single(d => d.FilePath == moved));
        Assert.Contains("' unsaved edit", document.Text);
        Assert.False(HasNode(vm, helper));
    }

    [AvaloniaFact]
    public async Task PastingOntoANameThatExistsIsRefusedAndNeitherFileChanges()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var helper = Path.Combine(_root, "Helper.vb");
        var inModels = Path.Combine(models, "Helper.vb");
        File.WriteAllText(helper, Helper);
        File.WriteAllText(inModels, "Module Kept\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, helper), StringKeys.ExplorerCut);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerPaste);
        await host.SettleAsync(10);

        Assert.Equal(Helper, File.ReadAllText(helper));
        Assert.Equal("Module Kept\nEnd Module\n", File.ReadAllText(inModels));
        Assert.Contains(vm.BuildOutput, line => line.Contains("'Helper.vb' already exists", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task CuttingAFolderAndPastingItIntoItselfIsRefused()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        var inner = Path.Combine(models, "Inner");
        Directory.CreateDirectory(inner);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerCut);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, inner), StringKeys.ExplorerPaste);
        await host.SettleAsync(10);

        Assert.True(Directory.Exists(inner), "the folder was moved into its own folder");
        Assert.True(Directory.Exists(models));
        Assert.Contains(vm.BuildOutput, line => line.Contains("cannot be moved into itself", StringComparison.Ordinal));
    }

    // ==================================================================
    // Copy and Paste: a copy gets a free name and overwrites nothing
    // ==================================================================

    [AvaloniaFact]
    public async Task CopyAndPasteInTheSameFolderMakesACopyWithAFreeNameAndOverwritesNothing()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var module1 = Path.Combine(_root, "Module1.vb");
        const string Module1Text = "Module Module1\nEnd Module\n";
        File.WriteAllText(module1, Module1Text);
        var firstCopy = Path.Combine(_root, "Module1 - Copy.vb");
        File.WriteAllText(firstCopy, "' a file already called that\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, module1), StringKeys.ExplorerCopy);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, module1), StringKeys.ExplorerPaste);

        var secondCopy = Path.Combine(_root, "Module1 - Copy (2).vb");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => File.Exists(secondCopy), TimeSpan.FromSeconds(30)),
            "the paste did not make a copy under a free name");

        Assert.Equal("' a file already called that\n", File.ReadAllText(firstCopy));
        Assert.Equal(Module1Text, File.ReadAllText(secondCopy));
        Assert.Equal(Module1Text, File.ReadAllText(module1));
    }

    // ==================================================================
    // Exclude From Project and Show All Files, for an SDK-style project
    // ==================================================================

    [AvaloniaFact]
    public async Task ExcludingAFileHidesItKeepsItOnDiskShowAllFilesGreysItAndIncludingItBringsItBack()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var window = host.Window;
        Assert.True(HasNode(vm, extra));

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerExcludeFromProject);
        await host.SettleAsync(10);

        Assert.True(File.Exists(extra), "excluding a file deleted it");
        Assert.Contains("<Compile Remove=\"Extra.vb\" />", File.ReadAllText(project));
        Assert.False(HasNode(vm, extra), "an excluded file is still listed");
        Assert.True((await new MsBuildBuildService().BuildAsync(project)).Succeeded, "the project does not build without the file");

        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        Assert.True(HasNode(vm, extra), "Show All Files does not list the excluded file");
        Assert.True(NodeAt(vm, extra).IsOutOfProject, "the excluded file is not greyed out");

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.DoesNotContain("Remove=\"Extra.vb\"", File.ReadAllText(project));
        Assert.False(NodeAt(vm, extra).IsOutOfProject, "an included file is still greyed out");
        Assert.True((await new MsBuildBuildService().BuildAsync(project)).Succeeded, "the project does not build with the file back");
    }

    [AvaloniaFact]
    public async Task IncludingAFileTheDefaultGlobsDoNotReachAddsAnIncludeItemAndTheProjectBuilds()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        File.WriteAllText(project, File.ReadAllText(project).Replace(
            "</Project>",
            "  <PropertyGroup><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup>\n" +
            "  <ItemGroup><Compile Include=\"Program.vb\" /></ItemGroup>\n</Project>"));
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        Assert.False(HasNode(vm, extra), "a file outside the project is listed without Show All Files");

        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        Assert.True(NodeAt(vm, extra).IsOutOfProject);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.Contains("<Compile Include=\"Extra.vb\" />", File.ReadAllText(project));
        Assert.False(NodeAt(vm, extra).IsOutOfProject);
        Assert.True((await new MsBuildBuildService().BuildAsync(project)).Succeeded, "the project does not build with the included file");
    }

    // ==================================================================
    // Drag a file onto a folder
    // ==================================================================

    // ==================================================================
    // Project items the user wrote by hand, and the project's own layout
    // ==================================================================

    [AvaloniaFact]
    public async Task AHandWrittenRemoveIsRefusedAndTheProjectIsLeftAsItWas()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var handWritten = File.ReadAllText(project).Replace("</Project>",
            "  <ItemGroup>\n    <Compile Remove=\"Extra.vb\" Condition=\"'$(Configuration)' == 'Debug'\" />\n  </ItemGroup>\n</Project>");
        File.WriteAllText(project, handWritten);
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        Assert.True(NodeAt(vm, extra).IsOutOfProject);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.Equal(handWritten, File.ReadAllText(project));
        Assert.True(NodeAt(vm, extra).IsOutOfProject, "the file was included although a hand-written item excludes it");
        Assert.Contains(vm.BuildOutput, line => line.Contains("written by hand", StringComparison.Ordinal));
    }

    /// <remarks>
    /// A plain Remove written by hand with single quotes, spaces around "=" and
    /// a forward slash. The XML reading took it as plain while the text pattern
    /// missed it, so Include failed and said nothing. Reintroduced once (the
    /// double-quote-only pattern): the file stayed out of the project.
    /// </remarks>
    [AvaloniaFact]
    public async Task ASingleQuotedExcludeIsRemovedByIncludeAndNothingElseChanges()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var original = File.ReadAllText(project);
        File.WriteAllText(project, original.Replace("</Project>",
            "  <ItemGroup>\n    <Compile Remove = 'Tools/Extra.vb' />\n  </ItemGroup>\n</Project>"));
        var extra = Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "Tools")).FullName, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        Assert.True(NodeAt(vm, extra).IsOutOfProject);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.False(NodeAt(vm, extra).IsOutOfProject, "the single-quoted exclude was not removed");
        Assert.Equal(original, File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task ALongFormExcludeIsRemovedByIncludeAndNothingElseChanges()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var original = File.ReadAllText(project);
        var longForm = original.Replace("</Project>",
            "  <ItemGroup>\n    <Compile Remove=\"Extra.vb\"></Compile>\n  </ItemGroup>\n</Project>");
        File.WriteAllText(project, longForm);
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        Assert.True(NodeAt(vm, extra).IsOutOfProject);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.False(NodeAt(vm, extra).IsOutOfProject);
        Assert.Equal(original, File.ReadAllText(project));
    }

    [AvaloniaFact]
    public async Task AGlobRemoveIsNeverTouchedAndExcludeThenIncludeLeavesTheProjectByteIdentical()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        File.WriteAllText(project, File.ReadAllText(project).Replace("</Project>",
            "  <ItemGroup>\n    <Compile Remove=\"Old\\**\" />\n  </ItemGroup>\n</Project>"));
        var before = File.ReadAllBytes(project);
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerExcludeFromProject);
        await host.SettleAsync(10);
        Assert.Contains("<Compile Remove=\"Extra.vb\" />", File.ReadAllText(project));

        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.Contains("<Compile Remove=\"Old\\**\" />", File.ReadAllText(project));
        Assert.Equal(before, File.ReadAllBytes(project));
    }

    [AvaloniaFact]
    public async Task ACrlfProjectGetsCrlfAndItsOwnIndentationAndExcludeThenIncludeIsByteIdentical()
    {
        var project = Path.Combine(_root, "App.vbproj");
        const string crlfProject =
            "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n" +
            "    <PropertyGroup>\r\n" +
            "        <OutputType>Exe</OutputType>\r\n" +
            "        <TargetFramework>net10.0</TargetFramework>\r\n" +
            "    </PropertyGroup>\r\n" +
            "</Project>\r\n";
        File.WriteAllText(project, crlfProject);
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module Program\r\nEnd Module\r\n");
        var extra = Path.Combine(_root, "Extra.vb");
        File.WriteAllText(extra, "Module Extra\r\nEnd Module\r\n");
        var before = File.ReadAllBytes(project);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerExcludeFromProject);
        await host.SettleAsync(10);

        var excluded = File.ReadAllText(project);
        Assert.Contains("    <ItemGroup>\r\n        <Compile Remove=\"Extra.vb\" />\r\n    </ItemGroup>\r\n</Project>", excluded);
        Assert.Equal(excluded.Split("\r\n").Length - 1, excluded.Count(c => c == '\n'));

        vm.Explorer.ShowAllFiles = true;
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, extra), StringKeys.ExplorerIncludeInProject);
        await host.SettleAsync(10);

        Assert.Equal(before, File.ReadAllBytes(project));
    }

    // ==================================================================
    // A Cut whose source vanished, a folder copied into itself, a folder
    // moved with an open unsaved file, and a watcher that lost events
    // ==================================================================

    [AvaloniaFact]
    public async Task ACutFileDeletedBeforePasteIsReportedOnceAndTheClipboardIsCleared()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, helper), StringKeys.ExplorerCut);
        await host.SettleAsync(5);
        File.Delete(helper);
        await host.SettleAsync(5);

        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerPaste);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerPaste);
        await host.SettleAsync(5);

        Assert.Equal(1, vm.BuildOutput.Count(line => line.Contains("no longer exists", StringComparison.Ordinal)));
        Assert.False(File.Exists(Path.Combine(models, "Helper.vb")));
    }

    [AvaloniaFact]
    public async Task CopyingAFolderIntoItselfIsRefusedWithTheCopyMessage()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        var inner = Path.Combine(models, "Inner");
        Directory.CreateDirectory(inner);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerCopy);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, inner), StringKeys.ExplorerPaste);
        await host.SettleAsync(10);

        Assert.Equal(new[] { inner }, Directory.GetDirectories(models));
        Assert.Contains(vm.BuildOutput, line => line.Contains("cannot be copied into itself", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.BuildOutput, line => line.Contains("cannot be moved into itself", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task CutAndPasteOfAFolderKeepsItsOpenFileAndTheUnsavedEdits()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var customer = Path.Combine(models, "Customer.vb");
        File.WriteAllText(customer, "Public Class Customer\nEnd Class\n");
        var target = Path.Combine(_root, "Target");
        Directory.CreateDirectory(target);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(customer);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == customer);
        document.Text += "' unsaved edit\n";

        ChooseInExplorerMenu(host, NodeAt(vm, models), StringKeys.ExplorerCut);
        await host.SettleAsync(5);
        ChooseInExplorerMenu(host, NodeAt(vm, target), StringKeys.ExplorerPaste);

        var moved = Path.Combine(target, "Models", "Customer.vb");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, moved), TimeSpan.FromSeconds(60)),
            "the moved folder's file is not listed under its new place");
        await host.SettleAsync(5);

        Assert.False(Directory.Exists(models));
        Assert.Equal(moved, document.FilePath);
        Assert.Same(document, vm.OpenDocuments.Single(d => d.FilePath == moved));
        Assert.Contains("' unsaved edit", document.Text);
        Assert.True(document.IsModified);
    }

    [AvaloniaFact]
    public async Task AWatcherOverflowReadsTheWholeTreeAgain()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        // Let the window's own startup refreshes, and the watcher's late events
        // for the files written above, finish before the loads are counted.
        await host.SettleAsync(40);

        var loads = 0;
        vm.Explorer.Loaded += (_, _) => loads++;

        host.Window.SolutionExplorerForTests!.RaiseWatchErrorForTests();

        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => loads > 0, TimeSpan.FromSeconds(5)),
            "a lost-events error did not read the tree again");
    }

    /// <summary>A point on a tree row, in window coordinates, where a pointer lands on it.</summary>
    private static async Task<Point> RowPointAsync(TestWindow host, MainWindowViewModel vm, SolutionTreeNode node)
    {
        var tree = host.Window.SolutionExplorerForTests!.TreeForTests;

        // A row exists on screen only once every folder above it is open, and
        // the tree realises its containers on the dispatcher, as TestExplorerPanelTests
        // waits for them: open each parent, run the jobs, and look again.
        for (var pass = 0; pass < 6; pass++)
        {
            foreach (var parent in UserJourneyItemsTests.Flatten(vm.Explorer.Roots).Where(n => n.Children.Count > 0).ToList())
                if (tree.TreeContainerFromItem(parent) is TreeViewItem { IsExpanded: false } open) open.IsExpanded = true;

            Dispatcher.UIThread.RunJobs();
            await host.SettleAsync(3);

            // The row's own label: the point a pointer lands on to press the row.
            var label = tree.GetVisualDescendants().OfType<TextBlock>()
                .FirstOrDefault(text => text.Text == node.Name && text.Bounds.Width > 0);
            if (label is not null)
                return label.TranslatePoint(new Point(4, 4), host.Window)!.Value;
        }

        var rootItem = tree.GetVisualDescendants().OfType<TreeViewItem>().Count();
        var rootContainer = tree.TreeContainerFromItem(vm.Explorer.Roots.Single());
        Assert.Fail("the row for " + node.Name + " never came on screen; tree items " + rootItem + ", root container " + (rootContainer is null ? "null" : rootContainer.GetType().Name + " expanded=" + ((TreeViewItem)rootContainer).IsExpanded) + ", tree visible " + tree.IsVisible + ", bounds " + tree.Bounds);
        return default;
    }

    /// <summary>
    /// Replaces the panel's drag start with one that only records the call, so a
    /// test sees whether a drag would have started without a drag loop running.
    /// </summary>
    private static List<PointerPressedEventArgs> RecordDragStarts(TestWindow host)
    {
        var starts = new List<PointerPressedEventArgs>();
        host.Window.SolutionExplorerForTests!.StartDragAsync = (press, _) =>
        {
            starts.Add(press);
            return Task.CompletedTask;
        };
        return starts;
    }

    [AvaloniaFact]
    public async Task AClickSelectsTheRowAndStartsNoDrag()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var starts = RecordDragStarts(host);
        var node = NodeAt(vm, helper);

        var at = await RowPointAsync(host, vm, node);
        host.Window.MouseDown(at, MouseButton.Left);
        host.Window.MouseUp(at, MouseButton.Left);
        await host.SettleAsync(5);

        Assert.Equal(helper, (host.Window.SolutionExplorerForTests!.TreeForTests.SelectedItem as SolutionTreeNode)?.Path);
        Assert.Empty(starts);

        // Released before the threshold: a later move with the button up is no drag either.
        host.Window.MouseMove(at + new Vector(40, 0));
        await host.SettleAsync(2);
        Assert.Empty(starts);
    }

    [AvaloniaFact]
    public async Task ADoubleClickOpensTheFileAndStartsNoDrag()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var starts = RecordDragStarts(host);
        var node = NodeAt(vm, helper);
        var panel = host.Window.SolutionExplorerForTests!;

        var at = await RowPointAsync(host, vm, node);
        host.Window.MouseDown(at, MouseButton.Left);
        host.Window.MouseUp(at, MouseButton.Left);
        host.Window.MouseDown(at, MouseButton.Left);
        host.Window.MouseUp(at, MouseButton.Left);

        // The double-tap itself is raised as the tests elsewhere raise it: the
        // tree's own gesture recogniser is not driven by the headless pointer.
        panel.TreeForTests.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(10);

        Assert.Contains(vm.OpenDocuments, d => d.FilePath == helper);
        Assert.Empty(starts);
    }

    [AvaloniaFact]
    public async Task MovingPastTheThresholdWithTheButtonDownStartsADragOnce()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var starts = RecordDragStarts(host);

        var at = await RowPointAsync(host, vm, NodeAt(vm, helper));
        host.Window.MouseDown(at, MouseButton.Left);

        host.Window.MouseMove(at + new Vector(1, 1), RawInputModifiers.LeftMouseButton);
        await host.SettleAsync(2);
        Assert.Empty(starts);

        host.Window.MouseMove(at + new Vector(12, 0), RawInputModifiers.LeftMouseButton);
        await host.SettleAsync(2);
        Assert.Single(starts);

        host.Window.MouseUp(at + new Vector(12, 0), MouseButton.Left);
        host.Window.MouseMove(at + new Vector(30, 0));
        await host.SettleAsync(2);
        Assert.Single(starts);
    }

    [AvaloniaFact]
    public async Task DroppingAFileOnAFolderMovesItAndItsOpenTabFollows()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "App");
        var models = Path.Combine(_root, "Models");
        Directory.CreateDirectory(models);
        var helper = Path.Combine(_root, "Helper.vb");
        File.WriteAllText(helper, Helper);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);

        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        var document = vm.OpenDocuments.Single(d => d.FilePath == helper);
        document.Text += "' unsaved edit\n";

        var panel = host.Window.SolutionExplorerForTests!;

        // The drop raises the panel's CommandChosen. MainWindow.ConnectContextMenus
        // carries that event to RunExplorerCommandAsync; this test carries it to
        // the same command directly, so the move itself is the real one.
        Task? moving = null;
        panel.CommandChosen += (_, chosen) =>
            moving = host.Window.RunExplorerFileCommandAsync(chosen.Command, chosen.Node);

        panel.DropOn(NodeAt(vm, helper), NodeAt(vm, models));
        Assert.NotNull(moving);
        await moving!;

        var moved = Path.Combine(models, "Helper.vb");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => HasNode(vm, moved), TimeSpan.FromSeconds(60)),
            "the dropped file is not listed under the folder it was dropped on");
        await host.SettleAsync(5);

        Assert.True(File.Exists(moved));
        Assert.False(File.Exists(helper));
        Assert.Same(document, vm.OpenDocuments.Single(d => d.FilePath == moved));
        Assert.Contains("' unsaved edit", document.Text);
    }
}
