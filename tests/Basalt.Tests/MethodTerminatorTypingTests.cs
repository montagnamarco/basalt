using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class MethodTerminatorTypingTests
{
    [AvaloniaTheory]
    [InlineData("Function M() As Integer", "End Sub", "End Function")]
    [InlineData("Sub M()", "End Function", "End Sub")]
    public async Task CorrectsTheAssociatedTerminatorInTheEditorAsOneUndo(
        string declaration, string oldTerminator, string newTerminator)
    {
        var source = $"Class C\n    {declaration}\n        ' Keep the body intact.\n    {oldTerminator}\nEnd Class";
        using var shell = new MainWindowViewModel();
        var code = new CodeEditor(new EditorDocumentViewModel("Terminator.vb", source), shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.CaretOffset = editor.Document.GetLineByNumber(2).EndOffset;
            var caret = editor.CaretOffset;
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            await code.ApplyVisualBasicConventionsAsync();

            Assert.Equal(source.Replace(oldTerminator, newTerminator, StringComparison.Ordinal), editor.Text);
            Assert.Equal(caret, editor.CaretOffset);
            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();
            Assert.Equal(source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task LeavingTheDeclarationWithDownUpdatesTheClosingLine()
    {
        const string source = "Class C\n    Function M() As Integer\n        Return 1\n    End Sub\nEnd Class";
        using var shell = new MainWindowViewModel();
        var code = new CodeEditor(new EditorDocumentViewModel("Terminator.vb", source), shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.CaretOffset = editor.Document.GetLineByNumber(2).EndOffset;
            editor.TextArea.Focus();
            code.AutoFormatWhileTyping = true;

            window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            window.KeyRelease(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
            var caret = editor.CaretOffset;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("End Function", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(source.Replace("End Sub", "End Function", StringComparison.Ordinal), editor.Text);
            Assert.Equal(caret, editor.CaretOffset);
            Assert.Equal(3, editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void SpanEditsPreserveCrLfAndMoveTheCurrentCaretPastEarlierEdits()
    {
        const string source = "Class C\r\nfunction M() As Integer\r\n    End Sub\r\nEnd Class";
        const string updated = "Class C\r\n    Function M() As Integer\r\n    End Function\r\nEnd Class";
        var editor = new TextEditor { Text = source, CaretOffset = source.Length };
        editor.Document.UndoStack.ClearAll();

        Assert.True(EditorTypingChanges.Apply(editor, source, updated));
        Assert.Equal(updated, editor.Text);
        Assert.Equal(updated.Length, editor.CaretOffset);
        editor.Document.UndoStack.Undo();
        Assert.Equal(source, editor.Text);
        Assert.False(editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("user edit", "Class C\nEnd Class")]
    [InlineData("Class C\nEnd Class", "Class C\n\nEnd Class")]
    [InlineData("Class C\nEnd Class", "Class C\r\nEnd Class")]
    public void RefusesStaleSnapshotsOrChangedLineStructure(string current, string updated)
    {
        const string snapshot = "Class C\nEnd Class";
        var editor = new TextEditor { Text = current, CaretOffset = current.Length };

        Assert.False(EditorTypingChanges.Apply(editor, snapshot, updated));
        Assert.Equal(current, editor.Text);
        Assert.Equal(current.Length, editor.CaretOffset);
    }
}
