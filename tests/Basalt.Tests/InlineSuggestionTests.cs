using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Basalt.Extensibility;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

/// <summary>
/// Suggestions drawn in the editor without entering the document.
/// </summary>
/// <remarks>
/// Against a provider that answers immediately, which is the reason there is
/// an interface at all: the real ones need an account and a network, and an
/// editor whose suggestion path is only exercised against those is one nobody
/// can test.
/// </remarks>
public class InlineSuggestionTests
{
    /// <summary>A provider that always offers the same thing.</summary>
    private sealed class Fixed(string text) : IInlineSuggestionProvider
    {
        public bool IsAvailable { get; set; } = true;

        public Task<InlineSuggestion?> SuggestAsync(
            string filePath, string documentText, int position, CancellationToken ct = default) =>
            Task.FromResult<InlineSuggestion?>(new InlineSuggestion(text, position));
    }

    private const string Code = """
        Module Program
            Sub Main()

            End Sub
        End Module
        """;

    private static (Window Window, CodeEditor Editor) Open(string? suggests = null)
    {
        var document = new EditorDocumentViewModel("/x/Program.vb", Code);
        var editor = new CodeEditor(document, new MainWindowViewModel());

        if (suggests is not null) editor.SuggestionProvider = new Fixed(suggests);

        var window = new Window { Content = editor, Width = 700, Height = 500 };

        window.Show();
        window.UpdateLayout();

        return (window, editor);
    }

    [Fact]
    public void ASuggestionShowsOnlyItsFirstLine()
    {
        // Drawing all of a multi-line suggestion pushes the code below it down
        // the screen and back up again on every keystroke, which is unreadable
        // however good the suggestion is.
        var suggestion = new InlineSuggestion("Console.WriteLine()\nEnd Sub", 0);

        Assert.Equal("Console.WriteLine()", suggestion.FirstLine);
    }

    [Fact]
    public void ASuggestionCanBeTakenAWordAtATime()
    {
        // A suggestion is often right at the start and wrong further along.
        var suggestion = new InlineSuggestion("Console.WriteLine(name)", 0);

        Assert.Equal("Console.WriteLine(name)", suggestion.FirstWord);

        // And leading space belongs to the first word, or accepting one takes
        // nothing at all and looks broken.
        Assert.Equal("    Dim", new InlineSuggestion("    Dim x = 1", 0).FirstWord);
    }

    [AvaloniaFact]
    public async Task TheEditorDrawsWhatIsSuggested()
    {
        var (window, editor) = Open("Console.WriteLine()");

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Co");

        await Task.Delay(600);

        Assert.True(editor.IsSuggesting, "nothing was suggested");

        // And the document does not have it: undo knows nothing about a
        // suggestion, and the file on disk does not carry one.
        Assert.DoesNotContain("WriteLine", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task AcceptingWritesItIntoTheDocument()
    {
        var (window, editor) = Open("Console.WriteLine()");

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Co");

        await Task.Delay(600);

        Assert.True(editor.AcceptSuggestion(), "there was nothing to accept");
        Assert.Contains("Console.WriteLine()", editor.TextForTests);

        // And it stops being suggested once it is real.
        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    [AvaloniaFact]
    public async Task AcceptingAWordTakesOnlyTheFirst()
    {
        var (window, editor) = Open("Dim total As Integer = 0");

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Di");

        await Task.Delay(600);

        Assert.True(editor.AcceptSuggestion(wordOnly: true));
        Assert.Contains("Dim", editor.TextForTests);
        Assert.DoesNotContain("Integer", editor.TextForTests);

        window.Close();
    }

    [AvaloniaFact]
    public async Task NoProviderMeansNoSuggestionAndNoRequest()
    {
        // An editor with nothing signed in should not pay for a round trip
        // that can only fail.
        var (window, editor) = Open();

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Co");

        await Task.Delay(600);

        Assert.False(editor.IsSuggesting);

        window.Close();
    }

    [AvaloniaFact]
    public async Task TypingOnTakesTheSuggestionAway()
    {
        // A suggestion made for what was on the line a moment ago is worse
        // than none, because it reads as an answer to what is there now.
        var (window, editor) = Open("Console.WriteLine()");

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Co");

        await Task.Delay(600);

        Assert.True(editor.IsSuggesting);

        editor.TypeForTests("n");

        Assert.False(editor.IsSuggesting, "the stale suggestion stayed on screen");

        window.Close();
    }

    [AvaloniaFact]
    public async Task TheSuggestionIsActuallyDrawn()
    {
        // Rendered and looked at. A generator that returns an element the
        // formatter refuses throws on every draw — "The returned TextRun is
        // too long" — and every assertion about the model still passes, since
        // the model is right and only the screen is wrong.
        var (window, editor) = Open("Console.WriteLine()");

        editor.CaretOffsetForTests = Code.IndexOf("\n\n", StringComparison.Ordinal) + 1;
        editor.TypeForTests("Co");

        await Task.Delay(600);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var target = new Avalonia.Media.Imaging.RenderTargetBitmap(
            new Avalonia.PixelSize(700, 500));

        // Throws if the generator produced something the formatter cannot
        // lay out, which is exactly the failure a model assertion misses.
        target.Render(window);

        var path = Path.Combine(Path.GetTempPath(), "basalt-ghost-text.png");

        using (var file = File.Create(path))
            target.Save(file, new Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        Assert.True(new FileInfo(path).Length > 1000, "the render is empty");

        window.Close();
    }
}
