using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class DocumentationTypingTests
{
    private const string Source = "Class C\n    ''\n    Function Read(value As Integer) As String\n    End Function\nEnd Class";

    [AvaloniaFact]
    public async Task ThirdApostropheGeneratesDocumentationAndPlacesTheCaretInSummary()
    {
        using var shell = new MainWindowViewModel();
        var code = new CodeEditor(new EditorDocumentViewModel("Documentation.vb", Source), shell);
        var window = new Window { Content = code, Width = 700, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
            editor.CaretOffset = Source.IndexOf("''", StringComparison.Ordinal) + 2;
            editor.TextArea.Focus();
            window.KeyTextInput("'");

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!editor.Text.Contains("<summary>", StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            const string expected = "Class C\n    ''' <summary>\n    ''' \n    ''' </summary>\n    ''' <param name=\"value\"></param>\n    ''' <returns></returns>\n    Function Read(value As Integer) As String\n    End Function\nEnd Class";
            Assert.Equal(expected, editor.Text);
            Assert.Equal(editor.Document.GetLineByNumber(3).EndOffset, editor.CaretOffset);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DoesNotApplyDocumentationAfterTheDocumentChanges()
    {
        var text = Source.Replace("''", "'''");
        var editor = new TextEditor { Text = text };
        editor.CaretOffset = text.IndexOf("'''", StringComparison.Ordinal) + 3;

        var generation = VisualBasicDocumentationInput.TryGenerateAsync(editor);
        editor.Document.Insert(editor.CaretOffset, " user text");
        var edited = editor.Text;

        Assert.False(await generation);
        Assert.Equal(edited, editor.Text);
    }

    [AvaloniaFact]
    public async Task DoesNotApplyDocumentationAfterTheCaretMoves()
    {
        var text = Source.Replace("''", "'''");
        var editor = new TextEditor { Text = text };
        editor.CaretOffset = text.IndexOf("'''", StringComparison.Ordinal) + 3;

        var generation = VisualBasicDocumentationInput.TryGenerateAsync(editor);
        editor.CaretOffset = 0;

        Assert.False(await generation);
        Assert.Equal(text, editor.Text);
        Assert.Equal(0, editor.CaretOffset);
    }
}
