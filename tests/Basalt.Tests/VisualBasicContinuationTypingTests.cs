using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class VisualBasicContinuationTypingTests
{
    [AvaloniaTheory]
    [InlineData("\n", "Dim total = 1 + _", 12)]
    [InlineData("\r\n", "Dim total = 1 + _", 12)]
    [InlineData("\n", "Dim total = 1 +", 12)]
    [InlineData("\r\n", "Dim total = 1 +", 12)]
    [InlineData("\n", "Dim total = Add(", 12)]
    [InlineData("\r\n", "Dim total = Add(", 12)]
    [InlineData("\n", "Dim total = Add(1,", 24)]
    [InlineData("\r\n", "Dim total = Add(1,", 24)]
    public async Task EnterIndentsContinuationsAndPreservesFollowingTextCaretAndUndo(string newline, string header, int indentation)
    {
        var source = $"Class C{newline}    Sub M(){newline}        {header}{newline}        Dim nextValue = 0{newline}    End Sub{newline}End Class";
        using var shell = new MainWindowViewModel();
        var code = new CodeEditor(new EditorDocumentViewModel("Continuation.vb", source), shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.CaretOffset = editor.Text.IndexOf(header, StringComparison.Ordinal) + header.Length;
            editor.TextArea.Focus();
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            var expected = source.Replace(header, header + newline + new string(' ', indentation), StringComparison.Ordinal);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (editor.Text != expected && DateTime.UtcNow < deadline) await Task.Delay(20);

            Assert.Equal(expected, editor.Text);
            Assert.Equal(editor.Document.GetLineByNumber(4).EndOffset, editor.CaretOffset);
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
}
