using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Seeing a .axaml as a drawing and as markup, in the same tab.
///
/// They used to be separate documents, chosen when the file was opened and
/// unchangeable afterwards: reading the markup of a window meant closing it
/// and opening it again from a different menu entry.
/// </summary>
public sealed class DesignerHostTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-designerhost-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private string Write(string xaml = "")
    {
        var path = Path.Combine(_root, "MainWindow.axaml");

        File.WriteAllText(path, xaml.Length > 0 ? xaml : """
            <Window xmlns="https://github.com/avaloniaui">
              <StackPanel><Button Content="Hello" /></StackPanel>
            </Window>
            """);

        return path;
    }

    private static DesignerHost HostIn(TestWindow window) =>
        window.Window.GetVisualDescendants().OfType<DesignerHost>().First();

    [AvaloniaFact]
    public async Task ADesignableFileOpensShowingTheDrawing()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write(), inDesigner: true);
        await window.SettleAsync();

        Assert.True(HostIn(window).ShowingDesigner);
    }

    [AvaloniaFact]
    public async Task TheMarkupIsOneClickAway()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write(), inDesigner: true);
        await window.SettleAsync();

        var host = HostIn(window);

        host.ShowText();

        await window.SettleAsync();

        Assert.False(host.ShowingDesigner);

        var editor = host.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();

        Assert.Contains("StackPanel", editor.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AnEditInTheMarkupIsKeptWhenGoingBack()
    {
        // The two halves are one file: what is typed in one must not be lost
        // by looking at the other.
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write(), inDesigner: true);
        await window.SettleAsync();

        var host = HostIn(window);

        host.ShowText();
        await window.SettleAsync();

        var editor = host.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();

        editor.Document.Text = editor.Document.Text.Replace("Hello", "Goodbye");

        host.ShowDesigner();
        await window.SettleAsync();

        host.ShowText();
        await window.SettleAsync();

        Assert.Contains("Goodbye", editor.Document.Text, StringComparison.Ordinal);
        Assert.Contains("Goodbye", vm.OpenDocuments[0].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task GoingBackAndForthKeepsTheSameEditor()
    {
        // Rebuilding it each time would throw away the caret and the undo
        // history.
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write(), inDesigner: true);
        await window.SettleAsync();

        var host = HostIn(window);

        host.ShowText();
        await window.SettleAsync();

        var first = host.TextView;

        host.ShowDesigner();
        host.ShowText();
        await window.SettleAsync();

        Assert.Same(first, host.TextView);
    }

    [AvaloniaFact]
    public async Task ABrokenFileStillOffersItsMarkup()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(Write("<Window><StackPanel></Window>"), inDesigner: true);
        await window.SettleAsync();

        var host = HostIn(window);

        // The designer half explains itself...
        var said = host.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text ?? "")
            .Where(t => t.Contains("designer", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(said);

        // ...and the markup is still one click away, which is where it gets
        // repaired.
        host.ShowText();
        await window.SettleAsync();

        var editor = host.GetVisualDescendants().OfType<AvaloniaEdit.TextEditor>().First();

        Assert.Contains("StackPanel", editor.Text, StringComparison.Ordinal);
    }
}
