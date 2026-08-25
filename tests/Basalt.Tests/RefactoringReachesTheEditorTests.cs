using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// A refactoring reaching the screen, not only the disk.
///
/// The tests for each refactoring proved the file was rewritten correctly, and
/// every one of them passed while the editor went on showing the old text: the
/// editor copied the document once when it was built and the flow only ran the
/// other way. The change was on disk, invisible until the file was reopened.
/// </summary>
public sealed class RefactoringReachesTheEditorTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-refactorloop-").FullName;

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

        await File.WriteAllTextAsync(ProgramPath, """
            Public Class Greeter
                Public Property Name As String
            End Class
            """);
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    private static AvaloniaEdit.TextEditor? EditorIn(TestWindow window) =>
        window.Window.CurrentEditorForTests();

    [AvaloniaFact]
    public async Task TheEditorShowsWhatARefactoringWroteToTheFile()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await window.SettleAsync();

        var editor = EditorIn(window);

        Assert.NotNull(editor);
        Assert.Contains("Name", editor.Document.Text, StringComparison.Ordinal);

        // What a refactoring does: rewrite the file, then tell the document.
        await File.WriteAllTextAsync(ProgramPath, """
            Public Class Greeter
                Public Property FullName As String
            End Class
            """);

        vm.OpenDocuments[0].Text = await File.ReadAllTextAsync(ProgramPath);

        await window.SettleAsync();

        // The editor has to follow. It used to go on showing "Name".
        Assert.Contains("FullName", editor.Document.Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ReloadingKeepsTheCaretWhereItWas()
    {
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await window.SettleAsync();

        var editor = EditorIn(window)!;

        editor.CaretOffset = editor.Document.Text.IndexOf("Property", StringComparison.Ordinal);

        var before = editor.CaretOffset;

        vm.OpenDocuments[0].Text = editor.Document.Text.Replace("Greeter", "Greeter2");

        await window.SettleAsync();

        // Jumping to the top on every refactoring loses the reader's place.
        Assert.Equal(before, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task TypingIsStillSentBackToTheDocument()
    {
        // The direction that already worked must keep working: following the
        // document must not turn the binding into a loop.
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await window.SettleAsync();

        var editor = EditorIn(window)!;

        editor.Document.Text = "Public Class Typed\nEnd Class\n";

        await window.SettleAsync();

        Assert.Contains("Typed", vm.OpenDocuments[0].Text, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task SettingTheSameTextChangesNothing()
    {
        // A refactoring that decides to write the file unchanged must not
        // disturb the editor at all.
        using var window = new TestWindow();

        var vm = (MainWindowViewModel)window.Window.DataContext!;

        await vm.OpenFileAsync(ProgramPath);
        await window.SettleAsync();

        var editor = EditorIn(window)!;

        editor.CaretOffset = 12;

        vm.OpenDocuments[0].Text = editor.Document.Text;

        await window.SettleAsync();

        Assert.Equal(12, editor.CaretOffset);
    }
}
