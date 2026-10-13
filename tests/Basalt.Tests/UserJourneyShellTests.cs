using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// End-to-end user journeys through a real <see cref="MainWindow"/>: starting
/// from the welcome screen, creating a solution, working the Solution
/// Explorer, juggling tabs, changing Settings and walking every menu — the
/// same sequence of clicks and keystrokes a person sitting at the IDE would
/// make, rather than a single call into one method.
///
/// Settings-touching tests share <see cref="SettingsFileCollection"/> so the
/// process-wide BASALT_SETTINGS variable is never read or written by two
/// tests at once.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyShellTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyShellTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    private static string WriteConsoleProject(string root, string projectName = "App")
    {
        var projectPath = Path.Combine(root, $"{projectName}.vbproj");
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

    private static string L(string key) => Localizer.Get(key);

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

    private static async Task<Window?> WaitForDialogAsync(TestWindow host)
    {
        Window? dialog = null;

        for (var i = 0; i < 80 && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static void ClickButton(Window dialog, Func<Button, bool> choose)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().FirstOrDefault(choose);
        Assert.NotNull(button);
        button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void SetTextBox(Window dialog, string text)
    {
        var box = dialog.GetVisualDescendants().OfType<TextBox>().First();
        box.Text = text;
    }

    // ------------------------------------------------------------------
    // Regression: the left dock's tab headers ("Toolbox", "Structure",
    // "Outline") must not truncate at the IDE's default window size.
    //
    // IdeDockFactory gave the left column Proportion = 0.11 of the window's
    // width, which at 1440px left about 150px for three tabs — just short
    // of what "Toolbox" and "Structure" need, so both ended up "Tool…" and
    // "Stru…" with nothing wrong in ShrinkingTabPanel itself (it trims
    // correctly; there was simply not enough room to start with).
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task LeftDockTabHeadersAreNotTruncatedAtTheDefaultWindowSize()
    {
        var project = WriteConsoleProject(_root);

        using var host = new TestWindow(project);
        host.Window.Width = 1440;
        host.Window.Height = 900;
        await host.SettleAsync(10);

        var factory = (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(host.Window)!;

        // Make sure the three tabs this column always starts with are the
        // ones being measured, regardless of what else docked there later.
        Assert.Contains(factory.Toolbox, factory.ToolboxArea!.VisibleDockables!);
        Assert.Contains(factory.ElementTree, factory.ToolboxArea!.VisibleDockables!);
        Assert.Contains(factory.Outline, factory.ToolboxArea!.VisibleDockables!);

        var items = host.Window.GetVisualDescendants()
            .OfType<Dock.Avalonia.Controls.ToolTabStripItem>()
            .Where(i => i.DataContext is SolutionExplorerTool or ToolboxTool or ElementTreeTool or OutlineTool)
            .ToList();

        var leftTabs = host.Window.GetVisualDescendants()
            .OfType<Dock.Avalonia.Controls.ToolTabStripItem>()
            .Where(i => i.DataContext is ToolboxTool or ElementTreeTool or OutlineTool)
            .ToList();

        Assert.True(leftTabs.Count >= 3, $"expected the left column's 3 tabs, found {leftTabs.Count}");

        foreach (var tab in leftTabs)
        {
            var label = tab.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.GetVisualAncestors().OfType<ContentPresenter>()
                    .Any(p => p.Name == "PART_HeaderPresenter"));

            // ShrinkingTabPanel only caps MaxWidth when a tab did not get its
            // natural width; a tab with room to spare never has it set.
            Assert.False(label.IsSet(Layoutable.MaxWidthProperty),
                $"'{label.Text}' is truncated: the left dock is too narrow for it to show in full.");
        }
    }

    // ------------------------------------------------------------------
    // Regression: Project > Add Window with no project open used to report
    // the refusal in Italian ("Aprire prima un progetto.") in an otherwise
    // English interface.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AddingAWindowWithNoProjectReportsTheStatusInEnglish()
    {
        using var host = new TestWindow();
        await host.SettleAsync(5);

        var vm = (MainWindowViewModel)host.Window.DataContext!;
        Assert.Null(vm.StartupProject);

        InvokeMenu(host.Window, L(StringKeys.MenuProject), L(StringKeys.MenuProjectAddWindow));
        await host.SettleAsync(5);

        Assert.Equal(L(StringKeys.StatusOpenProjectFirst), vm.StatusMessage);
        Assert.DoesNotContain("progetto", vm.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // File > New Solution: the tree of dialects and project kinds, creating
    // a VB console app, a WinForms app and a class library — each actually
    // opened in the IDE afterwards, the way ShowNewSolutionDialogAsync does.
    // ------------------------------------------------------------------
    [AvaloniaTheory]
    [InlineData(ProjectTemplate.ConsoleApp)]
    [InlineData(ProjectTemplate.WinFormsApp)]
    [InlineData(ProjectTemplate.ClassLibrary)]
    public async Task NewSolutionDialogCreatesTheChosenTemplateAndOpensIt(ProjectTemplate template)
    {
        var dialog = new NewSolutionDialog();
        dialog.Show();
        for (var i = 0; i < 5; i++) dialog.UpdateLayout();

        var tree = dialog.FindControl<TreeView>("TemplateTree")!;
        var leaf = Flatten(tree.ItemsSource).Single(i => LeafTemplate(i) == template);
        tree.SelectedItem = leaf;

        var name = "Journey" + template;
        dialog.FindControl<TextBox>("NameBox")!.Text = name;
        dialog.FindControl<TextBox>("PathBox")!.Text = _root;

        Assert.Equal(template, dialog.SelectedTemplate);
        Assert.True(dialog.FindControl<Button>("CreateButton")!.IsEnabled);

        dialog.FindControl<Button>("CreateButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        dialog.Close();

        var parameters = dialog.LastCreatedForTests;
        Assert.NotNull(parameters);
        Assert.Equal(template, parameters!.Template);

        var created = await NewSolutionDialog.CreateAsync(parameters);
        Assert.True(File.Exists(created.ProjectPath), "the project file was not written to disk");
        Assert.True(File.Exists(created.SolutionPath), "the solution file was not written to disk");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenSolutionAsync(created.SolutionPath);
        await host.SettleAsync(8);

        Assert.Equal(Path.GetFullPath(created.SolutionPath), Path.GetFullPath(vm.SolutionPath!));
        Assert.NotNull(vm.StartupProject);

        // The project is the node itself, named as Visual Studio names it, and stands for its file.
        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Kind == NodeKind.Project
            && n.Name == Path.GetFileNameWithoutExtension(created.ProjectPath)
            && Path.GetFullPath(n.Path) == Path.GetFullPath(created.ProjectPath));
    }

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private static IEnumerable<TreeViewItem> Flatten(System.Collections.IEnumerable? items)
    {
        if (items is null) yield break;

        foreach (var entry in items)
        {
            if (entry is not TreeViewItem item) continue;
            yield return item;
            foreach (var child in Flatten(item.ItemsSource)) yield return child;
        }
    }

    private static ProjectTemplate? LeafTemplate(TreeViewItem item) =>
        item.Tag?.GetType().GetProperty("Template")?.GetValue(item.Tag) as ProjectTemplate?;

    // ------------------------------------------------------------------
    // Solution Explorer: opening a file by double-click and by Enter both
    // bring it up in an editor.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task DoubleClickingAndPressingEnterInSolutionExplorerBothOpenTheFile()
    {
        var project = WriteConsoleProject(_root);
        File.WriteAllText(Path.Combine(_root, "Other.vb"), "Module Other\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var explorer = w.SolutionExplorerForTests!;

        var programNode = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Program.vb");
        var otherNode = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Other.vb");

        // Double-click: a real DoubleTapped on the tree, the way the panel
        // itself listens for it.
        explorer.TreeForTests.SelectedItem = programNode;
        explorer.TreeForTests.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
        await host.SettleAsync(8);

        Assert.Equal(Path.Combine(_root, "Program.vb"), vm.ActiveDocument?.FilePath);

        // Enter, on a different node, through the tree's own KeyDown —
        // Visual Studio opens the selected item the same way a double-click
        // does.
        explorer.TreeForTests.SelectedItem = otherNode;
        explorer.TreeForTests.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });
        await host.SettleAsync(8);

        Assert.Equal(Path.Combine(_root, "Other.vb"), vm.ActiveDocument?.FilePath);
        Assert.Equal(2, vm.OpenDocuments.Count);
    }

    // ------------------------------------------------------------------
    // Solution Explorer context menu: New File, New Folder, Rename and
    // Delete, each driven through the real menu item a right click would
    // show, answering the dialog it opens the way a person would.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ExplorerContextMenuAddsRenamesAndDeletesAFileAndAFolder()
    {
        var project = WriteConsoleProject(_root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var explorer = w.SolutionExplorerForTests!;

        var projectNode = vm.Explorer.Roots.Single();

        // New File…
        explorer.TreeForTests.SelectedItem = projectNode;
        ClickContextMenuItem(explorer, "New File…");
        var newFileDialog = await WaitForDialogAsync(host);
        Assert.NotNull(newFileDialog);
        SetTextBox(newFileDialog!, "Added.vb");
        ClickButton(newFileDialog!, b => b.IsDefault);
        await host.SettleAsync(10);

        var addedPath = Path.Combine(_root, "Added.vb");
        Assert.True(File.Exists(addedPath), "New File did not create the file on disk");
        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == addedPath);

        // New Folder…
        explorer.TreeForTests.SelectedItem = projectNode;
        ClickContextMenuItem(explorer, "New Folder…");
        var newFolderDialog = await WaitForDialogAsync(host);
        Assert.NotNull(newFolderDialog);
        SetTextBox(newFolderDialog!, "Stuff");
        ClickButton(newFolderDialog!, b => b.IsDefault);
        await host.SettleAsync(10);

        var folderPath = Path.Combine(_root, "Stuff");
        Assert.True(Directory.Exists(folderPath), "New Folder did not create the folder on disk");

        // Rename… the file just created.
        var addedNode = Flatten(vm.Explorer.Roots).Single(n => n.Path == addedPath);
        explorer.TreeForTests.SelectedItem = addedNode;
        ClickContextMenuItem(explorer, "Rename…");
        var renameDialog = await WaitForDialogAsync(host);
        Assert.NotNull(renameDialog);
        SetTextBox(renameDialog!, "Renamed.vb");
        ClickButton(renameDialog!, b => b.IsDefault);
        await host.SettleAsync(10);

        var renamedPath = Path.Combine(_root, "Renamed.vb");
        Assert.False(File.Exists(addedPath), "the old file still exists after rename");
        Assert.True(File.Exists(renamedPath), "the renamed file is missing");

        // Delete the folder: confirm, then it is gone.
        var folderNode = Flatten(vm.Explorer.Roots).Single(n => n.Path == folderPath);
        explorer.TreeForTests.SelectedItem = folderNode;
        ClickContextMenuItem(explorer, "Delete");
        var deleteDialog = await WaitForDialogAsync(host);
        Assert.NotNull(deleteDialog);
        ClickButton(deleteDialog!, b => b.Classes.Contains("primary"));
        await host.SettleAsync(10);

        Assert.False(Directory.Exists(folderPath), "Delete did not remove the folder");
    }

    private static void ClickContextMenuItem(SolutionExplorerPanel explorer, string header)
    {
        var item = (explorer.Menu!.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>()
            .Single(i => i.Header as string == header);

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    // ------------------------------------------------------------------
    // Multiple tabs: a dirty marker on an edited one, "Close Others"
    // through the tab's own context menu leaving only it open, and closing
    // that last dirty tab through Ctrl+W asking Save/Don't Save/Cancel.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task MultipleTabsShowADirtyMarkerCloseOthersAndAskBeforeLosingEdits()
    {
        var project = WriteConsoleProject(_root);
        var fileA = Path.Combine(_root, "A.vb");
        var fileB = Path.Combine(_root, "B.vb");
        var fileC = Path.Combine(_root, "C.vb");
        File.WriteAllText(fileA, "Module A\nEnd Module\n");
        File.WriteAllText(fileB, "Module B\nEnd Module\n");
        File.WriteAllText(fileC, "Module C\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(fileA);
        await host.SettleAsync(3);
        await vm.OpenFileAsync(fileB);
        await host.SettleAsync(3);
        await vm.OpenFileAsync(fileC);
        await host.SettleAsync(5);

        Assert.Equal(3, vm.OpenDocuments.Count);

        var editedDoc = vm.OpenDocuments.Single(d => d.FilePath == fileB);
        editedDoc.Text += "\n' edited\n";
        var editedText = editedDoc.Text;

        Assert.EndsWith("•", editedDoc.Title);
        Assert.DoesNotContain(vm.OpenDocuments, d => d.FilePath != fileB && d.Title.EndsWith("•"));

        // "Close Others" from B's own tab context menu, via Dock's real menu.
        var tab = w.GetVisualDescendants()
            .OfType<Dock.Avalonia.Controls.DocumentTabStripItem>()
            .Single(t => (t.DataContext as IdeDocument)?.Document.FilePath == fileB);

        var closeOthers = (tab.ContextMenu?.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>().Single(i => i.Header as string == "Close Others");
        closeOthers.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        await host.SettleAsync(10);

        var remaining = Assert.Single(vm.OpenDocuments);
        Assert.Equal(fileB, remaining.FilePath);
        Assert.True(remaining.IsModified);
        Assert.Equal(editedText, remaining.Text);

        // Ctrl+W on the last, dirty tab: Save/Don't Save/Cancel, and Save
        // writes the edit to disk before the tab closes.
        w.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.W,
            KeyModifiers = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control,
            Source = w
        });

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ClickButton(dialog!, b => b.Classes.Contains("primary")); // Save
        await host.SettleAsync(10);

        Assert.Empty(vm.OpenDocuments);
        Assert.Equal(editedText, File.ReadAllText(fileB));
    }

    // ------------------------------------------------------------------
    // Settings: changing the theme and the editor font, through the real
    // controls, persists to the settings file on disk.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public void ChangingThemeAndFontInSettingsPersistsToDisk()
    {
        var store = new SettingsStore(_settings);
        var window = new SettingsWindow(store, null);
        window.Show();
        for (var i = 0; i < 5; i++) window.UpdateLayout();

        try
        {
            // Appearance category: Theme is the first of its ComboBoxes.
            window.SelectCategoryForTests(window.Categories.ToList().IndexOf("Appearance"));

            var themeCombo = window.CurrentPage!.GetVisualDescendants().OfType<ComboBox>().First();
            var wantedTheme = AppTheme.Dark;
            Assert.NotEqual(window.Settings.Appearance.Theme, wantedTheme);
            themeCombo.SelectedItem = IdeThemes.DisplayName(wantedTheme);

            Assert.Equal(wantedTheme, window.Settings.Appearance.Theme);

            // Editor category: font family (TextBox, applies on LostFocus)
            // and font size (NumericUpDown).
            window.SelectCategoryForTests(window.Categories.ToList().IndexOf("Editor"));

            var fontBox = window.CurrentPage!.GetVisualDescendants().OfType<TextBox>().First();
            var fontSize = window.CurrentPage!.GetVisualDescendants().OfType<NumericUpDown>().First();

            fontBox.Focus();
            fontBox.Text = "Cascadia Code";
            fontSize.Focus(); // Moving focus away is what applies the font family.

            fontSize.Value = 18;

            Assert.Equal("Cascadia Code", window.Settings.Editor.FontFamily);
            Assert.Equal(18, window.Settings.Editor.FontSize);
        }
        finally
        {
            window.Close();
        }

        // A fresh read from disk, the way the IDE itself would on the next
        // launch — not the in-memory IdeSettings the window already has.
        var reloaded = new SettingsStore(_settings).Load();
        Assert.Equal(AppTheme.Dark, reloaded.Appearance.Theme);
        Assert.Equal("Cascadia Code", reloaded.Editor.FontFamily);
        Assert.Equal(18, reloaded.Editor.FontSize);
    }

    // ------------------------------------------------------------------
    // Every top-level menu item runs without throwing, with no solution
    // open and with one open. Entries that spawn a real external process —
    // build, run, debug, "Run All" tests, git fetch/pull/push/init — and
    // Exit (which would close the window the rest of the loop needs) are
    // out of scope here: each already has its own dedicated tests, and
    // running them for real in a loop would be slow, network-touching or
    // destructive rather than a check for crashes.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task EveryTopLevelMenuItemRunsWithoutThrowingWithAndWithoutASolution()
    {
        await RunMenuSweepAsync(withSolution: false);
        await RunMenuSweepAsync(withSolution: true);
    }

    private async Task RunMenuSweepAsync(bool withSolution)
    {
        var excluded = new HashSet<string>
        {
            L(StringKeys.MenuFileExit),
            L(StringKeys.MenuBuildBuildSolution),
            L(StringKeys.MenuRunWithoutDebugging),
            L(StringKeys.MenuDebugStart),
            L(StringKeys.MenuDebugContinue),
            L(StringKeys.MenuDebugPause),
            L(StringKeys.MenuDebugStop),
            L(StringKeys.MenuDebugStepOver),
            L(StringKeys.MenuDebugStepInto),
            L(StringKeys.MenuDebugStepOut),
            L(StringKeys.MenuDebugToggleBreakpoint),
            L(StringKeys.MenuTestRunAll),
            L(StringKeys.MenuGitInitialize),
            L(StringKeys.MenuGitFetch),
            L(StringKeys.MenuGitPull),
            L(StringKeys.MenuGitPush),
        };

        string? project = null;
        if (withSolution)
        {
            var root = Directory.CreateTempSubdirectory("basalt-journey-sweep-").FullName;
            project = WriteConsoleProject(root);
        }

        using var host = project is null ? new TestWindow() : new TestWindow(project);
        await host.SettleAsync(10);

        var leaves = new List<MenuEntry>();
        void Collect(IReadOnlyList<MenuEntry> entries)
        {
            foreach (var entry in entries)
            {
                if (entry.IsSeparator) continue;
                if (entry.Children is { Count: > 0 } children) Collect(children);
                else leaves.Add(entry);
            }
        }
        Collect(host.Window.MenuEntriesForTests());

        var failures = new List<string>();

        foreach (var entry in leaves)
        {
            if (!entry.IsEnabled) continue;
            if (excluded.Contains(entry.Header)) continue;

            try
            {
                if (entry.Action is { } action) action();
                else entry.Command!.Execute(null);

                await host.SettleAsync(2);
            }
            catch (Exception ex)
            {
                failures.Add($"{entry.Header}: {ex}");
            }

            // Close any dialog the entry opened, so the next entry starts
            // from the plain window and dialogs do not pile up.
            foreach (var owned in host.Window.OwnedWindows.ToList())
            {
                try { owned.Close(); } catch { /* best effort cleanup */ }
            }
            await host.SettleAsync(2);
        }

        Assert.True(failures.Count == 0,
            $"[solution {(withSolution ? "open" : "none")}] these menu entries threw:\n"
            + string.Join("\n", failures));
    }

    // ------------------------------------------------------------------
    // View menu: every tool panel closed, then reopened one by one from the
    // View menu, each becoming visible again.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task TheViewMenuReopensEveryClosedToolPanel()
    {
        var project = WriteConsoleProject(_root);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var factory = (IdeDockFactory)typeof(MainWindow)
            .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(w)!;

        (IDockable Tool, string Header)[] panels =
        [
            (factory.SolutionExplorer, L(StringKeys.ToolSolutionExplorer)),
            (factory.Toolbox, L(StringKeys.ToolToolbox)),
            (factory.Problems, L(StringKeys.ToolProblems)),
            (factory.Output, L(StringKeys.ToolOutput)),
        ];

        var contents = (Dictionary<IDockable, Control>)typeof(MainWindow)
            .GetField("_contents", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(w)!;

        foreach (var (tool, header) in panels)
        {
            // Closing removes the dockable from its dock's own list — the
            // same thing F4ReopensAClosedPropertiesDockAndFocusesItsSelectedRow
            // (DesignerToolDockRoutingTests) checks, because a dockable's
            // Owner can stay set to the dock it used to live in even while
            // closed when siblings remain in it, so Owner alone does not say
            // whether the panel is still open.
            var dock = Assert.IsAssignableFrom<IDock>(tool.Owner);

            factory.CloseDockable(tool);
            await host.SettleAsync(5);

            Assert.DoesNotContain(tool, dock.VisibleDockables!);

            InvokeMenu(w, L(StringKeys.MenuView), header);
            await host.SettleAsync(8);

            Assert.Contains(tool, (tool.Owner as IDock)?.VisibleDockables ?? []);
            Assert.True(contents[tool].IsEffectivelyVisible, $"{header} did not become visible again");
        }
    }
}
