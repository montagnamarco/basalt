using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using AvaloniaEdit.Folding;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Folding in the editor itself, not only in the strategy that finds the
/// blocks.
/// </summary>
public sealed class CodeEditorFoldingTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-editorfold-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private const string Code = """
        Module Program
            Sub Main()
                Dim x = 1
            End Sub
        End Module
        """;

    private (Window Window, CodeEditor Editor) Open(string name = "Program.vb")
    {
        var path = Path.Combine(_root, name);

        File.WriteAllText(path, Code);

        var vm = new MainWindowViewModel();
        var document = new EditorDocumentViewModel(path, Code);

        var editor = new CodeEditor(document, vm);

        var window = new Window { Content = editor, Width = 600, Height = 400 };

        window.Show();
        window.UpdateLayout();

        return (window, editor);
    }

    private static FoldingMargin? MarginIn(CodeEditor editor) =>
        editor.GetVisualDescendants().OfType<FoldingMargin>().FirstOrDefault();

    [AvaloniaFact]
    public void AVisualBasicFileGetsFoldMarks()
    {
        var (window, editor) = Open();

        Assert.NotNull(MarginIn(editor));

        window.Close();
    }

    [AvaloniaFact]
    public void AFileThatIsNotVisualBasicDoesNot()
    {
        // The strategy reads VB keywords; arrows that fold nothing would be
        // worse than none at all.
        var (window, editor) = Open("notes.json");

        Assert.Null(MarginIn(editor));

        window.Close();
    }

    [AvaloniaFact]
    public void FoldingEverythingClosesTheBlocks()
    {
        var (window, editor) = Open();

        editor.FoldAll();

        var sections = Sections(editor);

        Assert.NotEmpty(sections);
        Assert.All(sections, s => Assert.True(s.IsFolded));

        window.Close();
    }

    [AvaloniaFact]
    public void UnfoldingEverythingOpensThemAgain()
    {
        var (window, editor) = Open();

        editor.FoldAll();
        editor.UnfoldAll();

        Assert.All(Sections(editor), s => Assert.False(s.IsFolded));

        window.Close();
    }

    [AvaloniaFact]
    public void TheBlockAtTheCaretFoldsOnItsOwn()
    {
        var (window, editor) = Open();

        var inner = editor.TextForTests.IndexOf("Dim x", StringComparison.Ordinal);

        editor.CaretOffsetForTests = inner;

        editor.ToggleFoldAtCaret();

        // The innermost one: the Sub, not the Module around it.
        var folded = Sections(editor).Where(s => s.IsFolded).ToList();

        Assert.Single(folded);

        window.Close();
    }

    [AvaloniaFact]
    public void TheMarksFollowTheTextThatIsTyped()
    {
        var (window, editor) = Open();

        var before = Sections(editor).Count;

        editor.DocumentForTests.Text = Code + "\nSub Added()\nEnd Sub\n";

        editor.RefreshFolding();

        Assert.True(Sections(editor).Count > before);

        window.Close();
    }

    private static IReadOnlyList<FoldingSection> Sections(CodeEditor editor)
    {
        var manager = editor.FoldingForTests;

        if (manager is null) return [];

        var found = new List<FoldingSection>();

        var at = 0;

        while (manager.GetNextFolding(at) is { } section)
        {
            found.Add(section);
            at = section.StartOffset + 1;
        }

        return found;
    }
}
