using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Formatting a line the caret has left by other means than Enter: arrow keys,
/// a click elsewhere, or focus moving out of the editor.
///
/// Visual Basic finishes a line whenever you leave it, not only when you press
/// Enter, and a line abandoned half-written would otherwise stay untidy.
/// </summary>
public sealed class FormatOnLeavingLineTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-leave", Guid.NewGuid().ToString("N"));

    public FormatOnLeavingLineTests() => Directory.CreateDirectory(_root);

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
    public async Task TidiesAVisualBasicLineWhenTheCaretLeavesIt()
    {
        // Typed and abandoned with an arrow key, never followed by Enter.
        var (host, code, editor) = await OpenAsync("Leave.vb", """
            Public Class A
                Sub M()
                    dim n as integer=5
                End Sub
            End Class
            """);
        using var _ = host;

        await code.FormatLineOnLeavingForTestsAsync(3);

        Assert.Contains("Dim n As Integer = 5", editor.Text);
    }

    [AvaloniaFact]
    public async Task CorrectsBlockKeywordsOnTheLineBeingLeft()
    {
        var (host, code, editor) = await OpenAsync("Block.vb", """
            Public Class A
                Sub M()
                    If x > 0 Then
                    end if
                End Sub
            End Class
            """);
        using var _ = host;

        await code.FormatLineOnLeavingForTestsAsync(4);

        Assert.Contains("End If", editor.Text);
        Assert.DoesNotContain("end if", editor.Text);
    }

    [AvaloniaFact]
    public async Task LeavesTheCaretAloneWhenItSitsBeforeTheEditedLine()
    {
        var (host, code, editor) = await OpenAsync("Caret.vb", """
            Public Class A
                Sub M()
                    dim n as integer=5
                End Sub
            End Class
            """);
        using var _ = host;

        // Caret on line 2, formatting line 3 below it.
        editor.CaretOffset = editor.Text.IndexOf("Sub M()", StringComparison.Ordinal);
        var before = editor.CaretOffset;

        await code.FormatLineOnLeavingForTestsAsync(3);

        Assert.Equal(before, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task MovesTheCaretAlongWhenItSitsAfterTheEditedLine()
    {
        var (host, code, editor) = await OpenAsync("CaretAfter.vb", """
            Public Class A
                Sub M()
                    dim n as integer=5
                    Dim marker As Integer = 0
                End Sub
            End Class
            """);
        using var _ = host;

        // The caret follows the same character it was on, even though the line
        // above it grew by the spaces that were inserted.
        var markerBefore = editor.Text.IndexOf("marker", StringComparison.Ordinal);
        editor.CaretOffset = markerBefore;

        await code.FormatLineOnLeavingForTestsAsync(3);

        var markerAfter = editor.Text.IndexOf("marker", StringComparison.Ordinal);
        Assert.Equal(markerAfter, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task IgnoresAnEmptyLine()
    {
        var (host, code, editor) = await OpenAsync("Empty.vb", """
            Public Class A
                Sub M()

                End Sub
            End Class
            """);
        using var _ = host;

        var before = editor.Text;
        await code.FormatLineOnLeavingForTestsAsync(3);

        Assert.Equal(before, editor.Text);
    }

    [AvaloniaFact]
    public async Task IgnoresALineNumberOutsideTheDocument()
    {
        var (host, code, editor) = await OpenAsync("Range.vb", "Public Class A\nEnd Class");
        using var _ = host;

        var before = editor.Text;

        await code.FormatLineOnLeavingForTestsAsync(999);
        await code.FormatLineOnLeavingForTestsAsync(0);

        Assert.Equal(before, editor.Text);
    }

    [AvaloniaFact]
    public async Task DoesNothingWhenAutomaticFormattingIsOff()
    {
        var (host, code, editor) = await OpenAsync("Off.vb", """
            Public Class A
                Sub M()
                    dim n as integer=5
                End Sub
            End Class
            """);
        using var _ = host;

        code.AutoFormatWhileTyping = false;
        await code.FormatLineOnLeavingForTestsAsync(3);

        Assert.Contains("dim n as integer=5", editor.Text);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
