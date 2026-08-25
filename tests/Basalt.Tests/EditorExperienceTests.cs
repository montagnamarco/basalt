using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The things a Visual Basic programmer expects from an editor.
///
/// Each of these was missing, and in two cases the language service could
/// already answer — the editor simply never asked.
/// </summary>
public sealed class EditorExperienceTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-experience-").FullName;

    private MainWindowViewModel _vm = null!;

    private string ProgramPath => Path.Combine(_root, "Program.vb");

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

        await File.WriteAllTextAsync(ProgramPath,
            "Public Class Greeter\n"
          + "    ''' <summary>Says hello to someone.</summary>\n"
          + "    Public Function Greet(name As String, times As Integer) As String\n"
          + "        Return name\n"
          + "    End Function\n"
          + "End Class\n");

        _vm = new MainWindowViewModel();

        await _vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    private CodeEditor OpenEditor(out Window window)
    {
        var document = new EditorDocumentViewModel(
            ProgramPath, File.ReadAllText(ProgramPath));

        var editor = new CodeEditor(document, _vm);

        window = new Window { Content = editor, Width = 700, Height = 500 };

        window.Show();
        window.UpdateLayout();

        return editor;
    }

    [AvaloniaFact]
    public async Task HoveringASymbolSaysWhatItIs()
    {
        var text = await File.ReadAllTextAsync(ProgramPath);

        var at = text.IndexOf("Greet(", StringComparison.Ordinal) + 2;

        var info = await _vm.GetQuickInfoAsync(ProgramPath, at);

        Assert.NotNull(info);
        Assert.Contains("Greet", info, StringComparison.Ordinal);

        // The documentation comment too, not only the signature.
        Assert.Contains("hello", info, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task TheCallBeingWrittenShowsItsParameters()
    {
        var text = await File.ReadAllTextAsync(ProgramPath);

        var caller = text
            + "\nPublic Class Other\n"
            + "    Sub M()\n"
            + "        Dim g As New Greeter()\n"
            + "        g.Greet(\n"
            + "    End Sub\n"
            + "End Class\n";

        var at = caller.LastIndexOf("g.Greet(", StringComparison.Ordinal)
               + "g.Greet(".Length;

        var help = await _vm.GetSignatureHelpAsync(ProgramPath, at, caller);

        Assert.NotNull(help);
        Assert.NotEmpty(help.Signatures);
        Assert.Contains("name", help.Signatures[0].Signature, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task TypingALetterOpensTheCompletionList()
    {
        // It used to open only on a dot, so a bare name never got a list —
        // and a bare name is most of what anyone types.
        var editor = OpenEditor(out var window);

        editor.CaretOffsetForTests = editor.TextForTests
            .IndexOf("Return name", StringComparison.Ordinal) + "Return ".Length;

        editor.TypeForTests("na");

        await Task.Delay(600);

        // That completion was asked for, not that a popup appeared: a popup
        // is a window, and a headless run has no screen to put one on.
        Assert.True(editor.CompletionRequestsForTests > 0,
            "typing a letter did not ask for completion");

        window.Close();
    }

    [AvaloniaFact]
    public async Task ADotStillOpensIt()
    {
        var editor = OpenEditor(out var window);

        editor.CaretOffsetForTests = editor.TextForTests
            .IndexOf("Return name", StringComparison.Ordinal) + "Return name".Length;

        editor.TypeForTests(".");

        await Task.Delay(600);

        Assert.True(editor.CompletionRequestsForTests > 0);

        window.Close();
    }
}
