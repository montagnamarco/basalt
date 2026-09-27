using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class CompletionKeyboardTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-completion-keys-").FullName;
    private MainWindowViewModel _shell = null!;

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
        await File.WriteAllTextAsync(Path.Combine(_root, "Typing.vb"), Source("CompleteTar"));
        _shell = new MainWindowViewModel();
        await _shell.OpenSolutionAsync(project);
    }

    private static string Source(string word) =>
        "Module Program\n    Sub CompleteTarget()\n    End Sub\n    Sub Main()\n        " +
        word + "\n    End Sub\nEnd Module\n";

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Tab)]
    public async Task AcceptsAPartialWordWithoutInsertingANewLine(Key key)
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(key, RawInputModifiers.None, key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter, null);
            window.KeyRelease(key, RawInputModifiers.None, key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter, null);
            await Task.Delay(100);

            Assert.Equal(Source("CompleteTarget"), editor.Text);
            Assert.Equal(editor.Text.IndexOf("        CompleteTarget", StringComparison.Ordinal) +
                "        CompleteTarget".Length, editor.CaretOffset);
            Assert.Null(code.SelectedCompletionForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EnterAfterAFullyTypedWordAlsoInsertsANewLine()
    {
        var (window, code, editor) = await OpenAsync("CompleteTarget");
        try
        {
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Task.Delay(100);

            Assert.Equal(Source("CompleteTarget\n"), editor.Text);
            Assert.Equal(editor.Document.GetLineByNumber(6).Offset, editor.CaretOffset);
            Assert.Null(code.SelectedCompletionForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EscapeDismissesWithoutAcceptingTheWord()
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            var caret = editor.CaretOffset;
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);

            Assert.Equal(Source("CompleteTar"), editor.Text);
            Assert.Equal(caret, editor.CaretOffset);
            Assert.Null(code.SelectedCompletionForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ControlJInvokesCompletion()
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            Assert.Null(code.SelectedCompletionForTests);

            window.KeyPress(Key.J, RawInputModifiers.Control, PhysicalKey.J, null);
            window.KeyRelease(Key.J, RawInputModifiers.Control, PhysicalKey.J, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (code.SelectedCompletionForTests is null && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal("CompleteTarget", code.SelectedCompletionForTests);
            Assert.Equal(Source("CompleteTar"), editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ControlShiftSpaceInvokesSignatureHelp()
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            editor.Text = Source("CompleteTarget(");
            editor.CaretOffset = editor.Text.IndexOf("        CompleteTarget(", StringComparison.Ordinal) +
                "        CompleteTarget(".Length;

            var modifiers = RawInputModifiers.Control | RawInputModifiers.Shift;
            window.KeyPress(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, modifiers, PhysicalKey.Space, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (code.SignatureHelpCountForTests == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.True(code.SignatureHelpCountForTests > 0);
            Assert.Null(code.SelectedCompletionForTests);
            Assert.Equal(Source("CompleteTarget("), editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypingAnOpeningParenthesisShowsTheCallSignature(bool completeBrackets)
    {
        var (window, code, editor) = await OpenAsync("CompleteTarget");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            code.AutoCloseBrackets = completeBrackets;
            window.KeyTextInput("(");
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (code.SignatureHelpCountForTests == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.True(code.SignatureHelpCountForTests > 0);
            Assert.Equal(Source(completeBrackets ? "CompleteTarget()" : "CompleteTarget("), editor.Text);
            Assert.Equal(editor.Text.IndexOf("        CompleteTarget(", StringComparison.Ordinal) +
                "        CompleteTarget(".Length, editor.CaretOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(" ", " ", 1)]
    [InlineData(".", ".", 1)]
    [InlineData("(", "()", 1)]
    [InlineData(")", ")", 1)]
    [InlineData(",", ",", 1)]
    [InlineData("=", "=", 1)]
    [InlineData(":", ":", 1)]
    public async Task TypedCommitCharacterAcceptsTheWordAndUsesNormalTextInput(
        string typed, string suffix, int caretAdvance)
    {
        var (window, _, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyTextInput(typed);

            Assert.Equal(Source("CompleteTarget" + suffix), editor.Text);
            Assert.Equal(editor.Text.IndexOf("        CompleteTarget", StringComparison.Ordinal) +
                "        CompleteTarget".Length + caretAdvance, editor.CaretOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PhysicalBracketKeyDoesNotInventAnOpeningParenthesis()
    {
        var (window, _, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.OemOpenBrackets, RawInputModifiers.None, PhysicalKey.BracketLeft, null);
            window.KeyTextInput("[");
            window.KeyRelease(Key.OemOpenBrackets, RawInputModifiers.None, PhysicalKey.BracketLeft, null);

            Assert.Equal(Source("CompleteTar[]"), editor.Text);
            Assert.Equal(editor.Text.IndexOf("        CompleteTar", StringComparison.Ordinal) +
                "        CompleteTar[".Length, editor.CaretOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SuggestionModeLeavesTypedSeparatorsUncommittedAndCanBeTurnedOff()
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            var modifiers = RawInputModifiers.Control | RawInputModifiers.Alt;
            window.KeyPress(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyTextInput(" ");
            Assert.Equal(Source("CompleteTar "), editor.Text);

            editor.Text = Source("CompleteTar");
            editor.CaretOffset = editor.Text.IndexOf("        CompleteTar", StringComparison.Ordinal) +
                "        CompleteTar".Length;
            await code.ShowCompletionForTestsAsync();
            window.KeyPress(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyTextInput(" ");
            Assert.Equal(Source("CompleteTarget "), editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(Key.Tab)]
    [InlineData(Key.Enter)]
    public async Task SuggestionModeStillAcceptsExplicitCommitKeys(Key key)
    {
        var (window, _, editor) = await OpenAsync("CompleteTar");
        try
        {
            var modifiers = RawInputModifiers.Control | RawInputModifiers.Alt;
            window.KeyPress(Key.Space, modifiers, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, modifiers, PhysicalKey.Space, null);
            var physicalKey = key == Key.Tab ? PhysicalKey.Tab : PhysicalKey.Enter;
            window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
            Assert.Equal(Source("CompleteTarget"), editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardsCompletionAfterCaretMovementOrEqualLengthEdit(bool editText)
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            var pending = code.ShowCompletionForTestsAsync();
            if (editText)
                editor.Document.Replace(editor.CaretOffset - 3, 3, "XYZ");
            else
                editor.CaretOffset = 0;

            var expected = editor.Text;
            await pending;

            Assert.Null(code.SelectedCompletionForTests);
            Assert.Equal(expected, editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardsSignatureAfterCaretMovementOrEqualLengthEdit(bool editText)
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            editor.Text = Source("CompleteTarget(");
            editor.CaretOffset = editor.Text.IndexOf("        CompleteTarget(", StringComparison.Ordinal) +
                "        CompleteTarget(".Length;

            var pending = code.ShowSignatureHelpForTestsAsync();
            if (editText)
                editor.Document.Replace(editor.CaretOffset - 2, 1, "X");
            else
                editor.CaretOffset = 0;
            await pending;

            Assert.Equal(0, code.SignatureHelpCountForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task NewerCompletionRequestWinsOverAnEarlierContext()
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            editor.Text = Source("Console.WriteLine()\n        CompleteTar");
            editor.CaretOffset = editor.Text.IndexOf("Console.", StringComparison.Ordinal) + "Console.".Length;
            var first = code.ShowCompletionForTestsAsync();
            editor.CaretOffset = editor.Text.IndexOf("        CompleteTar", StringComparison.Ordinal) +
                "        CompleteTar".Length;
            var second = code.ShowCompletionForTestsAsync();
            await Task.WhenAll(first, second);

            Assert.Equal("CompleteTarget", code.SelectedCompletionForTests);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal(Source("Console.WriteLine()\n        CompleteTarget"), editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReturningToAnEarlierCaretDoesNotReviveItsSupersededRequest(bool signature)
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            var expression = signature ? "CompleteTarget()" : "CompleteTar";
            var prefixLength = signature ? "CompleteTarget(".Length : expression.Length;
            editor.Text = Source(expression + "\n        " + expression);
            var firstPosition = editor.Text.IndexOf("        " + expression, StringComparison.Ordinal) +
                8 + prefixLength;
            var secondPosition = editor.Text.LastIndexOf("        " + expression, StringComparison.Ordinal) +
                8 + prefixLength;
            editor.CaretOffset = firstPosition;
            var first = signature ? code.ShowSignatureHelpForTestsAsync() : code.ShowCompletionForTestsAsync();
            editor.CaretOffset = secondPosition;
            var second = signature ? code.ShowSignatureHelpForTestsAsync() : code.ShowCompletionForTestsAsync();
            editor.CaretOffset = firstPosition;
            await Task.WhenAll(first, second);

            Assert.Null(code.SelectedCompletionForTests);
            Assert.Equal(0, code.SignatureHelpCountForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EscapePreventsAnInFlightRequestFromOpeningAPopup(bool signature)
    {
        var (window, code, editor) = await OpenAsync("CompleteTar");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            if (signature)
            {
                editor.Text = Source("CompleteTarget(");
                editor.CaretOffset = editor.Text.IndexOf("        CompleteTarget(", StringComparison.Ordinal) +
                    "        CompleteTarget(".Length;
            }
            var pending = signature ? code.ShowSignatureHelpForTestsAsync() : code.ShowCompletionForTestsAsync();
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await pending;

            Assert.Null(code.SelectedCompletionForTests);
            Assert.Equal(0, code.SignatureHelpCountForTests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ArmingASnippetCancelsTheCompletionQueuedByTypingItsShortcut()
    {
        var (window, code, editor) = await OpenAsync("if", "If");
        try
        {
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            editor.Text = Source("");
            editor.CaretOffset = editor.Document.GetLineByNumber(5).EndOffset;
            window.KeyTextInput("if");
            var requests = code.CompletionRequestsForTests;
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await Task.Delay(600);

            Assert.Equal(requests, code.CompletionRequestsForTests);
            Assert.False(code.CompletionOpenForTests);
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (editor.SelectedText != "condition" && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(Source("If condition Then\n            \n        End If"), editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task TabCommitsTheKeywordAndTheNextTabExpandsItsSnippet()
    {
        var (window, code, editor) = await OpenAsync("if", "If");
        try
        {
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            Assert.Equal(Source("If"), editor.Text);
            Assert.False(code.CompletionOpenForTests);

            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (editor.SelectedText != "condition" && DateTime.UtcNow < deadline) await Task.Delay(10);

            Assert.Equal(Source("If condition Then\n            \n        End If"), editor.Text);
            Assert.Equal("condition", editor.SelectedText);
        }
        finally { window.Close(); }
    }

    private async Task<(Window Window, CodeEditor Code, TextEditor Editor)> OpenAsync(string word, string expected = "CompleteTarget")
    {
        var text = Source(word);
        var code = new CodeEditor(new EditorDocumentViewModel(Path.Combine(_root, "Typing.vb"), text), _shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        window.Show();
        window.UpdateLayout();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = text.IndexOf("        " + word, StringComparison.Ordinal) + 8 + word.Length;
        editor.TextArea.Focus();
        await code.ShowCompletionForTestsAsync();
        Assert.Equal(expected, code.SelectedCompletionForTests);
        return (window, code, editor);
    }

    public ValueTask DisposeAsync()
    {
        _shell.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        return ValueTask.CompletedTask;
    }
}
