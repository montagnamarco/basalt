using Avalonia.Headless.XUnit;
using Basalt.Core.Model;
using Basalt.Designer;
using Basalt.Designer.Model;
using Basalt.Designer.Toolbox;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Drawing a window and then running it.
///
/// Each of these stands for a way the designer looked broken while every
/// part of it worked on its own: the control was drawn, the preview showed
/// it, and what came up when the program ran was an empty window.
/// </summary>
public sealed class DesignerWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-designer-flow", Guid.NewGuid().ToString("N"));

    public DesignerWorkflowTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private const string EmptyWindow = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                x:Class="App.MainWindow">
          <StackPanel />
        </Window>
        """;

    private static readonly ToolboxItem Button =
        new("Pulsante", "Button", "Comuni", """<Button Content="Pulsante" />""");

    [AvaloniaFact]
    public async Task WritesWhatWasDrawnToDiskBeforeItIsBuilt()
    {
        // The compiler reads the disk, not the editors. A control drawn and
        // never saved was simply not in the build, and the window came up
        // empty with nothing saying why.
        var path = Path.Combine(_root, "MainWindow.axaml");
        await File.WriteAllTextAsync(path, EmptyWindow);

        var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();

        var session = vm.DesignerSessionFor(document);
        Assert.NotNull(session);

        var stack = XamlDocument.ControlChildren(session!.Document.Root).First();
        session.InsertFromToolbox(Button, stack);

        // Drawn but not saved: the file on disk still knows nothing about it.
        Assert.DoesNotContain("Button", await File.ReadAllTextAsync(path));

        await vm.SaveAllAsync();

        Assert.Contains("Button", await File.ReadAllTextAsync(path));
    }

    [AvaloniaFact]
    public async Task TakesUpMarkupThatWasEditedByHand()
    {
        // The session used to be built once and kept. Typing in the XAML half
        // of the tab left the drawing showing the old tree, and the next
        // thing drawn wrote that stale tree back over what had been typed.
        var path = Path.Combine(_root, "Typed.axaml");
        await File.WriteAllTextAsync(path, EmptyWindow);

        var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(path, inDesigner: true);

        var document = vm.OpenDocuments.Single();

        Assert.NotNull(vm.DesignerSessionFor(document));

        // As if typed in the text half of the tab.
        document.Text = EmptyWindow.Replace(
            "<StackPanel />", """<StackPanel><TextBox /></StackPanel>""");

        var session = vm.DesignerSessionFor(document);

        Assert.NotNull(session);

        var stack = XamlDocument.ControlChildren(session!.Document.Root).First();

        Assert.Single(XamlDocument.ControlChildren(stack));
        Assert.Equal("TextBox", XamlDocument.ControlChildren(stack).First().Name.LocalName);
    }

    [AvaloniaFact]
    public void PutsASecondControlBesideTheFirstRatherThanNowhere()
    {
        // Adding from the toolbox aimed at wherever the pointer last crossed
        // the surface, which after reaching the toolbox is stale. Naming the
        // selection instead is what makes a second button land in the panel:
        // the insert walks up from it until something can hold a child.
        var session = new DesignerSession(
            XamlDocument.Parse(EmptyWindow), SourceLanguage.VisualBasic);

        var stack = XamlDocument.ControlChildren(session.Document.Root).First();

        var first = session.InsertFromToolbox(Button, stack);

        // The first one is now selected, and it cannot hold a child itself.
        session.Select(first);

        session.InsertFromToolbox(Button, session.Selection);

        Assert.Equal(2, XamlDocument.ControlChildren(stack).Count());
    }
}
