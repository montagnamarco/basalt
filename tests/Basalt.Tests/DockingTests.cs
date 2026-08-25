using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Dock.Model.Controls;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Verifies the dockable layout.
///
/// The controls must not only be present in the model but also rendered:
/// without Dock's theme the panels exist and stay invisible, exactly as
/// happened to the editor without AvaloniaEdit's theme.
/// </summary>
public sealed class DockingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-dock", Guid.NewGuid().ToString("N"));

    public DockingTests() => Directory.CreateDirectory(_root);

    [AvaloniaFact]
    public void IlLayoutInizialeContieneTuttiIPannelli()
    {
        var factory = new IdeDockFactory { TerminalWorkingDirectory = _root };
        var layout = factory.CreateLayout();

        Assert.NotNull(layout);
        Assert.NotNull(factory.DocumentArea);
        Assert.NotNull(factory.BottomArea);

        // Problems, output and a terminal are created in the bottom pane.
        var inferiore = factory.BottomArea!.VisibleDockables!;
        Assert.Contains(factory.Problems, inferiore);
        Assert.Contains(factory.Output, inferiore);
        Assert.Contains(inferiore, d => d is TerminalTool);
    }

    /// <summary>
    /// Dock builds its controls over several layout passes: a single
    /// UpdateLayout is not enough for the panels to appear in the visual tree.
    /// </summary>
    private static async Task StabilizzaLayoutAsync(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            window.UpdateLayout();
            await Task.Delay(60);
        }
        window.UpdateLayout();
    }

    [AvaloniaFact]
    public async Task IPannelliSonoRenderizzatiNellaFinestra()
    {
        using var host = new TestWindow();
        var window = host.Window;
        await StabilizzaLayoutAsync(window);

        var tipi = window.GetVisualDescendants().OfType<Control>()
            .Select(c => c.GetType()).Distinct().ToList();

        Assert.Contains(typeof(SolutionExplorerPanel), tipi);
        Assert.Contains(typeof(ToolboxPanel), tipi);
    }

    [AvaloniaFact]
    public async Task LEditorSiApreNellAreaDocumentiEdEVisibile()
    {
        var file = Path.Combine(_root, "Prova.cs");
        await File.WriteAllTextAsync(file, "class Prova { }");

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        await vm.OpenFileAsync(file);
        await StabilizzaLayoutAsync(window);

        var editor = window.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();
        Assert.NotNull(editor);
        Assert.Equal("class Prova { }", editor!.Text);

        // The template must be applied, otherwise the editor is invisible.
        var area = editor.GetVisualDescendants().OfType<AvaloniaEdit.Editing.TextArea>().FirstOrDefault();
        Assert.NotNull(area);
    }

    [AvaloniaFact]
    public void ApreTerminaliAggiuntiviNelRiquadroInferiore()
    {
        var factory = new IdeDockFactory { TerminalWorkingDirectory = _root };
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        var primi = factory.BottomArea!.VisibleDockables!.OfType<TerminalTool>().Count();

        factory.OpenNewTerminal();
        factory.OpenNewTerminal();

        var dopo = factory.BottomArea!.VisibleDockables!.OfType<TerminalTool>().ToList();
        Assert.Equal(primi + 2, dopo.Count);

        // The titles tell the sessions apart.
        Assert.Equal(dopo.Count, dopo.Select(t => t.Title).Distinct().Count());
    }

    [AvaloniaFact]
    public void RiapreUnPannelloChiusoDallUtente()
    {
        var factory = new IdeDockFactory { TerminalWorkingDirectory = _root };
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);

        factory.RemoveDockable(factory.Output, collapse: false);
        Assert.DoesNotContain(factory.Output, factory.BottomArea!.VisibleDockables!);

        factory.ShowTool(factory.Output, factory.BottomArea!);
        Assert.Contains(factory.Output, factory.BottomArea!.VisibleDockables!);
    }

    [AvaloniaFact]
    public async Task PiuDesignerRestanoIndipendentiQuandoSonoApertiInsieme()
    {
        // With panels side by side two windows can be visible at the same
        // time: each one must have its own session.
        var uno = Path.Combine(_root, "Uno.axaml");
        var due = Path.Combine(_root, "Due.axaml");
        await File.WriteAllTextAsync(uno, """
            <Window xmlns="https://github.com/avaloniaui"><TextBlock Text="uno" /></Window>
            """);
        await File.WriteAllTextAsync(due, """
            <Window xmlns="https://github.com/avaloniaui"><TextBlock Text="due" /></Window>
            """);

        using var vm = new MainWindowViewModel();
        await vm.OpenFileAsync(uno, inDesigner: true);
        await vm.OpenFileAsync(due, inDesigner: true);

        var sessioneUno = vm.DesignerSessionFor(vm.OpenDocuments[0])!;
        var sessioneDue = vm.DesignerSessionFor(vm.OpenDocuments[1])!;

        Assert.NotSame(sessioneUno, sessioneDue);
        Assert.Contains("uno", sessioneUno.Document.ToXaml());
        Assert.Contains("due", sessioneDue.Document.ToXaml());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
