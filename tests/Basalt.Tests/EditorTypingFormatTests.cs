using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Formatting driven from the editor control, covering the wiring between the
/// document, the formatter and the caret.
///
/// Key presses are not simulated: headless Avalonia does not route synthetic
/// input into AvaloniaEdit's text area, so such a test would silently assert
/// nothing. The editor's own formatting entry point is invoked instead, which
/// is exactly what its key handlers call.
/// </summary>
public sealed class EditorTypingFormatTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-typing", Guid.NewGuid().ToString("N"));

    public EditorTypingFormatTests() => Directory.CreateDirectory(_root);

    private async Task<(TestWindow Host, CodeEditor Code, TextEditor Editor)> OpenAsync(
        string fileName, string content)
    {
        var file = Path.Combine(_root, fileName);
        await File.WriteAllTextAsync(file, content);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(file);
        await host.SettleAsync();

        var code = host.Window.GetVisualDescendants().OfType<CodeEditor>().Single();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        return (host, code, editor);
    }

    [AvaloniaFact]
    public async Task ReindentsTheLineLeftBehindWhenPressingEnter()
    {
        var (host, code, editor) = await OpenAsync("Enter.vb", """
            Public Class A
                Public Sub M()
            Dim x = 1
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.FormatCurrentLineAsync();

        Assert.Contains("        Dim x = 1", editor.Text);
    }

    [AvaloniaFact]
    public async Task ReindentsAVisualBasicBlockKeyword()
    {
        var (host, code, editor) = await OpenAsync("Block.vb", """
            Public Class A
                Public Sub M()
                    If x > 0 Then
                        Console.WriteLine(1)
            End If
                End Sub
            End Class
            """);
        using var _ = host;

        editor.CaretOffset = editor.Text.IndexOf("End If", StringComparison.Ordinal) + 6;
        await code.FormatCurrentLineAsync();

        Assert.Contains("        End If", editor.Text);
    }

    [AvaloniaFact]
    public async Task KeepsTheCaretOnTheSameCharacterAfterReindenting()
    {
        var (host, code, editor) = await OpenAsync("Caret.vb", """
            Public Class A
                Public Sub M()
            Dim x = 1
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.FormatCurrentLineAsync();

        // The caret must still sit at the end of the line, not where the text
        // used to end before the indentation was inserted.
        Assert.Equal(editor.Document.TextLength, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task LeavesTheLineAloneWhenFormattingIsTurnedOff()
    {
        var (host, code, editor) = await OpenAsync("Off.vb", """
            Public Class A
                Public Sub M()
            Dim x = 1
            """);
        using var _ = host;

        code.AutoFormatWhileTyping = false;
        editor.CaretOffset = editor.Document.TextLength;
        await code.FormatCurrentLineAsync();

        Assert.Contains("\nDim x = 1", editor.Text);
    }

    [AvaloniaFact]
    public async Task DoesNotDisturbTheLinesAboveTheCaret()
    {
        // The two lines above are deliberately unindented and must stay that way.
        var (host, code, editor) = await OpenAsync("Above.vb", """
            Public Class A
            Public Sub M()
            Dim x = 1
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.FormatCurrentLineAsync();

        // The line above stays unindented; only the caret's line moves.
        // Compared with "\n": the raw literal carries the checkout's line
        // breaks, which are "\r\n" on Windows, and the editor keeps them.
        Assert.Contains("\nPublic Sub M()\n", editor.Text.ReplaceLineEndings("\n"));
        Assert.Contains("        Dim x = 1", editor.Text);
    }

    [AvaloniaFact]
    public async Task FixesVisualBasicKeywordCasingAfterASeparator()
    {
        // The user typed "if" and then a space: the space is already in the
        // document when the handler runs, so the word ends one character back.
        var (host, code, editor) = await OpenAsync("Casing.vb", """
            Public Class A
                Sub M()
                    if 
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.Contains("If ", editor.Text);
        Assert.DoesNotContain("        if ", editor.Text);
    }

    [AvaloniaFact]
    public async Task KeepsTheCaretPutWhenFixingCasing()
    {
        var (host, code, editor) = await OpenAsync("CasingCaret.vb", """
            Public Class A
                Sub M()
                    dim 
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        var before = editor.CaretOffset;

        await code.ApplyVisualBasicConventionsAsync();

        // Casing never changes length, so the caret must not move.
        Assert.Equal(before, editor.CaretOffset);
        Assert.Contains("Dim ", editor.Text);
    }

    [AvaloniaFact]
    public async Task LeavesCSharpCasingAlone()
    {
        // C# is case-sensitive: "if" is already correct.
        var (host, code, editor) = await OpenAsync("Casing.cs", """
            class A
            {
                void M()
                {
                    if 
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.Contains("if ", editor.Text);
        Assert.DoesNotContain("If ", editor.Text);
    }

    [AvaloniaFact]
    public async Task FixesCasingOnEnterBeforeTheNewlineIsInserted()
    {
        // On Enter the newline is not in the document yet, so the word ends at
        // the caret rather than one character before it.
        var (host, code, editor) = await OpenAsync("Newline.vb", """
            Public Class A
                Sub M()
                    dim
            """);
        using var _ = host;

        editor.CaretOffset = editor.Document.TextLength;
        await code.ApplyVisualBasicConventionsAsync();

        Assert.EndsWith("Dim", editor.Text);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
