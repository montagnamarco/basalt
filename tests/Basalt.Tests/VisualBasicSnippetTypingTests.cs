using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class VisualBasicSnippetTypingTests
{
    private const string Prefix = "Class C\n    Sub M()\n        ";
    private const string Suffix = "\n        Dim following = 1\n    End Sub\nEnd Class";

    [AvaloniaFact]
    public async Task QuestionTabWritesConsoleOutputWithTheCaretInsideParentheses()
    {
        using var session = new Session("?");
        session.Press(Key.Tab);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!session.Editor.Text.Contains("Console.WriteLine()", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.Equal(Prefix + "Console.WriteLine()" + Suffix, session.Editor.Text);
        Assert.Equal(Prefix.Length + "Console.WriteLine(".Length, session.Editor.CaretOffset);
        session.Code.AutoFormatWhileTyping = false;
        session.Editor.Document.UndoStack.Undo();
        Assert.Equal(Prefix + "?" + Suffix, session.Editor.Text);
        Assert.False(session.Editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task TabTabExpandsIfAndTabFinishesAtTheBody(string newline)
    {
        using var session = new Session("if", newline);
        await session.ExpandAsync("condition");
        var expected = (Prefix + "If condition Then\n            \n        End If" + Suffix).ReplaceLineEndings(newline);
        Assert.Equal(expected, session.Editor.Text);
        Assert.Equal("condition", session.Editor.SelectedText);

        session.Window.KeyTextInput("ready");
        session.Press(Key.Tab);

        Assert.Equal(expected.Replace("condition", "ready", StringComparison.Ordinal), session.Editor.Text);
        Assert.Equal(0, session.Editor.SelectionLength);
        Assert.Equal(session.Editor.Document.GetLineByNumber(4).EndOffset, session.Editor.CaretOffset);
    }

    [AvaloniaFact]
    public async Task TabAndShiftTabTrackEditedFieldLengths()
    {
        using var session = new Session("for");
        await session.ExpandAsync("i");
        session.Window.KeyTextInput("index");
        session.Press(Key.Tab);
        Assert.Equal("count", session.Editor.SelectedText);
        session.Window.KeyTextInput("limit");
        session.Press(Key.Tab, RawInputModifiers.Shift);
        Assert.Equal("index", session.Editor.SelectedText);
        session.Press(Key.Tab);
        Assert.Equal("limit", session.Editor.SelectedText);
        session.Press(Key.Tab);
        Assert.Equal(Prefix + "For index As Integer = 0 To limit\n            \n        Next" + Suffix, session.Editor.Text);
        Assert.Equal(session.Editor.Document.GetLineByNumber(4).EndOffset, session.Editor.CaretOffset);
    }

    [AvaloniaTheory]
    [InlineData(Key.Enter)]
    [InlineData(Key.Escape)]
    public async Task FinishingAFieldDoesNotInsertAnExtraLine(Key key)
    {
        using var session = new Session("if");
        await session.ExpandAsync("condition");
        var text = session.Editor.Text;
        session.Press(key);
        Assert.Equal(text, session.Editor.Text);
        Assert.Equal(0, session.Editor.SelectionLength);
    }

    [AvaloniaFact]
    public async Task ExpansionUndoesInOneStep()
    {
        using var session = new Session("if");
        await session.ExpandAsync("condition");
        session.Code.AutoFormatWhileTyping = false;
        session.Editor.Document.UndoStack.Undo();
        Assert.Equal(Prefix + "if" + Suffix, session.Editor.Text);
        Assert.False(session.Editor.Document.UndoStack.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData("text")]
    [InlineData("caret")]
    [InlineData("escape")]
    [InlineData("selection")]
    [InlineData("document")]
    public async Task PendingExpansionNeverReplacesNewerInput(string action)
    {
        var editor = new TextEditor { Text = Prefix + "if" + Suffix, CaretOffset = Prefix.Length + 2 };
        var input = new VisualBasicSnippetInput(editor, action => action());
        await input.HandleKey(new KeyEventArgs { Key = Key.Tab })!;
        var pending = input.HandleKey(new KeyEventArgs { Key = Key.Tab })!;
        switch (action)
        {
            case "text":
                editor.Document.Insert(editor.CaretOffset, " changed");
                break;
            case "caret":
                editor.CaretOffset = 0;
                break;
            case "escape":
                _ = input.HandleKey(new KeyEventArgs { Key = Key.Escape });
                break;
            case "selection":
                editor.Select(Prefix.Length, 2);
                break;
            case "document":
                editor.Document = new AvaloniaEdit.Document.TextDocument("replacement");
                break;
        }
        var snapshot = editor.Text;
        await pending;
        Assert.Equal(snapshot, editor.Text);
        Assert.False(input.IsActive);
    }

    [AvaloniaTheory]
    [InlineData("Class C\n    Sub M()\n        Dim xml = <node>\nif\n</node>\n    End Sub\nEnd Class", "if")]
    [InlineData("Class C\n    Sub M()\n        Dim text = \"first\nif\nlast\"\n    End Sub\nEnd Class", "if")]
    [InlineData("Class C\n    Sub M()\n        Dim xml = <node>\n?\n</node>\n    End Sub\nEnd Class", "?")]
    [InlineData("Class C\n    Sub M()\n        Dim text = \"first\n?\nlast\"\n    End Sub\nEnd Class", "?")]
    public async Task TextThatLooksLikeAShortcutDoesNotExpandInsideLiterals(string source, string shortcut)
    {
        var editor = new TextEditor
        {
            Text = source,
            CaretOffset = source.IndexOf("\n" + shortcut + "\n", StringComparison.Ordinal) + shortcut.Length + 1
        };
        var input = new VisualBasicSnippetInput(editor, action => action());
        await input.HandleKey(new KeyEventArgs { Key = Key.Tab })!;
        await input.HandleKey(new KeyEventArgs { Key = Key.Tab })!;
        Assert.Equal(source, editor.Text);
        Assert.False(input.IsActive);
    }

    private sealed class Session : IDisposable
    {
        private readonly MainWindowViewModel _shell = new();
        public CodeEditor Code { get; }
        public Window Window { get; }
        public TextEditor Editor { get; }

        public Session(string shortcut, string newline = "\n")
        {
            var source = (Prefix + shortcut + Suffix).ReplaceLineEndings(newline);
            Code = new CodeEditor(new EditorDocumentViewModel("Snippet.vb", source), _shell)
            {
                AutoFormatWhileTyping = false
            };
            Window = new Window { Content = Code, Width = 800, Height = 600 };
            Window.Show();
            Window.UpdateLayout();
            Editor = Code.GetVisualDescendants().OfType<TextEditor>().Single();
            Editor.CaretOffset = Editor.Document.GetLineByNumber(3).EndOffset;
            Editor.TextArea.Focus();
            Editor.Document.UndoStack.ClearAll();
            Code.AutoFormatWhileTyping = true;
        }

        public async Task ExpandAsync(string firstField)
        {
            var before = Editor.Text;
            Press(Key.Tab);
            Assert.Equal(before, Editor.Text);
            Press(Key.Tab);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Editor.SelectedText != firstField && DateTime.UtcNow < deadline) await Task.Delay(10);
            Assert.Equal(firstField, Editor.SelectedText);
        }

        public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
        {
            var physicalKey = key switch
            {
                Key.Tab => PhysicalKey.Tab,
                Key.Enter => PhysicalKey.Enter,
                Key.Escape => PhysicalKey.Escape,
                _ => PhysicalKey.None
            };
            Window.KeyPress(key, modifiers, physicalKey, null);
            Window.KeyRelease(key, modifiers, physicalKey, null);
        }

        public void Dispose()
        {
            Code.AutoFormatWhileTyping = false;
            Window.Close();
            _shell.Dispose();
        }
    }
}
