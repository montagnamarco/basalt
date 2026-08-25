using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Opening documents in the dockable central area.
///
/// Covers the defect where the tab appeared but the editor stayed invisible:
/// AvaloniaEdit's theme was missing, so the TextEditor existed without a
/// template. This checks rendering, not just presence.
/// </summary>
public sealed class EditorNelDockTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-editdock", Guid.NewGuid().ToString("N"));

    public EditorNelDockTests() => Directory.CreateDirectory(_root);

    private static async Task StabilizzaAsync(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            window.UpdateLayout();
            await Task.Delay(60);
        }
        window.UpdateLayout();
    }

    // Only Visual Basic now: a .cs file is coloured by TextMate rather than
    // by AvaloniaEdit's own definition, since C# is no longer a language
    // Basalt supports.
    [AvaloniaTheory]
    [InlineData("Prova.vb", "Public Class Prova\nEnd Class", "VB")]
    public async Task ApreLEditorConLaSintassiDelLinguaggio(
        string nomeFile, string contenuto, string sintassiAttesa)
    {
        var file = Path.Combine(_root, nomeFile);
        await File.WriteAllTextAsync(file, contenuto);

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        await vm.OpenFileAsync(file);
        await StabilizzaAsync(window);

        var editor = window.GetVisualDescendants().OfType<TextEditor>().FirstOrDefault();
        Assert.NotNull(editor);
        Assert.Equal(contenuto, editor!.Text);
        Assert.Equal(sintassiAttesa, editor.SyntaxHighlighting?.Name);

        // Without a template the control draws nothing.
        var area = editor.GetVisualDescendants().OfType<AvaloniaEdit.Editing.TextArea>().FirstOrDefault();
        Assert.NotNull(area);
    }

    [AvaloniaFact]
    public async Task ApreIlDesignerPerIFileXaml()
    {
        var file = Path.Combine(_root, "Finestra.axaml");
        await File.WriteAllTextAsync(file, """
            <Window xmlns="https://github.com/avaloniaui"><TextBlock Text="ciao" /></Window>
            """);

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        await vm.OpenFileAsync(file, inDesigner: true);
        await StabilizzaAsync(window);

        Assert.NotNull(window.GetVisualDescendants().OfType<DesignSurface>().FirstOrDefault());
    }

    [AvaloniaFact]
    public async Task LaSchermataInizialeScompareQuandoSiApreUnDocumento()
    {
        var file = Path.Combine(_root, "Codice.cs");
        await File.WriteAllTextAsync(file, "class C { }");

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        var benvenuto = window.FindControl<Border>("WelcomePane")!;
        Assert.True(benvenuto.IsVisible);

        await vm.OpenFileAsync(file);
        await StabilizzaAsync(window);

        // If it stayed visible it would cover the whole panel area.
        Assert.False(benvenuto.IsVisible);
    }

    [AvaloniaFact]
    public async Task RiusaLoStessoEditorTornandoSuUnDocumentoGiaAperto()
    {
        // Recreating it would lose the caret position and the undo history.
        var uno = Path.Combine(_root, "A.cs");
        var due = Path.Combine(_root, "B.cs");
        await File.WriteAllTextAsync(uno, "class A { }");
        await File.WriteAllTextAsync(due, "class B { }");

        using var host = new TestWindow();
        var window = host.Window;
        var vm = (MainWindowViewModel)window.DataContext!;

        await vm.OpenFileAsync(uno);
        await StabilizzaAsync(window);
        var primo = window.GetVisualDescendants().OfType<TextEditor>().First();

        await vm.OpenFileAsync(due);
        await StabilizzaAsync(window);
        await vm.OpenFileAsync(uno);
        await StabilizzaAsync(window);

        var ritrovato = window.GetVisualDescendants().OfType<TextEditor>()
            .FirstOrDefault(e => e.Text == "class A { }");
        Assert.Same(primo, ritrovato);
    }

    [AvaloniaTheory]
    [InlineData("View.cshtml")]
    [InlineData("View.vbhtml")]
    [InlineData("appsettings.json")]
    [InlineData("site.css")]
    [InlineData("Project.csproj")]
    [InlineData("Program.vb")]
    public async Task HighlightsFilesByExtensionNotJustByLanguage(string fileName)
    {
        // A project holds views, stylesheets and configuration besides source
        // files; those would otherwise open as plain text.
        var file = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(file, "content");

        using var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var editor = host.Window.GetVisualDescendants().OfType<TextEditor>().Single();

        // Colouring arrives one of two ways: AvaloniaEdit's own definitions
        // set SyntaxHighlighting, while the TextMate grammars install a line
        // transformer and leave it null. Either counts; what must not happen
        // is neither.
        var coloured = editor.SyntaxHighlighting is not null
                    || editor.TextArea.TextView.LineTransformers.Count > 0;

        Assert.True(coloured, $"{fileName} opened without any highlighting.");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
