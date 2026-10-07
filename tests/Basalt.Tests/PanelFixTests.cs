using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Core.Services;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Dock.Model.Core;

namespace Basalt.Tests;

/// <summary>
/// Regression tests for defects found by the panels audit
/// (docs/planning/PANELS-AUDIT.md): the Problems row not moving the caret,
/// a search hit opening the wrong editor, the Watch panel being unreachable
/// and "Run All" obeying a selection instead of running everything.
///
/// Each test drives the real control the user acts on (a grid row, a search
/// result, a menu entry, a button) rather than calling the private method
/// the window uses internally.
/// </summary>
/// <remarks>Run All builds a real test project, so this shares the collection
/// that serialises every test running dotnet build or restore.</remarks>
[Collection("PackageBuild")]
public sealed class PanelFixTests
{
    private static T Field<T>(object o, string name) =>
        (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;

    private static void Invoke(MainWindow w, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = w.MenuEntriesForTests();
        MenuEntry? hit = null;
        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException("menu entry not found: " + header);
            level = hit.Children ?? [];
        }
        if (hit!.Action is { } a) a();
        else hit.Command!.Execute(null);
    }

    private static string L(string key) => Localizer.Get(key);

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
        return projectPath;
    }

    /// <summary>
    /// Opening a Problems row must move the caret to the diagnostic's own
    /// line and column, not leave it at line 1.
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningAProblemsRowMovesTheCaretToItsLineAndColumn()
    {
        var root = Directory.CreateTempSubdirectory("basalt-problems-").FullName;
        var project = WriteProject(root);
        var file = Path.Combine(root, "Program.vb");

        // Ten lines so line 7 is unambiguous; column 9 lands just past the
        // indentation on that line, matching the bug report (7:9).
        File.WriteAllText(file, string.Join("\n",
            "Module Program", "    Sub Main()", "        Dim a = 1", "        Dim b = 2",
            "        Dim c = 3", "        Dim d = 4", "        Dim broken = e", "        Dim f = 6",
            "    End Sub", "End Module"));

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (Basalt.Shell.ViewModels.MainWindowViewModel)w.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        // A synthetic diagnostic rather than a real build: the panel only
        // needs a row to open, and a real compiler error would not promise a
        // specific column.
        vm.Diagnostics.ReplaceAll([
            new IdeDiagnostic("BC0001", "'e' is not declared.", DiagnosticSeverity.Error, file, 7, 9)
        ]);

        var problems = Field<Dictionary<IDockable, Control>>(w, "_contents")
            .Values.OfType<ProblemsPanel>().First();

        await host.SettleAsync(5);
        Assert.Single(problems.VisibleRows);

        problems.Grid.SelectedItem = problems.VisibleRows[0];
        problems.Grid.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter
        });

        await host.SettleAsync(10);

        var editor = w.CurrentEditorForTests();
        Assert.NotNull(editor);

        var line = editor!.Document.GetLineByOffset(editor.CaretOffset);
        Assert.Equal(7, line.LineNumber);
        Assert.Equal(9, editor.CaretOffset - line.Offset + 1);
    }

    /// <summary>
    /// A search hit must position the caret in the editor of the file it
    /// belongs to, even when another document is the one in front.
    /// </summary>
    [AvaloniaFact]
    public async Task OpeningASearchHitPositionsTheCaretInThatFilesEditor()
    {
        var root = Directory.CreateTempSubdirectory("basalt-searchhit-").FullName;
        var project = WriteProject(root);
        var other = Path.Combine(root, "Other.vb");
        var helper = Path.Combine(root, "Helper.vb");
        var program = Path.Combine(root, "Program.vb");

        File.WriteAllText(other, "Module Other\n    Sub Noop()\n    End Sub\nEnd Module\n");
        File.WriteAllText(helper, "Module Helper\n    Sub Greet()\n        Dim needle = 1\n    End Sub\nEnd Module\n");
        File.WriteAllText(program, "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;
        var vm = (Basalt.Shell.ViewModels.MainWindowViewModel)w.DataContext!;

        // Three documents open, none of them the hit's file by coincidence
        // of being first or active: Other opened first (so it is first in
        // the document map the old, broken code read from), Helper second
        // (the hit's own file, but neither first nor active), Program last
        // and active. Whatever the bug picks by position rather than by
        // identity, this setup makes visible.
        await vm.OpenFileAsync(other);
        await host.SettleAsync(5);
        await vm.OpenFileAsync(helper);
        await host.SettleAsync(5);
        await vm.OpenFileAsync(program);
        await host.SettleAsync(5);

        Assert.Equal(program, vm.ActiveDocument?.FilePath);

        var search = Field<SearchPanel>(w, "_search");
        search.SearchRoot = root;
        search.TermBox.Text = "needle";
        search.FindButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        await host.SettleAsync(10);

        var row = search.Results.First(r => !r.IsFileHeader);
        Assert.Equal(helper, row.Hit!.FilePath);

        search.ActivateForTests(row);
        await host.SettleAsync(10);

        // The hit's own file, not whatever was first opened or last active
        // before the search.
        Assert.Equal(helper, vm.ActiveDocument?.FilePath);

        var editor = w.CurrentEditorForTests();
        Assert.NotNull(editor);
        Assert.Contains("needle", editor!.Document.Text);

        // The hit was selected where it was found: the old code read the
        // offset from whichever editor it grabbed, so a wrong editor here
        // would select the wrong six characters, or none at all.
        Assert.Equal("needle", editor.SelectedText);
    }

    /// <summary>
    /// The Watch panel is built alongside the other debug panels and must be
    /// dockable and reachable from the View menu the same way they are.
    /// </summary>
    [AvaloniaFact]
    public async Task WatchPanelIsDockedAndReachableFromTheViewMenu()
    {
        var root = Directory.CreateTempSubdirectory("basalt-watch-").FullName;
        var project = WriteProject(root);
        File.WriteAllText(Path.Combine(root, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var factory = Field<IdeDockFactory>(w, "_factory");
        var contents = Field<Dictionary<IDockable, Control>>(w, "_contents");

        // Built, but a dockable that was never added to a dock or to the
        // content map cannot be shown: this is what the audit found broken.
        Assert.True(contents.ContainsKey(factory.Watch));
        Assert.IsType<WatchPanel>(contents[factory.Watch]);
        Assert.NotNull(factory.Watch.Owner);

        Invoke(w, L(StringKeys.MenuView), L(StringKeys.ToolWatch));
        await host.SettleAsync(10);

        var watch = contents[factory.Watch];
        Assert.True(watch.IsEffectivelyVisible);
        Assert.True(watch.Bounds.Width > 0);
    }

    /// <summary>
    /// The Immediate window is a VB6/Visual Studio tool window and must be
    /// reachable from the View menu next to the other tool windows, the same
    /// way Watch and Output are.
    /// </summary>
    [AvaloniaFact]
    public async Task ImmediateIsReachableFromTheViewMenuNextToTheOtherToolWindows()
    {
        var root = Directory.CreateTempSubdirectory("basalt-immediate-").FullName;
        var project = WriteProject(root);
        File.WriteAllText(Path.Combine(root, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        using var host = new TestWindow(project);
        await host.SettleAsync(10);
        var w = host.Window;

        var factory = Field<IdeDockFactory>(w, "_factory");
        var contents = Field<Dictionary<IDockable, Control>>(w, "_contents");

        Assert.True(contents.ContainsKey(factory.Immediate));
        Assert.IsType<ImmediatePanel>(contents[factory.Immediate]);
        Assert.NotNull(factory.Immediate.Owner);

        Invoke(w, L(StringKeys.MenuView), L(StringKeys.ToolImmediate));
        await host.SettleAsync(10);

        var immediate = contents[factory.Immediate];
        Assert.True(immediate.IsEffectivelyVisible);
        Assert.True(immediate.Bounds.Width > 0);
    }

    /// <summary>
    /// "Run All" must run every discovered test, not just whatever happens to
    /// be selected in the tree.
    /// </summary>
    [AvaloniaFact]
    public async Task RunAllRunsEveryTestRegardlessOfSelection()
    {
        var root = Directory.CreateTempSubdirectory("basalt-runall-").FullName;
        var project = WriteProject(root);
        File.WriteAllText(Path.Combine(root, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        var testDir = Directory.CreateDirectory(Path.Combine(root, "T")).FullName;
        File.WriteAllText(Path.Combine(testDir, "VbTests.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace><IsPackable>false</IsPackable></PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
                <PackageReference Include="xunit.v3" Version="3.2.2" />
                <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(testDir, "CalculatorTests.vb"), """
            Imports Xunit
            Namespace Sample
                Public Class CalculatorTests
                    <Fact>
                    Public Sub First()
                        Assert.True(True)
                    End Sub
                    <Fact>
                    Public Sub Second()
                        Assert.True(True)
                    End Sub
                    <Fact>
                    Public Sub Third()
                        Assert.True(True)
                    End Sub
                End Class
            End Namespace
            """);

        using var host = new TestWindow(project);
        var w = host.Window;
        var contents = Field<Dictionary<IDockable, Control>>(w, "_contents");
        var tests = contents.Values.OfType<TestExplorerPanel>().First();

        for (var i = 0; i < 120 && tests.Roots.Count == 0; i++) await host.SettleAsync(5);
        Assert.NotEmpty(tests.Roots);

        // Narrow the selection to one test, the way the bug report's "Run
        // All only ran the selected test" happened.
        var leaf = tests.Roots.SelectMany(r => r.Leaves).First();
        tests.SelectForTests(leaf);
        Assert.Single(tests.SelectedTests);
        Assert.Equal(3, tests.AllTests.Count);

        Invoke(w, L(StringKeys.MenuTest), L(StringKeys.MenuTestRunAll));

        for (var i = 0; i < 400
             && tests.Roots.SelectMany(r => r.Leaves).Any(l => l.Outcome == TestOutcome.NotRun); i++)
            await host.SettleAsync(5);

        var outcomes = tests.Roots.SelectMany(r => r.Leaves).Select(l => l.Outcome).ToList();
        Assert.Equal(3, outcomes.Count);
        Assert.All(outcomes, o => Assert.NotEqual(TestOutcome.NotRun, o));
    }
}
