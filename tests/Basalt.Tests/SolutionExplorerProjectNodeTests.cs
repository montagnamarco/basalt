using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A folder under a solution that holds one project is that project in
/// Solution Explorer, as Visual Studio shows it: the node carries the project's
/// icon, menu and actions, and its project file is not listed again inside it.
///
/// The node reuses the path a project opened alone already has for its root:
/// its Path is the project file, its children are read from the file's
/// directory. Sessions saved before the change remember the folder, and the
/// folder path still opens and selects the node.
/// </summary>
public sealed class SolutionExplorerProjectNodeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-project-node-").FullName;

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>Writes one console project per name, each in a folder of that name, and a .slnx listing them.</summary>
    private string WriteSolution(params string[] projects)
    {
        foreach (var name in projects)
            UserJourneyItemsTests.WriteConsoleProject(Path.Combine(_root, name), name);

        var solution = Path.Combine(_root, "Probe.slnx");
        var entries = string.Concat(projects.Select(name => $"  <Project Path=\"{name}/{name}.vbproj\" />\n"));
        File.WriteAllText(solution, $"<Solution>\n{entries}</Solution>\n");
        return solution;
    }

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private static SolutionTreeNode ProbeNode(SolutionExplorerViewModel explorer) =>
        Flatten(explorer.Roots).Single(n => n.Kind == NodeKind.Project && n.Name == "Probe");

    private static MainWindowViewModel ViewModelOf(TestWindow host) =>
        (MainWindowViewModel)host.Window.DataContext!;

    private static string L(string key) => Localizer.Get(key);

    private static void TypeInto(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<TextBox>().Single().Text = text;

    private static string? SelectedProjectPathOf(MainWindow window) =>
        (string?)typeof(MainWindow)
            .GetMethod("SelectedProjectPath", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null);

    private static IReadOnlyList<string> OtherProjectsOf(MainWindowViewModel vm, string projectPath) =>
        (IReadOnlyList<string>)typeof(ReferencesDialog)
            .GetMethod("OtherProjectsOf", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [vm, projectPath])!;

    // ==================================================================
    // The tree's structure
    // ==================================================================

    /// <summary>
    /// The outcome itself: the solution's Probe folder becomes the project,
    /// its MainForm.vb keeps its designer companion, Program.vb sits beside
    /// them, and Probe.vbproj is listed nowhere but as the node.
    /// </summary>
    [Fact]
    public void AFolderHoldingOneProjectIsTheProjectNodeAndItsFileIsNotListedAgain()
    {
        var solution = WriteSolution("Probe");
        var probe = Path.Combine(_root, "Probe");
        File.WriteAllText(Path.Combine(probe, "MainForm.vb"), "Public Class MainForm\nEnd Class\n");
        File.WriteAllText(Path.Combine(probe, "MainForm.Designer.vb"), "Partial Class MainForm\nEnd Class\n");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);

        var root = Assert.Single(explorer.Roots);
        Assert.Equal(NodeKind.Solution, root.Kind);

        var project = Assert.Single(root.Children);
        Assert.Equal(NodeKind.Project, project.Kind);
        Assert.Equal("Probe", project.Name);
        Assert.Equal(Path.Combine(probe, "Probe.vbproj"), project.Path);

        Assert.Equal(["MainForm.vb", "Program.vb"], project.Children.Select(n => n.Name));
        var form = project.Children[0];
        Assert.Equal(["MainForm.Designer.vb"], form.Children.Select(n => n.Name));
        Assert.Equal(Path.Combine(probe, "MainForm.Designer.vb"), form.DesignerPath);

        Assert.DoesNotContain(Flatten(explorer.Roots), n => n.Name == "Probe.vbproj");
    }

    [Fact]
    public void AFolderWithNoProjectOrSeveralStaysAFolder()
    {
        var solution = WriteSolution("Probe");
        Directory.CreateDirectory(Path.Combine(_root, "Empty"));
        var pair = Path.Combine(_root, "Pair");
        Directory.CreateDirectory(pair);
        File.WriteAllText(Path.Combine(pair, "Pair.vbproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(pair, "Pair.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);

        var children = explorer.Roots[0].Children;
        Assert.Equal(NodeKind.Folder, children.Single(n => n.Name == "Empty").Kind);

        // Two project files: no single project stands for the folder, so both
        // stay plain files in it, as they were before.
        var folder = children.Single(n => n.Name == "Pair");
        Assert.Equal(NodeKind.Folder, folder.Kind);
        Assert.Equal(["Pair.csproj", "Pair.vbproj"], folder.Children.Select(n => n.Name).OrderBy(n => n));
        Assert.DoesNotContain(Flatten(explorer.Roots), n => n.Kind == NodeKind.Project && n.Name == "Pair");
    }

    /// <summary>A project opened alone is not a solution: its subfolders keep their folder nodes.</summary>
    [Fact]
    public void AProjectOpenedAloneKeepsItsSubfoldersAsFolders()
    {
        var project = UserJourneyItemsTests.WriteConsoleProject(_root, "Lone");
        var sub = Path.Combine(_root, "Sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "Sub.vbproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(project);

        var folder = Flatten(explorer.Roots).Single(n => n.Path == sub);
        Assert.Equal(NodeKind.Folder, folder.Kind);
        Assert.Equal(NodeKind.Project, folder.Children.Single().Kind);
    }

    [Fact]
    public void ShowAllFilesKeepsTheProjectFileInsideItsOwnNodeOnly()
    {
        var solution = WriteSolution("Probe");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);
        explorer.ShowAllFiles = true;

        var project = ProbeNode(explorer);
        Assert.Equal(NodeKind.Project, project.Kind);
        Assert.Contains(project.Children, n => n.Name == "Program.vb");
        Assert.DoesNotContain(project.Children, n => n.Name == "Probe.vbproj");
        Assert.DoesNotContain(Flatten(explorer.Roots), n => n.Name == "Probe.vbproj");
    }

    /// <summary>The project node is named by its project, is not renamed or deleted, and does not open.</summary>
    [Fact]
    public void TheProjectNodeRefusesRenameDeleteAndOpenBecauseItIsTheProject()
    {
        var solution = WriteSolution("Probe");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);

        var project = ProbeNode(explorer);
        Assert.False(project.CanRenameOrDelete);
        Assert.False(project.IsOpenable);
        Assert.Equal(NodeKind.Project, project.Kind);
    }

    /// <summary>
    /// Expansion and selection are remembered by path. The folder path an
    /// older session holds selects the project node; the project file path a
    /// new session holds selects it too.
    /// </summary>
    [Fact]
    public void SelectingByTheFolderOrByTheProjectFileFindsTheProjectNode()
    {
        var solution = WriteSolution("Probe");
        var folder = Path.Combine(_root, "Probe");
        var projectFile = Path.Combine(folder, "Probe.vbproj");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);

        Assert.True(explorer.SelectPath(folder));
        Assert.Equal(NodeKind.Project, explorer.SelectedNode!.Kind);
        Assert.Equal(projectFile, explorer.SelectedNode.Path);

        Assert.True(explorer.SelectPath(projectFile));
        Assert.Equal(projectFile, explorer.SelectedNode!.Path);
    }

    /// <summary>
    /// The file watcher calls Refresh when a file in the folder is added. The
    /// project node stays open and selected, and the new file is listed in it.
    /// </summary>
    [Fact]
    public void RefreshKeepsTheProjectNodeOpenAndListsWhatWasAddedToItsFolder()
    {
        var solution = WriteSolution("Probe");
        var folder = Path.Combine(_root, "Probe");
        var program = Path.Combine(folder, "Program.vb");

        var explorer = new SolutionExplorerViewModel();
        explorer.Load(solution);
        ProbeNode(explorer).IsExpanded = true;
        Assert.True(explorer.SelectPath(program));

        var added = Path.Combine(folder, "Helper.vb");
        File.WriteAllText(added, "Module Helper\nEnd Module\n");
        Assert.True(explorer.IsListed(added));

        explorer.Refresh();

        var project = ProbeNode(explorer);
        Assert.True(project.IsExpanded);
        Assert.Contains(project.Children, n => n.Path == added);
        Assert.Equal(program, explorer.SelectedNode?.Path);
    }

    // ==================================================================
    // The node in the window: menu, icon, and each action
    // ==================================================================

    /// <summary>The project node's menu: Properties and References on, the file-only entries off, Rename and Delete refused.</summary>
    [AvaloniaFact]
    public async Task TheProjectNodeOffersTheProjectMenuAndTheProjectIcon()
    {
        var solution = WriteSolution("Probe");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;
        var project = ProbeNode(vm.Explorer);

        explorer.TreeForTests.SelectedItem = project;
        await host.SettleAsync(2);

        var icon = (IconKind)typeof(SolutionExplorerPanel)
            .GetMethod("IconFor", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [project])!;
        Assert.Equal(IconKind.Project, icon);

        var entries = ContextMenus.OpenForTests(explorer.Menu!);
        Assert.True(entries.Single(e => e.Header == L(StringKeys.SolutionExplorerProperties)).Enabled,
            "Properties is disabled on the project node");
        Assert.True(entries.Single(e => e.Header == L(StringKeys.ExplorerAddReference)).Enabled,
            "References is disabled on the project node");
        Assert.True(entries.Single(e => e.Header == L(StringKeys.ExplorerAddNewItem)).Enabled);
        Assert.False(entries.Single(e => e.Header == L(StringKeys.SolutionExplorerOpen)).Enabled,
            "Open is enabled on the project node, so the project would open as text");
        Assert.False(entries.Single(e => e.Header == L(StringKeys.SolutionExplorerRename)).Enabled,
            "Rename is enabled on the project node");
        Assert.False(entries.Single(e => e.Header == L(StringKeys.SolutionExplorerDelete)).Enabled,
            "Delete is enabled on the project node");
    }

    /// <summary>Project &gt; Properties from the node's menu opens the dialog for the project file.</summary>
    [AvaloniaFact]
    public async Task PropertiesOfTheProjectNodeOpensTheProjectFile()
    {
        var solution = WriteSolution("Probe");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;
        var project = ProbeNode(vm.Explorer);

        explorer.TreeForTests.SelectedItem = project;
        UserJourneyItemsTests.ClickContextMenuItem(explorer, L(StringKeys.SolutionExplorerProperties));

        var dialog = await UserJourneyItemsTests.WaitForDialogAsync(host);
        Assert.NotNull(dialog);
        Assert.Equal(Path.Combine(_root, "Probe", "Probe.vbproj"), SelectedProjectPathOf(w));

        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(dialog!, L(StringKeys.ButtonCancel)));
        await host.SettleAsync(10);
    }

    /// <summary>References on the project node targets its project file, and offers the other projects of the solution.</summary>
    [AvaloniaFact]
    public async Task ReferencesOfTheProjectNodeTargetsItsProjectFile()
    {
        var solution = WriteSolution("Probe", "Lib");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;
        var project = ProbeNode(vm.Explorer);

        explorer.TreeForTests.SelectedItem = project;
        Assert.Equal(project.Path, SelectedProjectPathOf(w));

        var others = OtherProjectsOf(vm, project.Path);
        Assert.Contains(Path.Combine(_root, "Lib", "Lib.vbproj"), others);
        Assert.DoesNotContain(project.Path, others);

        UserJourneyItemsTests.ClickContextMenuItem(explorer, L(StringKeys.ExplorerAddReference));
        var dialog = await UserJourneyItemsTests.WaitForDialogAsync(host);
        Assert.NotNull(dialog);
        dialog!.Close();
        await host.SettleAsync(5);
    }

    /// <summary>Add New Item on the project node writes the item into the project's folder and lists it there.</summary>
    [AvaloniaFact]
    public async Task AddNewItemOnTheProjectNodeWritesIntoTheProjectFolder()
    {
        var solution = WriteSolution("Probe");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var vm = ViewModelOf(host);
        var project = ProbeNode(vm.Explorer);

        var dialog = await UserJourneyItemsTests.OpenAddNewItemFromExplorerAsync(host, project);
        UserJourneyItemsTests.ChooseTemplate(dialog, ItemTemplate.Class);
        UserJourneyItemsTests.TypeName(dialog, "Customer");
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        var created = Path.Combine(_root, "Probe", "Customer.vb");
        Assert.True(File.Exists(created), "the new class was not written into the project's folder");
        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == created);
    }

    /// <summary>New File and New Folder on the project node create inside the project's folder.</summary>
    [AvaloniaFact]
    public async Task NewFileAndNewFolderOnTheProjectNodeCreateInsideTheProjectFolder()
    {
        var solution = WriteSolution("Probe");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;
        var project = ProbeNode(vm.Explorer);

        explorer.TreeForTests.SelectedItem = project;
        UserJourneyItemsTests.ClickContextMenuItem(explorer, L(StringKeys.SolutionExplorerNewFile));
        var fileDialog = await UserJourneyItemsTests.WaitForDialogAsync(host);
        Assert.NotNull(fileDialog);
        TypeInto(fileDialog!, "Added.vb");
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(fileDialog!, L(StringKeys.ButtonOk)));
        await host.SettleAsync(10);

        var folderNode = ProbeNode(vm.Explorer);
        explorer.TreeForTests.SelectedItem = folderNode;
        UserJourneyItemsTests.ClickContextMenuItem(explorer, L(StringKeys.SolutionExplorerNewFolder));
        var folderDialog = await UserJourneyItemsTests.WaitForDialogAsync(host);
        Assert.NotNull(folderDialog);
        TypeInto(folderDialog!, "Models");
        UserJourneyItemsTests.ClickLikeAMouse(UserJourneyItemsTests.ButtonWithText(folderDialog!, L(StringKeys.ButtonOk)));
        await host.SettleAsync(10);

        Assert.True(File.Exists(Path.Combine(_root, "Probe", "Added.vb")), "New File did not write into the project's folder");
        Assert.True(Directory.Exists(Path.Combine(_root, "Probe", "Models")), "New Folder did not create inside the project's folder");
    }

    /// <summary>Paste on the project node copies the clipboard entry into the project's folder.</summary>
    [AvaloniaFact]
    public async Task PasteOntoTheProjectNodeCopiesIntoTheProjectFolder()
    {
        var solution = WriteSolution("Probe");
        var notes = Path.Combine(_root, "Notes.txt");
        File.WriteAllText(notes, "shared notes");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;

        // The menu is driven as the journeys drive it: the entry's command runs
        // the same handler a click does.
        explorer.TreeForTests.SelectedItem = Flatten(vm.Explorer.Roots).Single(n => n.Path == notes);
        ContextMenus.ChooseForTests(explorer.Menu!, L(StringKeys.ExplorerCopy));
        await host.SettleAsync(5);

        explorer.TreeForTests.SelectedItem = ProbeNode(vm.Explorer);
        ContextMenus.ChooseForTests(explorer.Menu!, L(StringKeys.ExplorerPaste));

        // A paste always names its copy with the copy suffix, in any folder.
        var copied = Path.Combine(_root, "Probe", $"Notes{L(StringKeys.ExplorerCopySuffix)}.txt");
        Assert.True(await UserJourneyItemsTests.WaitUntilAsync(host, () => File.Exists(copied), TimeSpan.FromSeconds(30)),
            "Paste did not copy into the project's folder");
        Assert.True(File.Exists(notes), "Copy removed the original");
    }

    /// <summary>
    /// Double-click on the project node does not open the .vbproj as text: the
    /// node is not openable, so the tree opens nothing.
    /// </summary>
    [AvaloniaFact]
    public async Task DoubleClickOnTheProjectNodeOpensNothing()
    {
        var solution = WriteSolution("Probe");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = ViewModelOf(host);
        var explorer = w.SolutionExplorerForTests!;

        SolutionTreeNode? opened = null;
        explorer.FileOpened += (_, node) => opened = node;

        explorer.TreeForTests.SelectedItem = ProbeNode(vm.Explorer);
        explorer.TreeForTests.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(5);

        Assert.Null(opened);
        Assert.Empty(vm.OpenDocuments);
    }

    // ==================================================================
    // Sessions saved before the change
    // ==================================================================

    /// <summary>
    /// A session written before the project node existed remembers the folder.
    /// Restoring it opens and selects the project node; a session written now
    /// remembers the project file, which is the node's path.
    /// </summary>
    [AvaloniaFact]
    public async Task ASessionThatRemembersTheFolderReopensAndSelectsTheProjectNode()
    {
        var solution = WriteSolution("Probe");
        var folder = Path.Combine(_root, "Probe");
        var projectFile = Path.Combine(folder, "Probe.vbproj");

        using var viewModel = new MainWindowViewModel();
        viewModel.Explorer.Load(solution);
        var panel = new SolutionExplorerPanel(viewModel);
        viewModel.ReadExpandedFolders = () => panel.ExpandedFolders();
        viewModel.WriteExpandedFolders = folders => panel.ExpandFolders(folders);

        await viewModel.RestoreSessionAsync(new SolutionSession
        {
            SolutionPath = solution,
            ExpandedFolders = [folder],
            ExpansionRecorded = true,
            SelectedPath = folder
        });

        var project = ProbeNode(viewModel.Explorer);
        Assert.True(project.IsExpanded, "the project node was not reopened");
        Assert.Same(project, viewModel.Explorer.SelectedNode);

        var saved = viewModel.CaptureSession();
        Assert.Contains(projectFile, saved.ExpandedFolders);
        Assert.DoesNotContain(folder, saved.ExpandedFolders);
        Assert.Equal(projectFile, saved.SelectedPath);
    }
}
