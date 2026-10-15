using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Basalt.Core.Features;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Solution Explorer's Add New Item, the File &gt; Open filter, and the
/// shortcuts the menus show, driven the way a person at the IDE would drive
/// them: a real <see cref="MainWindow"/>, the context menu and Project menu,
/// the Add New Item dialog answered through its own controls, and the Settings
/// window for a keyboard scheme.
///
/// Settings-touching tests share <see cref="SettingsFileCollection"/> because
/// BASALT_SETTINGS is process-wide.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyItemsTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-items-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-items-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyItemsTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ---- fixtures -----------------------------------------------------

    private const string ConsoleProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>Exe</OutputType>
            <TargetFramework>net10.0</TargetFramework>
            <MyType>Empty</MyType>
          </PropertyGroup>
        </Project>
        """;

    internal static string WriteConsoleProject(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, name + ".vbproj");
        File.WriteAllText(projectPath, ConsoleProjectXml);
        File.WriteAllText(Path.Combine(directory, "Program.vb"), "Module Program\nSub Main()\nEnd Sub\nEnd Module\n");
        return projectPath;
    }

    internal static string L(string key) => Localizer.Get(key);

    internal static void InvokeMenu(MainWindow window, params string[] path)
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

    internal static async Task<Window?> WaitForDialogAsync(TestWindow host, int maxPasses = 100)
    {
        Window? dialog = null;

        for (var i = 0; i < maxPasses && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    internal static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    internal static void ClickLikeAMouse(Button button) =>
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static Button ButtonWithText(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);

    internal static void ClickContextMenuItem(SolutionExplorerPanel explorer, string header)
    {
        var item = (explorer.Menu!.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>()
            .Single(i => i.Header as string == header);

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    internal static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    /// <summary>The names of the templates the dialog lists now, in its list order.</summary>
    internal static IReadOnlyList<string> OfferedTemplates(Window dialog) =>
        TemplateList(dialog).Items.OfType<ListBoxItem>()
            .Select(item => ((ItemTemplateDefinition)item.Tag!).Name)
            .ToList();

    internal static ListBox TemplateList(Window dialog) =>
        dialog.GetVisualDescendants().OfType<ListBox>().Single(list => list.Name == "AddItemTemplates");

    /// <summary>Shows the templates of a category, as a click on it in the tree does.</summary>
    internal static void SelectCategory(Window dialog, ItemCategory category)
    {
        var tree = dialog.GetVisualDescendants().OfType<TreeView>().Single(t => t.Name == "AddItemCategories");
        tree.SelectedItem = tree.Items.OfType<TreeViewItem>().Single(item => item.Tag is ItemCategory c && c == category);
    }

    /// <summary>Picks a template in the Add New Item dialog, in its category, as clicks on them do.</summary>
    internal static void ChooseTemplate(Window dialog, string templateId)
    {
        SelectCategory(dialog, ItemTemplates.ById(templateId).Category);

        var list = TemplateList(dialog);
        list.SelectedItem = list.Items.OfType<ListBoxItem>()
            .Single(item => item.Tag is ItemTemplateDefinition chosen && chosen.Id == templateId);
    }

    internal static void TypeName(Window dialog, string name) =>
        dialog.GetVisualDescendants().OfType<TextBox>().Single(box => box.Name == "AddItemName").Text = name;

    /// <summary>The explorer's Add New Item, from its context menu on the selected node.</summary>
    internal static async Task<Window> OpenAddNewItemFromExplorerAsync(TestWindow host, SolutionTreeNode node)
    {
        var explorer = host.Window.SolutionExplorerForTests!;
        explorer.TreeForTests.SelectedItem = node;
        ClickContextMenuItem(explorer, L(StringKeys.ExplorerAddNewItem));

        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);
        return dialog!;
    }

    // ==================================================================
    // Add New Item
    // ==================================================================

    // ------------------------------------------------------------------
    // Solution Explorer > Add New Item: each VB template writes its file
    // into the project folder, the file is listed and opens in the editor,
    // and the project builds with every one of them in it.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task EachVisualBasicItemIsWrittenListedOpenedAndTheProjectBuilds()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        var items = new (string Template, string FileName, string Declaration)[]
        {
            (ItemTemplateIds.Class, "Customer.vb", "Public Class Customer"),
            (ItemTemplateIds.Module, "Helpers.vb", "Module Helpers"),
            (ItemTemplateIds.Interface, "IStore.vb", "Public Interface IStore"),
            (ItemTemplateIds.EmptyFile, "Scratch.vb", "")
        };

        foreach (var (template, fileName, declaration) in items)
        {
            var dialog = await OpenAddNewItemFromExplorerAsync(host, vm.Explorer.Roots.Single());

            ChooseTemplate(dialog, template);
            TypeName(dialog, fileName);
            ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
            await host.SettleAsync(10);

            var path = Path.Combine(_root, fileName);
            Assert.True(File.Exists(path), $"{template} did not write {fileName} into the project folder");
            Assert.Contains(declaration, File.ReadAllText(path));
            Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == path);
            Assert.Equal(path, vm.ActiveDocument?.FilePath);
        }

        var build = await new MsBuildBuildService().BuildAsync(project);
        Assert.True(build.Succeeded, string.Join("\n", build.Diagnostics.Select(d => d.Message)));
    }

    // ------------------------------------------------------------------
    // Add New Item from Project, with the project selected: the item lands
    // in that project's folder, as Project > Add Window does.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ProjectMenuAddNewItemWritesIntoTheSelectedProjectFolder()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        w.SolutionExplorerForTests!.TreeForTests.SelectedItem = vm.Explorer.Roots.Single();

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectAddItem));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        ChooseTemplate(dialog!, ItemTemplateIds.Class);
        TypeName(dialog!, "Ledger");
        ClickLikeAMouse(ButtonWithText(dialog!, L(StringKeys.AddItemCreate)));
        await host.SettleAsync(10);

        var path = Path.Combine(_root, "Ledger.vb");
        Assert.True(File.Exists(path), "the item was not written into the selected project's folder");
        Assert.Equal(path, vm.ActiveDocument?.FilePath);
    }

    // ------------------------------------------------------------------
    // A Class named End or a Module named Sub would not compile: the dialog
    // refuses a Visual Basic reserved word with its message, keeps itself
    // open, and writes nothing.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AClassNamedEndOrAModuleNamedSubIsRefusedAndNothingIsWritten()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        foreach (var (template, name) in new[] { (ItemTemplateIds.Class, "End"), (ItemTemplateIds.Module, "Sub") })
        {
            var dialog = await OpenAddNewItemFromExplorerAsync(host, vm.Explorer.Roots.Single());

            ChooseTemplate(dialog, template);
            TypeName(dialog, name);
            ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.AddItemCreate)));
            await host.SettleAsync(5);

            Assert.Contains(dialog, host.Window.OwnedWindows.ToList());
            Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == L(StringKeys.AddItemNameInvalid));
            Assert.False(File.Exists(Path.Combine(_root, name + ".vb")), $"{name}.vb was written");

            ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.ButtonCancel)));
            await host.SettleAsync(3);
        }
    }

    // ------------------------------------------------------------------
    // A project that cannot build Windows Forms still sees the Windows Form
    // and UserControl items, marked as not for it: they are not hidden, and
    // adding one is allowed, with a warning (see AddItemDialogTests).
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AConsoleProjectSeesTheWindowsFormItemsMarkedAsNotForIt()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;

        var dialog = await OpenAddNewItemFromExplorerAsync(host, vm.Explorer.Roots.Single());

        var offered = OfferedTemplates(dialog);
        Assert.Contains(L(StringKeys.AddItemClass), offered);
        Assert.Contains(L(StringKeys.AddItemEmptyFile), offered);

        SelectCategory(dialog, ItemCategory.WindowsForms);
        await host.SettleAsync(3);
        var windowsForms = OfferedTemplates(dialog);
        Assert.Contains(L(StringKeys.AddItemWindowsForm), windowsForms);
        Assert.Contains(L(StringKeys.AddItemUserControl), windowsForms);

        var marked = TemplateList(dialog).Items.OfType<ListBoxItem>()
            .Where(item => item.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Name == "AddItemRowMisfit"))
            .Select(item => ((ItemTemplateDefinition)item.Tag!).Id)
            .ToList();
        Assert.Contains(ItemTemplateIds.WindowsForm, marked);
        Assert.Contains(ItemTemplateIds.UserControl, marked);

        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.ButtonCancel)));
        await host.SettleAsync(3);
        Assert.False(File.Exists(Path.Combine(_root, "Class1.vb")), "Cancel wrote a file");
    }

    // ==================================================================
    // File > Open
    // ==================================================================

    // ------------------------------------------------------------------
    // File > Open lists the QuickBASIC programs the language registry
    // claims, so a lone .bas file that opens fine by the Recent list can be
    // found from the dialog too. The projects and solutions it listed still are.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task FileOpenFilterListsTheQuickBasicExtensionsTheRegistryClaims()
    {
        using var host = new TestWindow(features: new IdeFeatures { LegacyBasic = true });
        await host.SettleAsync(10);

        var filters = host.Window.SolutionOpenFilters();

        var patterns = filters[0].Patterns!;
        Assert.Contains("*.bas", patterns);
        Assert.Contains("*.qb", patterns);
        Assert.Contains("*.sln", patterns);
        Assert.Contains("*.vbproj", patterns);
    }

    // ==================================================================
    // Menu shortcuts follow the keyboard scheme
    // ==================================================================

    // ------------------------------------------------------------------
    // Settings > Keyboard: switching to Visual Studio changes what the
    // menus show. Redo becomes Ctrl+Y, and Edit > Replace stays on Ctrl+H
    // rather than the Ctrl+Shift+H the menu used to print while Ctrl+H
    // was the bound key. The menu the platform holds shows the same.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task MenusShowTheGesturesOfTheSchemeInForceAndRefreshWhenItChanges()
    {
        using var host = new TestWindow();
        await host.SettleAsync(10);
        var w = host.Window;

        // Basalt is the default: Redo is Ctrl+Shift+Z, Replace Ctrl+H.
        AssertGesture(InstalledGesture(w, L(StringKeys.MenuEdit), L(StringKeys.MenuEditRedo)), Key.Z, KeyModifiers.Shift);
        AssertGesture(InstalledGesture(w, L(StringKeys.MenuEdit), L(StringKeys.MenuEditReplace)), Key.H, KeyModifiers.None);

        await ChooseKeyboardSchemeAsync(host, "VisualStudio");

        AssertGesture(InstalledGesture(w, L(StringKeys.MenuEdit), L(StringKeys.MenuEditRedo)), Key.Y, KeyModifiers.None);
        AssertGesture(InstalledGesture(w, L(StringKeys.MenuEdit), L(StringKeys.MenuEditReplace)), Key.H, KeyModifiers.None);

        // Format Document is a chord under Visual Studio: a menu item cannot
        // show one, so it shows no shortcut rather than half of it.
        Assert.Null(InstalledGesture(w, L(StringKeys.MenuEdit), L(StringKeys.MenuEditFormatDocument)));

        // The model the menus are built from agrees with what is installed.
        var redo = w.MenuEntriesForTests()
            .Single(e => e.Header == L(StringKeys.MenuEdit)).Children!
            .Single(e => e.Header == L(StringKeys.MenuEditRedo));
        AssertGesture(redo.Gesture, Key.Y, KeyModifiers.None);
    }

    /// <summary>Tools > Settings, the Keyboard page, the Scheme box set to <paramref name="scheme"/>.</summary>
    private async Task ChooseKeyboardSchemeAsync(TestWindow host, string scheme)
    {
        InvokeMenu(host.Window, L(StringKeys.MenuTools), L(StringKeys.MenuToolsSettings));
        var window = await WaitForDialogAsync(host) as SettingsWindow;
        Assert.NotNull(window);

        try
        {
            window!.SelectCategoryForTests(window.Categories.ToList().IndexOf("Keyboard"));
            await host.SettleAsync(3);

            var combo = window.CurrentPage!.GetVisualDescendants().OfType<ComboBox>()
                .First(c => c.ItemsSource is IEnumerable<string> names && names.Contains("VisualStudio"));
            combo.SelectedItem = scheme;

            Assert.Equal(scheme, window.Settings.Keyboard.Scheme);
            await host.SettleAsync(3);
        }
        finally
        {
            window!.Close();
        }

        await host.SettleAsync(5);
    }

    /// <summary>
    /// The shortcut a menu item shows as the platform has it installed: the
    /// managed menu on Windows and Linux, the system menu bar on macOS.
    /// </summary>
    internal static KeyGesture? InstalledGesture(MainWindow window, string menu, string item)
    {
        if (IdeMenu.UsesSystemMenuBar)
        {
            var top = NativeMenu.GetMenu(window)!.Items.OfType<NativeMenuItem>().Single(i => i.Header == menu);
            return top.Menu!.Items.OfType<NativeMenuItem>().Single(i => i.Header == item).Gesture;
        }

        var bar = window.FindControl<Menu>("WindowMenu")!;
        var top2 = bar.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == menu);
        return top2.ItemsSource!.OfType<MenuItem>().Single(i => i.Header as string == item).InputGesture;
    }

    /// <summary>
    /// The gesture the platform shows for a key: the command modifier is Cmd on
    /// macOS and Ctrl elsewhere, so the expectation says Ctrl and lets the
    /// platform say which.
    /// </summary>
    internal static void AssertGesture(KeyGesture? gesture, Key key, KeyModifiers ctrlAndMore)
    {
        Assert.NotNull(gesture);
        Assert.Equal(key, gesture!.Key);

        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        var expected = ctrlAndMore == KeyModifiers.None ? command : command | ctrlAndMore;
        Assert.Equal(expected, gesture.KeyModifiers);
    }
}
