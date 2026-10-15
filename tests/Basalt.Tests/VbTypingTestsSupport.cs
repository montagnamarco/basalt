using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A real <see cref="MainWindow"/> with one Visual Basic file open in its
/// editor, driven by keys typed into the window the way a developer types
/// them. Shared by the <c>VbTypingTests*</c> journeys.
/// </summary>
internal sealed class VbTypingSession : IDisposable
{
    private readonly string _root;
    private readonly TestWindow _host;

    private VbTypingSession(string root, TestWindow host, string filePath)
    {
        _root = root;
        _host = host;
        FilePath = filePath;
    }

    public string FilePath { get; }

    public MainWindow Window => _host.Window;

    public TextEditor Editor => Window.CodeEditorForTests(FilePath)!
        .GetVisualDescendants().OfType<TextEditor>().Single();

    public TestWindow Host => _host;

    public static async Task<VbTypingSession> OpenAsync(string source)
    {
        var root = Directory.CreateTempSubdirectory("basalt-vbtyping-").FullName;
        var project = Path.Combine(root, "Typing.vbproj");
        File.WriteAllText(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <MyType>Empty</MyType>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        var file = Path.Combine(root, "Program.vb");
        File.WriteAllText(file, source);

        var host = new TestWindow(project);
        await host.SettleAsync(10);
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync(5);

        return new VbTypingSession(root, host, file);
    }

    /// <summary>Puts the caret at the end of the first occurrence of <paramref name="text"/> and focuses the editor.</summary>
    public void CaretAfter(string text)
    {
        var editor = Editor;
        var index = editor.Text.IndexOf(text, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"'{text}' is not in the document.");
        editor.CaretOffset = index + text.Length;
        editor.TextArea.Focus();
    }

    /// <summary>Puts the caret at a given offset and focuses the editor.</summary>
    public void CaretAt(int offset)
    {
        var editor = Editor;
        editor.CaretOffset = offset;
        editor.TextArea.Focus();
    }

    public void Press(Key key, PhysicalKey physical)
    {
        Window.KeyPress(key, RawInputModifiers.None, physical, null);
        Window.KeyRelease(key, RawInputModifiers.None, physical, null);
    }

    public void Type(string text) => Window.KeyTextInput(text);

    public async Task<bool> WaitUntilAsync(Func<bool> condition, int maxPasses = 150)
    {
        for (var i = 0; i < maxPasses; i++)
        {
            if (condition()) return true;
            await _host.SettleAsync(1);
        }
        return condition();
    }

    public async Task<bool> WaitForTextAsync(string expected) =>
        await WaitUntilAsync(() => Editor.Text == expected);

    public string LineText(int lineNumber)
    {
        var line = Editor.Document.GetLineByNumber(lineNumber);
        return Editor.Document.GetText(line.Offset, line.Length);
    }

    public void Dispose()
    {
        _host.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
