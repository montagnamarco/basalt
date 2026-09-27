using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The list a space opens in Visual Basic, typed through the window.
/// </summary>
/// <remarks>
/// As in Visual Studio: "As " offers the types without committing any of them
/// on the next keystroke, and "= New " preselects the declared type, so Tab
/// or Enter writes it. The editor opened the list only on a letter or a dot.
/// </remarks>
public sealed class CompletionOnSpaceTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-completion-space-").FullName;
    private MainWindowViewModel _shell = null!;

    private static string Source(string statement) =>
        "Module Program\n    Sub Main()\n        " + statement + "\n    End Sub\nEnd Module\n";

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(_root, "Typing.vbproj");
        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(Path.Combine(_root, "Typing.vb"), Source(""));
        _shell = new MainWindowViewModel();
        await _shell.OpenSolutionAsync(project);
    }

    private (Window Window, CodeEditor Code, TextEditor Editor) Open(string statement)
    {
        var text = Source(statement);
        var code = new CodeEditor(new EditorDocumentViewModel(Path.Combine(_root, "Typing.vb"), text), _shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        window.Show();
        window.UpdateLayout();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;
        editor.TextArea.Focus();
        return (window, code, editor);
    }

    private static async Task WaitForListAsync(CodeEditor code)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);

        while (!code.CompletionOpenForTests && DateTime.UtcNow < deadline) await Task.Delay(20);

        Assert.True(code.CompletionOpenForTests, "the list did not open");
    }

    [AvaloniaFact]
    public async Task NewPreselectsTheDeclaredTypeForTabToWrite()
    {
        var (window, code, editor) = Open("Dim dice As Random = New");
        try
        {
            window.KeyTextInput(" ");
            await WaitForListAsync(code);

            Assert.Equal("Random", code.SelectedCompletionForTests);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await Task.Delay(100);

            Assert.Contains("Dim dice As Random = New Random", editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task AsOffersTypesWithoutCommittingOneOnTheNextKey()
    {
        // Opened by a space with nothing preselected, the list is a
        // suggestion: the "(" typed next must not write whatever sorted first.
        var (window, code, editor) = Open("Dim count As");
        try
        {
            window.KeyTextInput(" ");
            await WaitForListAsync(code);

            Assert.Null(code.SelectedCompletionForTests);
            Assert.True(code.LastCompletionCountForTests > 0);

            window.KeyTextInput("(");
            await Task.Delay(100);

            Assert.Contains("Dim count As (", editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    public ValueTask DisposeAsync()
    {
        _shell.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        return ValueTask.CompletedTask;
    }
}
