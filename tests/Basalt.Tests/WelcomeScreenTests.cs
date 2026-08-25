using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The welcome screen sits on top of the docking area, so whenever it stays
/// visible the whole IDE looks empty even though the panels loaded correctly.
/// </summary>
public sealed class WelcomeScreenTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-welcome", Guid.NewGuid().ToString("N"));

    public WelcomeScreenTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void IsVisibleOnlyBeforeAnythingIsLoaded()
    {
        using var vm = new MainWindowViewModel();

        Assert.True(vm.IsWelcomeVisible);
    }

    [Fact]
    public async Task HidesAsSoonAsASolutionIsOpenedEvenWithNoDocuments()
    {
        // The regression: opening a solution populates the explorer but opens
        // no document, and the welcome screen used to keep covering the panels.
        var created = await SolutionTemplates.CreateAsync(
            _root, "OpenedSolution", ProjectTemplate.ClassLibrary);

        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(created.SolutionPath);

        Assert.Empty(vm.OpenDocuments);
        Assert.NotEmpty(vm.Explorer.Roots);
        Assert.False(vm.IsWelcomeVisible);
    }

    [Fact]
    public async Task HidesWhenADocumentIsOpenedWithoutASolution()
    {
        var file = Path.Combine(_root, "Loose.cs");
        await File.WriteAllTextAsync(file, "class Loose { }");

        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(file);

        Assert.Null(vm.SolutionPath);
        Assert.False(vm.IsWelcomeVisible);
    }

    [AvaloniaFact]
    public async Task TheWelcomeBorderStopsCoveringTheDockAfterOpeningASolution()
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, "RenderedSolution", ProjectTemplate.ClassLibrary);

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        var welcome = window.FindControl<Border>("WelcomePane")!;
        Assert.True(welcome.IsVisible);

        await vm.OpenSolutionAsync(created.SolutionPath);
        for (var i = 0; i < 5; i++) { window.UpdateLayout(); await Task.Delay(60); }

        Assert.False(welcome.IsVisible);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
