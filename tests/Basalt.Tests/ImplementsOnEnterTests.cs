using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using AvaloniaEdit;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Enter after "Implements IGreeter" or "Inherits Shape" writes the members
/// the type now has to have, as Visual Studio does for Visual Basic.
/// </summary>
/// <remarks>
/// Before, the line stayed as typed, underlined with BC30149 until the
/// members were written by hand or through the light bulb.
/// </remarks>
public sealed class ImplementsOnEnterTests
{
    private const string Declarations =
        "Public Interface IGreeter\n" +
        "    Function Greet(name As String) As String\n" +
        "    Property Count As Integer\n" +
        "End Interface\n\n" +
        "Public MustInherit Class Shape\n" +
        "    Public MustOverride Function Area() As Double\n" +
        "End Class\n\n";

    private static string TypeWith(string line) =>
        Declarations + "Public Class Target\n" + line + "\n\nEnd Class\n";

    private static int LineOf(string text, string line) =>
        text[..text.IndexOf(line, StringComparison.Ordinal)].Count(character => character == '\n');

    /// <summary>
    /// The text once the edits are made, Enter having finished the last line
    /// of the given lines; null when there are no edits.
    /// </summary>
    private static async Task<string?> ImplementAsync(string lines)
    {
        using var service = new RoslynLanguageService();
        var text = TypeWith(lines);
        var lastLine = LineOf(text, lines) + lines.Count(character => character == '\n');

        var changes = await service.ImplementMembersAsync("Loose.vb", text, lastLine);

        return changes.Count == 0
            ? null
            : Microsoft.CodeAnalysis.Text.SourceText.From(text).WithChanges(changes).ToString();
    }

    [Fact]
    public async Task EveryInterfaceOnTheLineIsImplemented()
    {
        var written = await ImplementAsync("    Implements IGreeter, System.ICloneable");

        Assert.NotNull(written);
        Assert.Contains("Implements IGreeter.Greet", written);
        Assert.Contains("Implements ICloneable.Clone", written);
    }

    [Fact]
    public async Task AnImportsAddedByTheFirstFixDoesNotLoseTheNextInterface()
    {
        // The first fix writes an Imports above, which moves the line; the
        // second interface must still be found on it.
        var written = await ImplementAsync("    Implements System.ComponentModel.INotifyPropertyChanged, IGreeter");

        Assert.NotNull(written);
        Assert.StartsWith("Imports System.ComponentModel", written);
        Assert.Contains("Implements INotifyPropertyChanged.PropertyChanged", written);
        Assert.Contains("Implements IGreeter.Greet", written);
    }

    [Fact]
    public async Task AnImplementsContinuedOnTheNextLineIsImplementedWhenItEnds()
    {
        // Enter after the comma continues the statement; Enter after the last
        // interface finishes it, though the statement began a line above.
        Assert.Null(await ImplementAsync("    Implements IGreeter,"));

        var written = await ImplementAsync("    Implements IGreeter,\n               System.ICloneable");

        Assert.NotNull(written);
        Assert.Contains("Implements IGreeter.Greet", written);
        Assert.Contains("Implements ICloneable.Clone", written);
    }

    [Fact]
    public async Task ImplementsWritesTheInterfaceMembers()
    {
        var written = await ImplementAsync("    Implements IGreeter");

        Assert.NotNull(written);
        Assert.Contains("Public Function Greet(name As String) As String Implements IGreeter.Greet", written);
        Assert.Contains("Public Property Count As Integer Implements IGreeter.Count", written);
    }

    [Fact]
    public async Task ImplementsIDisposableWritesTheDisposePattern()
    {
        // Visual Basic's long-standing choice for IDisposable: Dispose calls
        // an Overridable Dispose(disposing), not an empty Sub.
        var written = await ImplementAsync("    Implements System.IDisposable");

        Assert.NotNull(written);
        Assert.Contains("Protected Overridable Sub Dispose(disposing As Boolean)", written);
        Assert.Contains("Public Sub Dispose() Implements IDisposable.Dispose", written);
    }

    [Fact]
    public async Task InheritsAMustInheritClassWritesItsMustOverrideMembers()
    {
        var written = await ImplementAsync("    Inherits Shape");

        Assert.NotNull(written);
        Assert.Contains("Public Overrides Function Area() As Double", written);
        Assert.DoesNotContain("MustInherit Class Target", written);
    }

    [Theory]
    [InlineData("    Dim unrelated As Integer")]
    [InlineData("    Implements System.ICloneable\n\n    Public Function Clone() As Object Implements System.ICloneable.Clone\n        Return Me\n    End Function")]
    public async Task ALineWithNothingMissingIsLeftAlone(string line)
    {
        Assert.Null(await ImplementAsync(line));
    }

    private static (Window Window, CodeEditor Code, TextEditor Editor) Open(
        MainWindowViewModel shell, string source, int caret)
    {
        var code = new CodeEditor(new EditorDocumentViewModel("Implements.vb", source), shell)
        {
            AutoFormatWhileTyping = false
        };
        var window = new Window { Content = code, Width = 700, Height = 500 };
        window.Show();
        window.UpdateLayout();
        var editor = code.GetVisualDescendants().OfType<TextEditor>().Single();
        editor.CaretOffset = caret;
        editor.TextArea.Focus();
        editor.Document.UndoStack.ClearAll();
        code.AutoFormatWhileTyping = true;
        return (window, code, editor);
    }

    private static void PressEnter(Window window)
    {
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task EnterWritesTheMembersAndOneUndoTakesThemBack(string newline)
    {
        const string line = "    Implements IGreeter";
        var source = (Declarations + "Public Class Target\n" + line + "\nEnd Class\n").Replace("\n", newline);

        using var shell = new MainWindowViewModel();
        var (window, code, editor) = Open(shell, source, source.IndexOf(line, StringComparison.Ordinal) + line.Length);
        try
        {
            PressEnter(window);

            const string member = "Implements IGreeter.Greet";
            var deadline = DateTime.UtcNow.AddSeconds(15);

            while (!editor.Text.Contains(member, StringComparison.Ordinal) && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Contains(member, editor.Text);

            // The file keeps its line breaks: Roslyn writes CRLF on Windows.
            var lineFeeds = editor.Text.Count(character => character == '\n');
            var returns = editor.Text.Count(character => character == '\r');
            Assert.Equal(newline == "\n" ? 0 : lineFeeds, returns);

            // The caret stays on the empty line Enter opened, below Implements.
            var implementsLine = editor.Document.GetLineByOffset(editor.Text.IndexOf(line, StringComparison.Ordinal)).LineNumber;
            Assert.Equal(implementsLine + 1, editor.TextArea.Caret.Line);
            var caretLine = editor.Document.GetLineByNumber(editor.TextArea.Caret.Line);
            Assert.Equal("", editor.Document.GetText(caretLine.Offset, caretLine.Length).Trim());

            code.AutoFormatWhileTyping = false;
            editor.Document.UndoStack.Undo();

            Assert.DoesNotContain(member, editor.Text);
            Assert.Contains(line + newline, editor.Text);

            // The second undo takes back the Enter itself.
            editor.Document.UndoStack.Undo();
            Assert.Equal(source, editor.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task TypingOnBeforeTheAnswerKeepsTheMembersOut()
    {
        // The members are written only into the text they were asked for: a
        // late answer must not land in the middle of what was typed since.
        const string line = "    Implements IGreeter";
        var source = Declarations + "Public Class Target\n" + line + "\nEnd Class\n";

        using var shell = new MainWindowViewModel();
        var (window, _, editor) = Open(shell, source, source.IndexOf(line, StringComparison.Ordinal) + line.Length);
        try
        {
            // Typed once Enter has finished and the members are on their way:
            // Enter's own check has passed, so only this step's can stop them.
            var arrived = false;
            VisualBasicImplementsInput.AnswerArrivedForTests = () =>
            {
                arrived = true;
                window.KeyTextInput("P");
            };

            PressEnter(window);

            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!arrived && DateTime.UtcNow < deadline) await Task.Delay(20);
            await Task.Delay(200);

            Assert.True(arrived, "the members were never asked for");
            Assert.DoesNotContain("Implements IGreeter.Greet", editor.Text);
            Assert.Contains("P", editor.Text[source.IndexOf(line, StringComparison.Ordinal)..]);
        }
        finally
        {
            VisualBasicImplementsInput.AnswerArrivedForTests = null;
            window.Close();
        }
    }
}
