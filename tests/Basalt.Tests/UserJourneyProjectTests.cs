using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Core.Commands;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Workspace.Projects;
using Basalt.Workspace.Testing;

namespace Basalt.Tests;

/// <summary>
/// Project dialogs, Settings and the Test Explorer driven the way a person at
/// the IDE would drive them: a real <see cref="MainWindow"/>, the menus and
/// keys a user reaches for, dialogs answered through their own controls, and
/// a temporary Visual Basic project on disk.
///
/// Settings-touching tests share <see cref="SettingsFileCollection"/> because
/// BASALT_SETTINGS is process-wide.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyProjectTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-journey-project-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-project-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings =
        Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyProjectTests() =>
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

    private const string LibraryProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <MyType>Empty</MyType>
          </PropertyGroup>
        </Project>
        """;

    private static string WriteConsoleProject(string directory, string name)
    {
        Directory.CreateDirectory(directory);
        var projectPath = Path.Combine(directory, name + ".vbproj");
        File.WriteAllText(projectPath, ConsoleProjectXml);
        File.WriteAllText(Path.Combine(directory, "Program.vb"),
            "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");
        return projectPath;
    }

    /// <summary>A solution listing two projects, so both appear in Solution Explorer.</summary>
    private string WriteAppAndLibrarySolution()
    {
        WriteConsoleProject(Path.Combine(_root, "App"), "App");

        var libDirectory = Path.Combine(_root, "Lib");
        Directory.CreateDirectory(libDirectory);
        var lib = Path.Combine(libDirectory, "Lib.vbproj");
        File.WriteAllText(lib, LibraryProjectXml);
        File.WriteAllText(Path.Combine(libDirectory, "Greeter.vb"),
            "Public Module Greeter\n    Public Function Greet() As String\n        Return \"hi\"\n    End Function\nEnd Module\n");

        var solution = Path.Combine(_root, "Journey.slnx");
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="App/App.vbproj" />
              <Project Path="Lib/Lib.vbproj" />
            </Solution>
            """);

        return solution;
    }

    // ---- shared helpers -----------------------------------------------

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

    private static async Task<Window?> WaitForDialogAsync(TestWindow host, int maxPasses = 100)
    {
        Window? dialog = null;

        for (var i = 0; i < maxPasses && dialog is null; i++)
        {
            await host.SettleAsync(1);
            dialog = host.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static async Task<bool> WaitUntilAsync(TestWindow host, Func<bool> condition, TimeSpan timeout)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < timeout)
        {
            if (condition()) return true;
            await host.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>A button clicked the way a mouse does it: focus first, then the click.</summary>
    private static void ClickLikeAMouse(Button button)
    {
        button.Focus();
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static Button DefaultButton(Window dialog) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsDefault);

    private static Button ButtonWithText(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);

    /// <summary>The editor sitting under a label, the way the properties pages lay them out.</summary>
    private static Control EditorLabelled(Window dialog, string label)
    {
        var caption = dialog.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == label);
        var row = Assert.IsType<StackPanel>(caption.Parent);
        return row.Children[1];
    }

    /// <summary>Types into a labelled box and moves focus away, which is what applies it.</summary>
    private static void TypeInto(Window dialog, string label, string text, Control focusAfter)
    {
        var box = Assert.IsType<TextBox>(EditorLabelled(dialog, label));
        box.Focus();
        box.Text = text;
        focusAfter.Focus();
    }

    private static void ChooseInCombo(Window dialog, string label, string value)
    {
        var combo = Assert.IsType<ComboBox>(EditorLabelled(dialog, label));
        combo.SelectedItem = value;
    }

    private static void PressKey(MainWindow window, Key key, RawInputModifiers modifiers, PhysicalKey physical)
    {
        window.KeyPress(key, modifiers, physical, null);
        window.KeyRelease(key, modifiers, physical, null);
    }

    private static TextEditor TextEditorFor(MainWindow window, string filePath)
    {
        var code = window.CodeEditorForTests(filePath);
        Assert.NotNull(code);
        return code!.GetVisualDescendants().OfType<TextEditor>().Single();
    }

    private static T Field<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    private static IEnumerable<SolutionTreeNode> Flatten(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }

    private static void ClickContextMenuItem(SolutionExplorerPanel explorer, string header)
    {
        var item = (explorer.Menu!.ItemsSource ?? Array.Empty<object>())
            .OfType<MenuItem>()
            .Single(i => i.Header as string == header);

        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    private static void SetTextBox(Window dialog, string text) =>
        dialog.GetVisualDescendants().OfType<TextBox>().First().Text = text;

    // ==================================================================
    // 1. Project > Properties
    // ==================================================================

    // ------------------------------------------------------------------
    // Project > Properties: OK writes the application, compile and startup
    // edits to the .vbproj, and the project still builds afterwards.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ProjectPropertiesOkWritesTheEditsAndTheProjectStillBuilds()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectProperties));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var categories = dialog!.FindControl<ListBox>("CategoryList")!;

        // Application page.
        TypeInto(dialog, "Assembly name", "JourneyApp", categories);
        TypeInto(dialog, "Root namespace", "Journey", categories);
        TypeInto(dialog, "Startup object", "Journey.Program", categories);

        // Compile page: the target framework and Option Strict.
        categories.SelectedIndex = 1;
        await host.SettleAsync(3);
        TypeInto(dialog, "Target framework", "net8.0", categories);
        ChooseInCombo(dialog, "Option Strict", "On");

        ClickLikeAMouse(ButtonWithText(dialog, "Save"));
        await host.SettleAsync(10);

        Assert.DoesNotContain(dialog, w.OwnedWindows.ToList());

        var xml = await File.ReadAllTextAsync(project);
        Assert.Contains("<AssemblyName>JourneyApp</AssemblyName>", xml);
        Assert.Contains("<RootNamespace>Journey</RootNamespace>", xml);
        Assert.Contains("<StartupObject>Journey.Program</StartupObject>", xml);
        Assert.Contains("<TargetFramework>net8.0</TargetFramework>", xml);
        Assert.Contains("<OptionStrict>On</OptionStrict>", xml);

        var build = await new MsBuildBuildService().BuildAsync(project);
        Assert.True(build.Succeeded, string.Join("\n", build.Diagnostics.Select(d => d.Message)));
    }

    // ------------------------------------------------------------------
    // Project > Properties: pressing Enter in a box (its default button is
    // Save) keeps what was typed, the way clicking Save does.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ASavedValueTypedWhileTheFocusStaysInTheBoxIsKept()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectProperties));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        // Typed in the box, which keeps the keyboard focus; Save is the
        // default button, so Enter is what a keyboard user presses to save.
        var box = Assert.IsType<TextBox>(EditorLabelled(dialog!, "Assembly name"));
        box.Focus();
        box.Text = "EnterSaved";

        // Enter activates the default button without moving focus, so the
        // box is still the focused control when Save runs.
        ButtonWithText(dialog!, "Save").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await host.SettleAsync(10);

        var xml = await File.ReadAllTextAsync(project);
        Assert.Contains("<AssemblyName>EnterSaved</AssemblyName>", xml);
    }

    // ------------------------------------------------------------------
    // Project > Properties: Cancel after editing changes nothing on disk.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task CancelLeavesTheProjectFileExactlyAsItWas()
    {
        var project = WriteConsoleProject(_root, "App");
        var before = await File.ReadAllTextAsync(project);

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectProperties));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var categories = dialog!.FindControl<ListBox>("CategoryList")!;
        TypeInto(dialog, "Assembly name", "Abandoned", categories);
        categories.SelectedIndex = 1;
        await host.SettleAsync(3);
        TypeInto(dialog, "Target framework", "net8.0", categories);

        ClickLikeAMouse(ButtonWithText(dialog, "Cancel"));
        await host.SettleAsync(10);

        Assert.Equal(before, await File.ReadAllTextAsync(project));
    }

    // ==================================================================
    // 2. Project > References…
    // ==================================================================

    // ------------------------------------------------------------------
    // Project > References…: checking a project, adding a package, then
    // unchecking and removing both leave the .vbproj as the dialog showed.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task ReferencesDialogAddsAndRemovesAProjectAndAPackageReference()
    {
        var solution = WriteAppAndLibrarySolution();
        var app = Path.Combine(_root, "App", "App.vbproj");

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var explorer = w.SolutionExplorerForTests!;

        await WaitUntilAsync(host, () => Flatten(vm.Explorer.Roots).Count(n => n.Name.EndsWith(".vbproj")) >= 2,
            TimeSpan.FromSeconds(10));

        var appNode = Flatten(vm.Explorer.Roots).Single(n => n.Name == "App.vbproj");
        explorer.TreeForTests.SelectedItem = appNode;

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectReferences));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        // Projects tab: tick Lib, then OK.
        var lib = dialog!.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Lib");
        lib.IsChecked = true;
        ClickLikeAMouse(DefaultButton(dialog));
        await host.SettleAsync(10);

        var projectReferences = ProjectProperties.Load(app).ProjectReferences;
        Assert.Single(projectReferences);
        Assert.EndsWith("Lib.vbproj", projectReferences[0]);

        // Packages tab: a package with an explicit version.
        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectReferences));
        dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        var tabs = dialog!.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 2;
        await host.SettleAsync(3);

        var packageId = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.PlaceholderText == L(StringKeys.ReferencesPackageId));
        var packageVersion = dialog.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.PlaceholderText == L(StringKeys.ReferencesPackageVersion));

        packageId.Text = "Newtonsoft.Json";
        packageVersion.Text = "13.0.3";
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.ReferencesAdd)));
        await host.SettleAsync(3);

        ClickLikeAMouse(DefaultButton(dialog));
        await host.SettleAsync(10);

        var packages = ProjectProperties.Load(app).PackageReferences;
        Assert.Contains(packages, p => p.Id == "Newtonsoft.Json" && p.Version == "13.0.3");

        // Reopen: the package is listed; remove it, untick Lib, OK.
        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectReferences));
        dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        dialog!.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 2;
        await host.SettleAsync(3);

        var listed = dialog.GetVisualDescendants().OfType<ListBox>().Single();
        listed.SelectedItem = listed.ItemsSource!.Cast<object>()
            .Single(item => item.ToString()!.Contains("Newtonsoft.Json", StringComparison.Ordinal));
        ClickLikeAMouse(ButtonWithText(dialog, L(StringKeys.ReferencesRemove)));
        await host.SettleAsync(3);

        dialog.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 0;
        await host.SettleAsync(3);
        dialog.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Lib").IsChecked = false;

        ClickLikeAMouse(DefaultButton(dialog));
        await host.SettleAsync(10);

        var after = ProjectProperties.Load(app);
        Assert.Empty(after.ProjectReferences);
        Assert.DoesNotContain(after.PackageReferences, p => p.Id == "Newtonsoft.Json");
    }

    // ==================================================================
    // 3. Solution Explorer: New File / Folder, Add Window
    // ==================================================================

    // ------------------------------------------------------------------
    // Project > Add Window with the second project selected: the window's
    // files land in that project's folder, are listed, and open.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task AddWindowCreatesTheFormInTheSelectedProjectAndOpensIt()
    {
        var solution = WriteAppAndLibrarySolution();

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var explorer = w.SolutionExplorerForTests!;

        await WaitUntilAsync(host, () => Flatten(vm.Explorer.Roots).Any(n => n.Name == "Lib.vbproj"),
            TimeSpan.FromSeconds(10));

        explorer.TreeForTests.SelectedItem = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Lib.vbproj");

        InvokeMenu(w, L(StringKeys.MenuProject), L(StringKeys.MenuProjectAddWindow));
        var dialog = await WaitForDialogAsync(host);
        Assert.NotNull(dialog);

        SetTextBox(dialog!, "LibWindow");
        ClickLikeAMouse(dialog!.FindControl<Button>("CreateButton")!);
        await host.SettleAsync(15);

        var libDirectory = Path.Combine(_root, "Lib");
        Assert.True(File.Exists(Path.Combine(libDirectory, "LibWindow.axaml")), "the window is not in the selected project's folder");
        Assert.True(File.Exists(Path.Combine(libDirectory, "LibWindow.axaml.vb")), "the code-behind is not in the selected project's folder");
        Assert.False(File.Exists(Path.Combine(_root, "App", "LibWindow.axaml")));

        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Name == "LibWindow.axaml");
        Assert.Equal(Path.Combine(libDirectory, "LibWindow.axaml"), vm.ActiveDocument?.FilePath);
    }

    // ------------------------------------------------------------------
    // Solution Explorer: New Folder, then New File inside it, creates both
    // on disk in the right place, lists them and opens the file.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task NewFileInANewFolderLandsInItAndOpens()
    {
        var project = WriteConsoleProject(_root, "App");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var explorer = w.SolutionExplorerForTests!;

        explorer.TreeForTests.SelectedItem = vm.Explorer.Roots.Single();
        ClickContextMenuItem(explorer, "New Folder…");
        var folderDialog = await WaitForDialogAsync(host);
        Assert.NotNull(folderDialog);
        SetTextBox(folderDialog!, "Models");
        ClickLikeAMouse(DefaultButton(folderDialog));
        await host.SettleAsync(10);

        var folderNode = Flatten(vm.Explorer.Roots).Single(n => n.Name == "Models");
        explorer.TreeForTests.SelectedItem = folderNode;
        ClickContextMenuItem(explorer, "New File…");
        var fileDialog = await WaitForDialogAsync(host);
        Assert.NotNull(fileDialog);
        SetTextBox(fileDialog!, "Customer.vb");
        ClickLikeAMouse(DefaultButton(fileDialog));
        await host.SettleAsync(10);

        var created = Path.Combine(_root, "Models", "Customer.vb");
        Assert.True(File.Exists(created), "the file was not created inside the new folder");
        Assert.Contains(Flatten(vm.Explorer.Roots), n => n.Path == created);
        Assert.Equal(created, vm.ActiveDocument?.FilePath);
    }

    // ==================================================================
    // 4. Settings: keyboard scheme
    // ==================================================================

    /// <summary>
    /// Tools > Settings, the Keyboard page, the Scheme box set to
    /// <paramref name="scheme"/>, then the window closed the way a user does.
    /// </summary>
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

    // ------------------------------------------------------------------
    // Settings > Keyboard: switching the scheme to Visual Studio makes Ctrl+Y
    // redo in the open code editor, without a restart.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task SwitchingToVisualStudioMakesCtrlYRedoInTheOpenEditor()
    {
        var project = WriteConsoleProject(_root, "App");
        var program = Path.Combine(_root, "Program.vb");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(program);
        await host.SettleAsync(5);

        var editor = TextEditorFor(w, program);
        var original = editor.Text;
        editor.CaretOffset = original.IndexOf("End Sub", StringComparison.Ordinal);
        editor.TextArea.Focus();
        w.KeyTextInput("' typed");
        var typed = editor.Text;
        Assert.NotEqual(original, typed);

        PressKey(w, Key.Z, RawInputModifiers.Control, PhysicalKey.Z);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == original, TimeSpan.FromSeconds(3)));

        await ChooseKeyboardSchemeAsync(host, "VisualStudio");
        await host.SettleAsync(5);

        PressKey(w, Key.Y, RawInputModifiers.Control, PhysicalKey.Y);
        Assert.True(await WaitUntilAsync(host, () => editor.Text == typed, TimeSpan.FromSeconds(3)),
            "Ctrl+Y did not redo after switching to the Visual Studio scheme");
    }

    // ------------------------------------------------------------------
    // Settings > Keyboard: switching to VS Code applies its gestures. Format
    // Document is Ctrl+Shift+I there, and Ctrl+K, Ctrl+D in the Basalt scheme,
    // and the choice is still in force in a new window.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task VsCodeSchemeGesturesApplyAndSurviveANewWindow()
    {
        var project = WriteConsoleProject(_root, "App");
        var program = Path.Combine(_root, "Program.vb");
        File.WriteAllText(program, "Module Program\nSub Main()\nEnd Sub\nEnd Module\n");

        using (var host = new TestWindow(project))
        {
            await host.SettleAsync(10);
            var w = host.Window;
            var vm = (MainWindowViewModel)w.DataContext!;

            await vm.OpenFileAsync(program);
            await host.SettleAsync(5);

            var editor = TextEditorFor(w, program);
            editor.TextArea.Focus();

            await ChooseKeyboardSchemeAsync(host, "VisualStudioCode");
            editor.TextArea.Focus();

            PressKey(w, Key.I, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.I);
            Assert.True(await WaitUntilAsync(host, () => editor.Text.Contains("    Sub Main()", StringComparison.Ordinal),
                TimeSpan.FromSeconds(5)),
                "Ctrl+Shift+I did not format the document after switching to the VS Code scheme");
        }

        Assert.Equal("VisualStudioCode", new SettingsStore(_settings).Load().Keyboard.Scheme);

        using var fresh = new TestWindow(project);
        await fresh.SettleAsync(10);
        var registry = Field<CommandRegistry>(fresh.Window, "_commands");
        Assert.Equal("Ctrl+Shift+I", registry.GestureFor(IdeCommands.EditFormatDocument));
    }

    // ==================================================================
    // 5. Test Explorer
    // ==================================================================

    private const string TestProjectXml = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
            <RootNamespace></RootNamespace>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit.v3" Version="3.2.2" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
        </Project>
        """;

    private static string CalculatorSource(int expectedSum) => $"""
        Imports Xunit

        Namespace Sample
            Public Class CalculatorTests

                <Fact>
                Public Sub AddsTwoNumbers()
                    Assert.Equal(4, 2 + 2)
                End Sub

                <Fact>
                Public Sub ThisOneFails()
                    Assert.Equal({expectedSum}, 2 + 2)
                End Sub

            End Class
        End Namespace
        """;

    private string WriteTestSolution(out string calculator)
    {
        var testDirectory = Path.Combine(_root, "VbTests");
        Directory.CreateDirectory(testDirectory);

        File.WriteAllText(Path.Combine(testDirectory, "VbTests.vbproj"), TestProjectXml);
        calculator = Path.Combine(testDirectory, "CalculatorTests.vb");
        File.WriteAllText(calculator, CalculatorSource(5));

        var solution = Path.Combine(_root, "Tests.slnx");
        File.WriteAllText(solution, """
            <Solution>
              <Project Path="VbTests/VbTests.vbproj" />
            </Solution>
            """);

        return solution;
    }

    private static TestNode LeafNamed(TestExplorerPanel panel, string name) =>
        panel.Roots.Single().Children.Single(c => c.Name == "CalculatorTests")
            .Children.Single(l => l.Name == name);

    private static Button TestButton(TestExplorerPanel panel, string text) =>
        panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == text);

    private static async Task<bool> WaitForSummaryAsync(TestWindow host, TestExplorerPanel panel, string fragment) =>
        await WaitUntilAsync(host, () => panel.Summary.Contains(fragment, StringComparison.Ordinal),
            TimeSpan.FromSeconds(240));

    // ------------------------------------------------------------------
    // Test Explorer: an xUnit VB project is discovered on open, Run All
    // reports the mix, a failure shows its message, double-clicking it opens
    // the test's line, and running it again after the fix turns it green.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task DiscoverRunAFailureOpenItAndRerunAfterTheFix()
    {
        var solution = WriteTestSolution(out var calculator);

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (MainWindowViewModel)w.DataContext!;
        var panel = Field<TestExplorerPanel>(w, "_tests");

        // The panel only has controls in the visual tree once its tool is shown.
        InvokeMenu(w, L(StringKeys.MenuTest), L(StringKeys.ToolTests));
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 2, TimeSpan.FromSeconds(240)),
            "the xUnit tests were not discovered: " + string.Join(", ", vm.OpenDocuments.Select(d => d.Title)));
        Assert.Contains("2 tests", panel.Summary);

        TestButton(panel, L(StringKeys.TestsRunAll)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await WaitForSummaryAsync(host, panel, "1 passed, 1 failed"), panel.Summary);

        var failing = LeafNamed(panel, "ThisOneFails");
        Assert.Equal(TestOutcome.Failed, failing.Outcome);

        // Select it, then read its message the way the detail pane shows it.
        panel.SelectForTests(failing);
        Assert.Contains("Assert.Equal", panel.Details);

        // Double-click opens the file at the failing line.
        var tree = panel.GetVisualDescendants().OfType<TreeView>().Single();
        tree.SelectedItem = failing;
        tree.RaiseEvent(new Avalonia.Input.TappedEventArgs(Control.DoubleTappedEvent, null!));
        Assert.True(await WaitUntilAsync(host, () => vm.ActiveDocument?.FilePath is not null, TimeSpan.FromSeconds(10)));

        Assert.Equal(Path.GetFileName(calculator), Path.GetFileName(vm.ActiveDocument!.FilePath));

        // ... and puts the caret on the failing line, not on line 1 (OpenAtAsync used to post a
        // single GoToLine that ran before the editor existed).
        var failingLine = panel.SelectedResult!.Line;
        Assert.True(failingLine > 1, $"the failure was reported on line {failingLine}.");
        var editor = TextEditorFor(w, vm.ActiveDocument!.FilePath);
        Assert.True(await WaitUntilAsync(host,
                () => editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber == failingLine, TimeSpan.FromSeconds(10)),
            $"the caret is on line {editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber}, not on the failing line {failingLine}.");

        // Fix the expectation and run the failed tests again.
        File.WriteAllText(calculator, CalculatorSource(4));
        TestButton(panel, L(StringKeys.TestsRunFailed)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await WaitForSummaryAsync(host, panel, "2 passed, 0 failed"), panel.Summary);

        Assert.Equal(TestOutcome.Passed, LeafNamed(panel, "ThisOneFails").Outcome);
        Assert.Empty(panel.FailedTests);
    }

    // ------------------------------------------------------------------
    // Test Explorer: Run Selected runs only the chosen test; the others keep
    // the outcome they already had.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task RunSelectedRunsOnlyTheTestPicked()
    {
        var solution = WriteTestSolution(out _);

        using var host = new TestWindow(solution);
        await host.SettleAsync(10);
        var w = host.Window;
        var panel = Field<TestExplorerPanel>(w, "_tests");

        // The panel only has controls in the visual tree once its tool is shown.
        InvokeMenu(w, L(StringKeys.MenuTest), L(StringKeys.ToolTests));
        await host.SettleAsync(5);

        Assert.True(await WaitUntilAsync(host, () => panel.AllTests.Count == 2, TimeSpan.FromSeconds(240)));

        TestButton(panel, L(StringKeys.TestsRunAll)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await WaitForSummaryAsync(host, panel, "1 passed, 1 failed"), panel.Summary);

        panel.SelectForTests(LeafNamed(panel, "AddsTwoNumbers"));
        TestButton(panel, L(StringKeys.TestsRunSelected)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await WaitForSummaryAsync(host, panel, "1 passed, 1 failed"), panel.Summary);

        Assert.Equal(TestOutcome.Passed, LeafNamed(panel, "AddsTwoNumbers").Outcome);
        Assert.Equal(TestOutcome.Failed, LeafNamed(panel, "ThisOneFails").Outcome);
    }
}
