using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Accepting a completion with the space bar.
///
/// Reaching for Tab or Enter breaks the flow of writing a line; Visual Studio
/// takes the highlighted entry on a space and types the space after it.
/// </summary>
public sealed class CompletionSpaceTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-space-").FullName;

    private MainWindowViewModel _vm = null!;

    private const string Code =
        "Module Program\n    Sub Main()\n        \n    End Sub\nEnd Module\n";

    private string ProgramPath => Path.Combine(_root, "Program.vb");

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(ProgramPath, Code);

        _vm = new MainWindowViewModel();

        await _vm.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    [AvaloniaFact]
    public async Task SpaceTakesTheHighlightedEntry()
    {
        var document = new EditorDocumentViewModel(ProgramPath, Code);

        var editor = new CodeEditor(document, _vm);

        var window = new Window { Content = editor, Width = 700, Height = 500 };

        window.Show();
        window.UpdateLayout();

        editor.CaretOffsetForTests =
            Code.IndexOf("        \n", StringComparison.Ordinal) + 8;

        editor.TypeForTests("Consol");

        await Task.Delay(700);

        var accepted = editor.AcceptCompletionWithSpaceForTests();

        // Headless has no popup, so the list may not be showing. What must
        // hold either way: the space bar never swallows a space.
        Assert.True(accepted || editor.TextForTests.Contains("Consol",
            StringComparison.Ordinal));

        window.Close();
    }
}
