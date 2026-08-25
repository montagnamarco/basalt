using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Verifies the full path of the "New solution" command: generation, opening
/// in the IDE and availability of the window for the designer.
/// </summary>
public sealed class NuovaSoluzioneFlussoTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-nuova", Guid.NewGuid().ToString("N"));

    public NuovaSoluzioneFlussoTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public async Task LaSoluzioneVbCreataSiApreNellIdeConLaFinestraPronta()
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, "AppVb", ProjectTemplate.AvaloniaApp);

        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(created.SolutionPath);

        Assert.Equal(created.SolutionPath, vm.SolutionPath);
        Assert.NotNull(vm.StartupProject);
        Assert.EndsWith(".vbproj", vm.StartupProject);

        // The generated window opens in the designer, with VB code-behind.
        await vm.OpenFileAsync(created.MainWindowXamlPath!, inDesigner: true);

        Assert.NotNull(vm.ActiveDesigner);
        Assert.Equal(SourceLanguage.VisualBasic, vm.ActiveDesigner!.Language);

        var preview = vm.ActiveDesigner.Render();
        Assert.True(preview.Succeeded, preview.Error);
    }

    [Fact]
    public async Task LAlberoDellaSoluzioneMostraIFileGenerati()
    {
        var created = await SolutionTemplates.CreateAsync(
            _root, "AppAlbero", ProjectTemplate.AvaloniaApp);

        using var vm = new MainWindowViewModel();
        await vm.OpenSolutionAsync(created.SolutionPath);

        var nomi = Raccogli(vm.Explorer.Roots).ToList();

        Assert.Contains("MainWindow.axaml", nomi);
        Assert.Contains("App.axaml", nomi);
        Assert.Contains("Program.vb", nomi);
    }

    private static IEnumerable<string> Raccogli(IEnumerable<SolutionTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node.Name;
            foreach (var child in Raccogli(node.Children)) yield return child;
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
