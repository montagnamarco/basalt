using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Opening a .axaml the designer cannot read.
///
/// XamlDocument.Parse goes through XDocument.Parse, which throws on malformed
/// XML, and nobody caught it: the tab opened blank and said nothing. A broken
/// XAML is repaired by editing it, and to edit it you have to see it.
/// </summary>
public sealed class BrokenXamlTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-brokenxaml-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Write(string name, string xaml)
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, xaml);

        return path;
    }

    [AvaloniaFact]
    public async Task AMalformedFileOpensWithoutThrowing()
    {
        var path = Write("Broken.axaml", "<Window><StackPanel></Window>");

        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        // This used to throw out of the view's construction.
        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        Assert.NotNull(vm.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task ItSaysWhatIsWrongAndWhere()
    {
        var path = Write("Broken.axaml", "<Window>\n  <StackPanel>\n</Window>");

        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        // The banner sits in the document's own view, so it is read from
        // there rather than from the whole window.
        var banners = window.Window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .Where(t => t.Contains("designer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var shown = string.Join(" ", banners);

        Assert.NotEmpty(banners);

        // Not a blank surface: the reason, and the line it is on.
        Assert.Contains("line", shown, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task TheTextIsStillReachable()
    {
        // The only way to repair it is to edit it.
        var path = Write("Broken.axaml", "<Window><StackPanel></Window>");

        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        var editors = window.Window.GetVisualDescendants()
            .OfType<AvaloniaEdit.TextEditor>()
            .ToList();

        Assert.NotEmpty(editors);
        Assert.Contains("StackPanel", editors[0].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnEmptyFileDoesNotThrowEither()
    {
        var path = Write("Empty.axaml", "");

        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        Assert.NotNull(vm.ActiveDocument);
    }

    [AvaloniaFact]
    public async Task AWellFormedFileStillOpensInTheDesigner()
    {
        var path = Write("Good.axaml", """
            <Window xmlns="https://github.com/avaloniaui">
              <StackPanel><Button Content="Hello" /></StackPanel>
            </Window>
            """);

        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(path, inDesigner: true);
        await window.SettleAsync();

        var surfaces = window.Window.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.GetType().Name == "DesignSurface")
            .ToList();

        Assert.NotEmpty(surfaces);
    }
}
