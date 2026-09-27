using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class VisualBasicEnterRaceTests : IAsyncLifetime
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-enter-race-").FullName;
    private MainWindowViewModel _shell = null!;

    public async ValueTask InitializeAsync()
    {
        var project = Path.Combine(_root, "Typing.vbproj");
        await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><RootNamespace></RootNamespace></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(_root, "HomeController.vb"), Source);
        _shell = new MainWindowViewModel();
        await _shell.OpenSolutionAsync(project);
    }

    public ValueTask DisposeAsync()
    {
        _shell.Dispose();
        ScratchFolder.Delete(_root);
        return ValueTask.CompletedTask;
    }

    private const string Source = "Class HomeController\n    Function Index() As String\n        Dim view As New Global.Views.Home.Index()\n        view.Model = New HomeViewModel()\n        Dim x As UInteger\n        x = x + 34\n        if x=0 then\n        Return Content(view.Render(), \"text/html\")\n    End Function\nEnd Class";

    private (Window Window, CodeEditor Code, TextEditor Editor) Open()
    {
        var code = new CodeEditor(new EditorDocumentViewModel(Path.Combine(_root, "HomeController.vb"), Source), _shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 800, Height = 600 };
        window.Show();
        window.UpdateLayout();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = Source.IndexOf("if x=0 then", StringComparison.Ordinal) + "if x=0 then".Length;
        editor.TextArea.Focus();
        editor.Document.UndoStack.ClearAll();
        code.AutoFormatWhileTyping = true;
        return (window, code, editor);
    }

    [AvaloniaFact]
    public async Task EnterInAnIfKeepsTheFollowingReturnIntact()
    {
        var (window, _, editor) = Open();
        try
        {
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("End If", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            var expected = Source.Replace("if x=0 then", "If x = 0 Then\n            \n        End If", StringComparison.Ordinal);
            Assert.Equal(expected, editor.Text);
            Assert.Equal(editor.Document.GetLineByNumber(8).EndOffset, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EnterInsertsItsNewlineBeforeLaterTextCanArrive()
    {
        var (window, code, editor) = Open();
        try
        {
            var pending = code.HandleEnterForTestsAsync();
            var afterEnter = editor.Text;
            var line = editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;
            // Route the input before pumping queued formatter continuations.
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "x = 12" });
            var typed = editor.Text;
            await pending;

            Assert.Equal(8, line);
            Assert.Equal(Source.Replace("if x=0 then", "if x=0 then\n        ", StringComparison.Ordinal), afterEnter);
            Assert.Equal(typed, editor.Text);
            Assert.Contains("\n        x = 12\n        Return Content", editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task PendingEnterDoesNotFollowTheCaretIntoReturn()
    {
        var (window, code, editor) = Open();
        try
        {
            var pending = code.HandleEnterForTestsAsync();
            code.AutoFormatWhileTyping = false;
            editor.CaretOffset = editor.Text.IndexOf("Return", StringComparison.Ordinal) + 4;
            var caret = editor.CaretOffset;
            var snapshot = editor.Text;
            await pending;

            Assert.Contains("Return Content(view.Render(), \"text/html\")", editor.Text);
            Assert.Equal(snapshot, editor.Text);
            Assert.Equal(caret, editor.CaretOffset);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ConsecutiveEnterKeysEachInsertOnlyAtTheirOwnCaret()
    {
        var (window, code, editor) = Open();
        try
        {
            var first = code.HandleEnterForTestsAsync();
            var second = code.HandleEnterForTestsAsync();
            var third = code.HandleEnterForTestsAsync();
            // Keep all three requests pending until this input changes the document.
            editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = "' next line" });
            var snapshot = editor.Text;
            await Task.WhenAll(first, second, third);

            Assert.Equal(snapshot, editor.Text);
            Assert.Equal(Source.Replace("if x=0 then", "if x=0 then\n        \n        \n        ' next line", StringComparison.Ordinal), editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EnterPreservesLineEndingsAndUndoesInOneStep(string newline)
    {
        var (window, code, editor) = Open();
        try
        {
            code.AutoFormatWhileTyping = false;
            var source = Source.ReplaceLineEndings(newline);
            editor.Text = source;
            editor.CaretOffset = source.IndexOf("if x=0 then", StringComparison.Ordinal) + "if x=0 then".Length;
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            await code.HandleEnterForTestsAsync();

            var expected = source.Replace("if x=0 then", $"If x = 0 Then{newline}            {newline}        End If", StringComparison.Ordinal);
            Assert.Equal(expected, editor.Text);
            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();
            Assert.Equal(source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
            editor.Document.UndoStack.Redo();
            Assert.Equal(expected, editor.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EnterReplacesSelectedTextAndUndoRestoresItWithCrLf()
    {
        var (window, code, editor) = Open();
        try
        {
            code.AutoFormatWhileTyping = false;
            var source = Source.Replace("then", "then discard", StringComparison.Ordinal).ReplaceLineEndings("\r\n");
            editor.Text = source;
            editor.Select(source.IndexOf(" discard", StringComparison.Ordinal), " discard".Length);
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            await code.HandleEnterForTestsAsync();

            Assert.Equal(Source.Replace("if x=0 then", "If x = 0 Then\n            \n        End If", StringComparison.Ordinal)
                .ReplaceLineEndings("\r\n"), editor.Text);
            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();
            Assert.Equal(source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task UndoDuringPendingEnterDoesNotLetTheReplyRestoreTheNewline()
    {
        var (window, code, editor) = Open();
        try
        {
            var pending = code.HandleEnterForTestsAsync();
            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();
            await pending;

            Assert.Equal(Source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LeavingAnOmittedThenWithDownPreservesTheFollowingStatementAndCaret()
    {
        var (window, code, editor) = Open();
        try
        {
            code.AutoFormatWhileTyping = false;
            var source = Source.Replace("if x=0 then", "if x=0", StringComparison.Ordinal);
            editor.Text = source;
            editor.CaretOffset = editor.Document.GetLineByNumber(7).EndOffset;
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            var column = editor.TextArea.Caret.Column;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("If x = 0 Then", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(source.Replace("if x=0", "If x = 0 Then", StringComparison.Ordinal), editor.Text);
            Assert.Equal(8, editor.TextArea.Caret.Line);
            Assert.Equal(column, editor.TextArea.Caret.Column);
            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();
            Assert.Equal(source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EnterCompletesAnOmittedThenAndClosesTheIfBeforeReturn()
    {
        var (window, code, editor) = Open();
        try
        {
            code.AutoFormatWhileTyping = false;
            editor.Text = Source.Replace("if x=0 then", "if x=0", StringComparison.Ordinal);
            editor.CaretOffset = editor.Document.GetLineByNumber(7).EndOffset;
            code.AutoFormatWhileTyping = true;

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("End If", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(Source.Replace("if x=0 then", "If x = 0 Then\n            \n        End If", StringComparison.Ordinal), editor.Text);
            Assert.Equal(editor.Document.GetLineByNumber(8).EndOffset, editor.CaretOffset);
        }
        finally { window.Close(); }
    }
}
