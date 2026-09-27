using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class ArgumentlessInvocationTypingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletingACallPreservesTheFollowingStatementAndUndoesInOneStep(bool enter)
    {
        const string source = "Class C\n    Sub Work()\n    End Sub\n    Sub Run()\n        Work\n        Dim nextValue = 1\n    End Sub\nEnd Class";
        using var shell = new MainWindowViewModel();
        var code = new CodeEditor(new EditorDocumentViewModel("Invocation.vb", source), shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.CaretOffset = editor.Document.GetLineByNumber(5).EndOffset;
            editor.TextArea.Focus();
            editor.Document.UndoStack.ClearAll();
            code.AutoFormatWhileTyping = true;

            var key = enter ? Key.Enter : Key.Down;
            var physicalKey = enter ? PhysicalKey.Enter : PhysicalKey.ArrowDown;
            window.KeyPress(key, RawInputModifiers.None, physicalKey, null);
            window.KeyRelease(key, RawInputModifiers.None, physicalKey, null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("        Work()", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            var replacement = enter ? "        Work()\n        " : "        Work()";
            Assert.Equal(source.Replace("        Work", replacement, StringComparison.Ordinal), editor.Text);
            Assert.Equal(6, editor.TextArea.Caret.Line);
            if (enter) Assert.Equal(editor.Document.GetLineByNumber(6).EndOffset, editor.CaretOffset);
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
