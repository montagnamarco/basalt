using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>The toolbar, and what it can hold.</summary>
public class ToolbarTests
{
    [AvaloniaFact]
    public void ShowsAButtonForEachAction()
    {
        var toolbar = new IdeToolbar();

        toolbar.Show([
            new ToolbarAction(IconKind.Save, "Save", () => { }),
            new ToolbarAction(IconKind.Build, "Build", () => { })
        ]);

        Assert.Equal(["Save", "Build"], toolbar.Tooltips);
    }

    [AvaloniaFact]
    public void GreysOutAButtonThatCannotBePressed()
    {
        // Stop should look unavailable while nothing is running, rather than
        // doing nothing when pressed.
        var running = false;

        var toolbar = new IdeToolbar();

        toolbar.Show([
            new ToolbarAction(IconKind.Stop, "Stop", () => { })
                { IsAvailable = () => running }
        ]);

        Assert.False(toolbar.IsButtonEnabled("Stop"));

        running = true;
        toolbar.RefreshAvailability();

        Assert.True(toolbar.IsButtonEnabled("Stop"));
    }

    [AvaloniaFact]
    public void HoldsAChooserAsWellAsButtons()
    {
        // The build configuration belongs on the toolbar but is picked from a
        // list, not pressed.
        var toolbar = new IdeToolbar();

        toolbar.Show([new ToolbarAction(IconKind.Build, "Build", () => { })]);

        toolbar.Add(new ToolbarChooser(
            "Configuration", ["Debug", "Release"], "Debug", _ => { }));

        Assert.Equal(1, toolbar.ChooserCount);
        Assert.Equal(1, toolbar.ButtonCount);
    }

    [AvaloniaFact]
    public void ReportsWhatWasChosen()
    {
        var toolbar = new IdeToolbar();

        string? chosen = null;

        toolbar.Add(new ToolbarChooser(
            "Configuration", ["Debug", "Release"], "Debug", picked => chosen = picked));

        // Shown in a window first: a control outside the tree has no realised
        // children to find.
        var window = new Window { Content = toolbar, Width = 400, Height = 60 };
        window.Show();

        var combo = toolbar.GetVisualDescendants().OfType<ComboBox>().Single();

        combo.SelectedIndex = 1;

        window.Close();

        Assert.Equal("Release", chosen);
    }
}

/// <summary>The status bar's fields.</summary>
public sealed class StatusBarFieldTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-statusbar", Guid.NewGuid().ToString("N"));

    public StatusBarFieldTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task ShowsWhereTheCaretIsAndHowTheFileIsStored()
    {
        var file = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(file, "Module A\n    Sub M()\n    End Sub\nEnd Module");

        using var host = new TestWindow();
        var vm = (Basalt.Shell.ViewModels.MainWindowViewModel)host.Window.DataContext!;

        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        // Named by first match: control templates reuse names such as
        // PART_Title, so a dictionary of every named block collides.
        string Label(string name) => host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .First(t => t.Name == name)
            .Text ?? "";

        var labels = new Dictionary<string, string>
        {
            ["CaretPositionLabel"] = Label("CaretPositionLabel"),
            ["EncodingLabel"] = Label("EncodingLabel"),
            ["LineEndingLabel"] = Label("LineEndingLabel"),
            ["LanguageLabel"] = Label("LanguageLabel"),
            ["IndentationLabel"] = Label("IndentationLabel")
        };

        Assert.Contains("Ln", labels["CaretPositionLabel"]);
        Assert.Equal("UTF-8", labels["EncodingLabel"]);
        Assert.Equal("LF", labels["LineEndingLabel"]);
        Assert.Equal("Visual Basic", labels["LanguageLabel"]);
        Assert.Contains("Spaces", labels["IndentationLabel"]);
    }

    [AvaloniaFact]
    public async Task TheClickableFieldsAreButtons()
    {
        // A status bar that only reports is half a status bar.
        using var host = new TestWindow();
        await host.SettleAsync();

        var buttons = host.Window.GetVisualDescendants()
            .OfType<Button>()
            .Select(b => b.Name)
            .Where(n => n is not null)
            .ToList();

        Assert.Contains("CaretPositionButton", buttons);
        Assert.Contains("IndentationButton", buttons);
        Assert.Contains("LineEndingButton", buttons);

        // Encoding and language read like the others, so they act like them
        // too: a value shown but not changeable reads as a gap.
        Assert.Contains("EncodingButton", buttons);
        Assert.Contains("LanguageButton", buttons);
    }

    [AvaloniaFact]
    public async Task ShowsWhatAFileIsActuallyEncodedAs()
    {
        // The label used to say UTF-8 whatever was on disk.
        var root = Path.Combine(Path.GetTempPath(), $"basalt-enc-{Guid.NewGuid():N}");

        Directory.CreateDirectory(root);

        try
        {
            var path = Path.Combine(root, "Latin.vb");

            await File.WriteAllTextAsync(path, "' città\nModule A\nEnd Module",
                System.Text.Encoding.Latin1);

            using var host = new TestWindow();
            var vm = (MainWindowViewModel)host.Window.DataContext!;

            await vm.OpenFileAsync(path);
            await host.SettleAsync();

            var label = host.Window.GetVisualDescendants()
                .OfType<TextBlock>()
                .First(t => t.Name == "EncodingLabel");

            Assert.Equal("Windows-1252", label.Text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task SaysNothingAboutTheDebuggerWhileItIsIdle()
    {
        using var host = new TestWindow();
        await host.SettleAsync();

        var label = host.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(t => t.Name == "DebugStateLabel");

        Assert.Equal("", label.Text ?? "");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Choosing which project runs.</summary>
public sealed class StartupProjectTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-startup", Guid.NewGuid().ToString("N"));

    public StartupProjectTests() => Directory.CreateDirectory(_root);

    private async Task WriteProjectAsync(string name, string outputType)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);

        var element = outputType.Length == 0 ? "" : $"\n    <OutputType>{outputType}</OutputType>";

        await File.WriteAllTextAsync(Path.Combine(directory, $"{name}.vbproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>{element}
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
    }

    [AvaloniaFact]
    public async Task FindsEveryProjectThatCanBeRun()
    {
        await WriteProjectAsync("Tool", "Exe");
        await WriteProjectAsync("App", "WinExe");
        await WriteProjectAsync("Library", "");

        var solution = Path.Combine(_root, "Solution.slnx");
        await File.WriteAllTextAsync(solution, "<Solution />");

        var vm = new Basalt.Shell.ViewModels.MainWindowViewModel();

        await vm.OpenSolutionAsync(solution);

        // A library is not runnable, whatever it is called.
        Assert.Equal(2, vm.RunnableProjects.Count);
        Assert.DoesNotContain(vm.RunnableProjects, p => p.Contains("Library"));
    }

    [AvaloniaFact]
    public async Task ChoosesWhichProjectRuns()
    {
        await WriteProjectAsync("First", "Exe");
        await WriteProjectAsync("Second", "Exe");

        var solution = Path.Combine(_root, "Solution.slnx");
        await File.WriteAllTextAsync(solution, "<Solution />");

        var vm = new Basalt.Shell.ViewModels.MainWindowViewModel();
        await vm.OpenSolutionAsync(solution);

        var second = vm.RunnableProjects.Single(p => p.Contains("Second"));

        vm.SetStartupProject(second);

        Assert.Equal(second, vm.StartupProject);
    }

    [AvaloniaFact]
    public async Task IgnoresAProjectThatIsNotInThisSolution()
    {
        // A setting left over from another solution should not leave the IDE
        // unable to run anything.
        await WriteProjectAsync("Only", "Exe");

        var solution = Path.Combine(_root, "Solution.slnx");
        await File.WriteAllTextAsync(solution, "<Solution />");

        var vm = new Basalt.Shell.ViewModels.MainWindowViewModel();
        await vm.OpenSolutionAsync(solution);

        var before = vm.StartupProject;

        vm.SetStartupProject("/somewhere/else/Other.vbproj");

        Assert.Equal(before, vm.StartupProject);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
