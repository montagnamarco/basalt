using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Typing brackets in the real editor.
///
/// The events are raised rather than the helper called directly: what makes
/// this worth testing is where the caret ends up, and that depends on how
/// AvaloniaEdit's own insertion interacts with the handler.
/// </summary>
public sealed class BracketTypingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-brackets", Guid.NewGuid().ToString("N"));

    public BracketTypingTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Types one character the way a keyboard would.</summary>
    private static void Type(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = editor.TextArea
        });
    }

    [AvaloniaFact]
    public async Task WritesTheClosingBracketWithTheOpeningOne()
    {
        var (host, _, editor) = await OpenAsync("A.vb", "Module A\n    Sub M()\n        Console\n");
        using var _h = host;

        editor.CaretOffset = editor.Text.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;

        Type(editor, "(");

        Assert.Contains("Console()", editor.Text);
    }

    [AvaloniaFact]
    public async Task LeavesTheCaretBetweenTheBrackets()
    {
        // The pair exists to be typed into; a caret outside it would make the
        // completion a nuisance rather than a help.
        var (host, _, editor) = await OpenAsync("B.vb", "Module A\n    Sub M()\n        Console\n");
        using var _h = host;

        var start = editor.Text.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;
        editor.CaretOffset = start;

        Type(editor, "(");

        Assert.Equal(start + 1, editor.CaretOffset);
        Assert.Equal(')', editor.Text[editor.CaretOffset]);
    }

    [AvaloniaFact]
    public async Task StepsOverTheClosingBracketInsteadOfDoublingIt()
    {
        var (host, _, editor) = await OpenAsync("C.vb", "Module A\n    Sub M()\n        Console\n");
        using var _h = host;

        var start = editor.Text.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;
        editor.CaretOffset = start;

        Type(editor, "(");
        Type(editor, ")");

        Assert.Contains("Console()", editor.Text);
        Assert.DoesNotContain("Console())", editor.Text);
        Assert.Equal(start + 2, editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task ClosesAQuoteAndTypesInsideIt()
    {
        var (host, _, editor) = await OpenAsync("D.vb", "Module A\n    Sub M()\n        Dim s = \n");
        using var _h = host;

        editor.CaretOffset = editor.Text.IndexOf("Dim s = ", StringComparison.Ordinal) + "Dim s = ".Length;

        Type(editor, "\"");
        Type(editor, "h");

        Assert.Contains("Dim s = \"h\"", editor.Text);
    }

    [AvaloniaFact]
    public async Task DoesNotCloseABracketBeforeAWord()
    {
        // "(" before "abc" is usually wrapping what follows, not opening a pair.
        var (host, _, editor) = await OpenAsync("E.vb", "Module A\n    Sub M()\n        abc\n");
        using var _h = host;

        editor.CaretOffset = editor.Text.IndexOf("abc", StringComparison.Ordinal);

        Type(editor, "(");

        Assert.Contains("(abc", editor.Text);
        Assert.DoesNotContain("()abc", editor.Text);
    }

    [AvaloniaFact]
    public async Task CanBeSwitchedOff()
    {
        var (host, code, editor) = await OpenAsync("F.vb", "Module A\n    Sub M()\n        Console\n");
        using var _h = host;

        code.AutoCloseBrackets = false;
        editor.CaretOffset = editor.Text.IndexOf("Console", StringComparison.Ordinal) + "Console".Length;

        Type(editor, "(");

        Assert.DoesNotContain("Console()", editor.Text);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
