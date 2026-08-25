using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// The Visual Basic editing experience end to end, as typed in the editor:
/// casing, spacing, indentation, and automatic block closing.
/// </summary>
public sealed class VisualBasicEditingExperienceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-vbexp", Guid.NewGuid().ToString("N"));

    public VisualBasicEditingExperienceTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, CodeEditor Code, TextEditor Editor)> OpenAsync(string content)
    {
        var file = Path.Combine(_root, "Typing.vb");
        await File.WriteAllTextAsync(file, content);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        return (host, code, editor);
    }

    /// <summary>Simulates pressing Enter through the editor's own handler.</summary>
    private static async Task PressEnterAsync(CodeEditor code, TextEditor editor)
    {
        await code.HandleEnterForTestsAsync();
        await Task.Delay(50);
    }

    [AvaloniaFact]
    public async Task SpacesTheAssignmentInADeclaration()
    {
        // Reported: Dim hello As String="" kept no spaces around "=".
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    Dim hello As String=""
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.Contains("Dim hello As String = \"\"", editor.Text);
    }

    [AvaloniaFact]
    public async Task CorrectsLowercaseThenAndSpacesTheComparison()
    {
        // Reported: If hello="prova" then kept "then" lowercase and no spaces.
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    If hello="prova" then
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.Contains("If hello = \"prova\" Then", editor.Text);
    }

    [AvaloniaFact]
    public async Task CorrectsLowercaseIfInEndIf()
    {
        // Reported: "End if" kept the "if" lowercase.
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    If x > 0 Then

                    End if
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.Contains("End If", editor.Text);
        Assert.DoesNotContain("End if", editor.Text);
    }

    [AvaloniaFact]
    public async Task WritesEndIfWhenEnterFollowsThen()
    {
        // Reported: pressing Enter after Then should produce End If below.
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    If x > 0 Then
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await PressEnterAsync(code, editor);

        Assert.Contains("End If", editor.Text);
    }

    [AvaloniaFact]
    public async Task WritesEndWhileWhenEnterFollowsWhile()
    {
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    While x < 10
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await PressEnterAsync(code, editor);

        Assert.Contains("End While", editor.Text);
    }

    [AvaloniaFact]
    public async Task WritesNextWhenEnterFollowsForEach()
    {
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    For Each item In items
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await PressEnterAsync(code, editor);

        Assert.Contains("Next", editor.Text);
    }

    [AvaloniaFact]
    public async Task PlacesTheCaretInsideTheBlockItJustClosed()
    {
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    If x > 0 Then
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await PressEnterAsync(code, editor);

        // The caret belongs on the empty body line, indented one level in.
        var caretLine = editor.Document.GetLineByOffset(editor.CaretOffset);
        var caretText = editor.Document.GetText(caretLine.Offset, caretLine.Length);

        Assert.True(string.IsNullOrWhiteSpace(caretText),
            $"caret should sit on an empty line, found '{caretText}'");
        Assert.Equal(caretLine.Offset + 12, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task DoesNotCloseABlockThatIsAlreadyClosed()
    {
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    If x > 0 Then
                    End If
            """);
        using var _ = host;

        // Caret at the end of the If line, which already has its End If.
        editor.CaretOffset = editor.Text.IndexOf("Then", StringComparison.Ordinal) + 4;
        await PressEnterAsync(code, editor);

        var occurrences = editor.Text.Split("End If").Length - 1;
        Assert.Equal(1, occurrences);
    }

    [AvaloniaFact]
    public async Task IndentsTheNewLineWithoutClosingAnythingAfterAPlainStatement()
    {
        var (host, code, editor) = await OpenAsync("""
            Public Class A
                Sub M()
                    Dim x = 1
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await PressEnterAsync(code, editor);

        Assert.DoesNotContain("End Sub", editor.Text);

        var caretLine = editor.Document.GetLineByOffset(editor.CaretOffset);
        Assert.Equal(caretLine.Offset + 8, editor.CaretOffset);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
