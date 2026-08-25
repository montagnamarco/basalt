using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Which file a context-menu command acts on.
///
/// Nineteen of the twenty-six called helpers that looked the editor up
/// through ActiveDocument, and a right click does not activate a document:
/// with two files open they read the caret from one and the path from the
/// other.
/// </summary>
public sealed class ContextCommandTargetTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-ctxtarget-").FullName;

    private MainWindowViewModel _vm = null!;

    private string FirstPath => Path.Combine(_root, "First.vb");
    private string SecondPath => Path.Combine(_root, "Second.vb");

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(FirstPath, "Public Class Alpha\nEnd Class\n");
        await File.WriteAllTextAsync(SecondPath, "Public Class Beta\nEnd Class\n");

        _vm = new MainWindowViewModel();

        await _vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task ACommandActsOnTheEditorItWasOpenedFrom()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        await vm.OpenFileAsync(FirstPath);
        await vm.OpenFileAsync(SecondPath);

        await window.SettleAsync();

        // Second.vb is active; the menu is opened on First.vb.
        Assert.Equal(SecondPath, vm.ActiveDocument?.FilePath);

        var first = window.Window.CodeEditorForTests(FirstPath);

        Assert.NotNull(first);

        var acted = window.Window.DocumentACommandWouldActOn(first);

        Assert.Equal(FirstPath, acted?.FilePath);
    }

    [AvaloniaFact]
    public async Task WithNoMenuOpenTheActiveDocumentDecides()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        await vm.OpenFileAsync(FirstPath);
        await vm.OpenFileAsync(SecondPath);

        await window.SettleAsync();

        Assert.Equal(SecondPath, window.Window.DocumentACommandWouldActOn(null)?.FilePath);
    }
}
