using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

/// <summary>
/// Moving a run of markup into a partial view.
///
/// By hand it is three steps — create the file, move the lines, write the
/// call — and the middle one silently leaves the original behind.
/// </summary>
public sealed class ExtractPartialTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-extractpartial-").FullName;

    private string ViewPath => Path.Combine(_root, "Index.vbhtml");

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private RefactoringPreview Extract(string text, string select, string name = "Row")
    {
        var start = text.IndexOf(select, StringComparison.Ordinal);

        Assert.True(start >= 0, $"'{select}' is not in the template.");

        return new ExtractPartialRefactoring()
            .Preview(ViewPath, text, start, select.Length, name);
    }

    [Fact]
    public void WritesThePartialAndCallsIt()
    {
        const string text = "<div>\n  <p>hello</p>\n</div>";

        var preview = Extract(text, "<p>hello</p>");

        Assert.Null(preview.Problem);
        Assert.Equal(2, preview.Changes.Count);

        var partial = preview.Changes[0];

        Assert.EndsWith("_Row.vbhtml", partial.FilePath, StringComparison.Ordinal);
        Assert.Contains("<p>hello</p>", partial.NewText, StringComparison.Ordinal);

        // And the page calls it where the markup was.
        var page = preview.Changes[1];

        Assert.Contains(@"@Html.Partial(""_Row"")", page.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>hello</p>", page.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public void GivesThePartialTheLeadingUnderscoreItself()
    {
        // The convention for a partial. Asking the user to remember it is
        // asking them to get it wrong.
        var preview = Extract("<p>x</p>", "<p>x</p>", name: "Row");

        Assert.EndsWith("_Row.vbhtml", preview.Changes[0].FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotDoubleAnUnderscoreTheUserTyped()
    {
        var preview = Extract("<p>x</p>", "<p>x</p>", name: "_Row");

        Assert.EndsWith("_Row.vbhtml", preview.Changes[0].FilePath, StringComparison.Ordinal);
        Assert.DoesNotContain("__Row", preview.Changes[0].FilePath, StringComparison.Ordinal);
    }

    [Fact]
    public void StripsTheIndentationTheMarkupHadInThePage()
    {
        // It is a file of its own now; keeping the old indentation would push
        // every line across for no reason.
        const string text = "<div>\n    <p>a</p>\n    <p>b</p>\n</div>";

        var preview = Extract(text, "<p>a</p>\n    <p>b</p>");

        Assert.StartsWith("<p>a</p>", preview.Changes[0].NewText, StringComparison.Ordinal);
        Assert.Contains("\n<p>b</p>", preview.Changes[0].NewText.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesWhenTheSelectionReadsAPageVariable()
    {
        // A partial is a separate view: a Dim in the page is not in scope
        // inside it, and the break would only appear at build time.
        const string text = "@Code\n    Dim name = \"x\"\nEnd Code\n<p>@name</p>";

        var preview = Extract(text, "<p>@name</p>");

        Assert.NotNull(preview.Problem);
        Assert.Contains("code from this view", preview.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AllowsASelectionThatOnlyReadsTheModel()
    {
        // The model travels: a partial is rendered with one.
        const string text = "<p>@Model.Title</p>";

        var preview = Extract(text, "<p>@Model.Title</p>");

        Assert.Null(preview.Problem);
    }

    [Fact]
    public void RefusesToMoveACodeBlock()
    {
        const string text = "<div>\n@Code\n    Dim a = 1\nEnd Code\n</div>";

        var preview = Extract(text, "@Code\n    Dim a = 1\nEnd Code");

        Assert.NotNull(preview.Problem);
    }

    [Fact]
    public void RefusesAnEmptySelection()
    {
        var preview = new ExtractPartialRefactoring()
            .Preview(ViewPath, "<p>x</p>", start: 0, length: 0, name: "Row");

        Assert.NotNull(preview.Problem);
    }

    [Fact]
    public void RefusesASelectionOfOnlyWhitespace()
    {
        var preview = Extract("<p>x</p>   \n<div/>", "   \n");

        Assert.NotNull(preview.Problem);
        Assert.Contains("whitespace", preview.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefusesANameThatCannotBecomeAFile()
    {
        var preview = Extract("<p>x</p>", "<p>x</p>", name: "my view!");

        Assert.NotNull(preview.Problem);
    }

    [Fact]
    public async Task ApplyingItReallyCreatesTheFile()
    {
        // The preview describes a file that does not exist yet. Writing a
        // change to a missing path has to create it, or the call the page now
        // makes would reach for nothing.
        const string text = "<div>\n  <p>hello</p>\n</div>";

        await File.WriteAllTextAsync(ViewPath, text);

        var preview = Extract(text, "<p>hello</p>");

        Assert.True(await Basalt.Workspace.RoslynLanguageService.ApplyAsync(preview));

        var partial = Path.Combine(_root, "_Row.vbhtml");

        Assert.True(File.Exists(partial), $"{partial} was not created.");
        Assert.Contains("<p>hello</p>", await File.ReadAllTextAsync(partial),
            StringComparison.Ordinal);

        // And the page now calls it.
        Assert.Contains(@"@Html.Partial(""_Row"")", await File.ReadAllTextAsync(ViewPath),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesWhenThePartialAlreadyExists()
    {
        // Overwriting someone's file is never the intent.
        File.WriteAllText(Path.Combine(_root, "_Row.vbhtml"), "<p>already here</p>");

        var preview = Extract("<p>x</p>", "<p>x</p>");

        Assert.NotNull(preview.Problem);
        Assert.Contains("already exists", preview.Problem, StringComparison.Ordinal);
    }
}
