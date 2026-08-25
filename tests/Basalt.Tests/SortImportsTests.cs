using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Putting a template's @Imports in order.
///
/// Kept out of formatting on purpose: reordering someone's lines is a change
/// they ask for, not something that happens because they pressed the format
/// key.
/// </summary>
public class SortImportsTests
{
    private static string Sort(string text) =>
        VbHtmlFormattingProvider.SortImports(text, caret: 0).Text;

    private static bool Changed(string text) =>
        VbHtmlFormattingProvider.SortImports(text, caret: 0).Changed;

    [Fact]
    public void PutsThemInAlphabeticalOrder()
    {
        var sorted = Sort("@Imports Zebra\n@Imports Alpha\n<p>x</p>");

        Assert.StartsWith("@Imports Alpha\n@Imports Zebra\n", sorted, StringComparison.Ordinal);
    }

    [Fact]
    public void PutsSystemFirst()
    {
        // The order Visual Studio and Roslyn's own organiser use.
        var sorted = Sort("@Imports Alpha\n@Imports System\n<p>x</p>");

        Assert.StartsWith("@Imports System\n@Imports Alpha\n", sorted, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheSystemFamilyTogetherAndInOrder()
    {
        var sorted = Sort(
            "@Imports Zebra\n@Imports System.Text\n@Imports System\n<p>x</p>");

        Assert.StartsWith(
            "@Imports System\n@Imports System.Text\n@Imports Zebra\n",
            sorted, StringComparison.Ordinal);
    }

    [Fact]
    public void RemovesADuplicate()
    {
        // The same import written twice is one import.
        var sorted = Sort("@Imports Alpha\n@Imports Alpha\n<p>x</p>");

        Assert.Equal("@Imports Alpha\n<p>x</p>", sorted);
    }

    [Fact]
    public void LeavesTheMarkupExactlyWhereItWas()
    {
        var sorted = Sort("@Imports Zebra\n@Imports Alpha\n<p>@name</p>\n<div>x</div>");

        Assert.EndsWith("<p>@name</p>\n<div>x</div>", sorted, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesNothingWhenTheyAreAlreadyInOrder()
    {
        const string text = "@Imports System\n@Imports Alpha\n<p>x</p>";

        Assert.False(Changed(text));
        Assert.Equal(text, Sort(text));
    }

    [Fact]
    public void ChangesNothingWithASingleImport()
    {
        const string text = "@Imports Alpha\n<p>x</p>";

        Assert.False(Changed(text));
    }

    [Fact]
    public void ChangesNothingWithNoImports()
    {
        const string text = "<p>x</p>";

        Assert.False(Changed(text));
        Assert.Equal(text, Sort(text));
    }

    [Fact]
    public void LeavesImportsWrittenFurtherDownAlone()
    {
        // Only the run at the top is touched: an import after markup stays
        // where the author put it.
        const string text = "<p>x</p>\n@Imports Zebra\n@Imports Alpha";

        Assert.False(Changed(text));
    }

    [Fact]
    public void KeepsWindowsLineBreaks()
    {
        var sorted = Sort("@Imports Zebra\r\n@Imports Alpha\r\n<p>x</p>");

        Assert.Contains("@Imports Alpha\r\n", sorted, StringComparison.Ordinal);
        Assert.DoesNotContain("@Imports Alpha\n@", sorted.Replace("\r\n", "\r\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTidyImportsCommandReachesATemplate()
    {
        // "Sort and Remove Imports" used to ask Roslyn, which does not hold a
        // .vbhtml, so it refused for the wrong reason. The command is shared;
        // only where the answer comes from differs.
        var root = Directory.CreateTempSubdirectory("basalt-sortimports-").FullName;

        try
        {
            var view = Path.Combine(root, "Index.vbhtml");

            await File.WriteAllTextAsync(view, "@Imports Zebra\n@Imports Alpha\n<p>x</p>");

            using var service = new Basalt.Workspace.RoslynLanguageService();

            var preview = await service.PreviewTidyImportsAsync(view);

            Assert.Null(preview.Problem);

            var change = Assert.Single(preview.Changes);

            Assert.StartsWith("@Imports Alpha", change.NewText, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public async Task TheCommandSaysSoWhenThereIsNothingToDo()
    {
        var root = Directory.CreateTempSubdirectory("basalt-sortimports-").FullName;

        try
        {
            var view = Path.Combine(root, "Index.vbhtml");

            await File.WriteAllTextAsync(view, "@Imports Alpha\n<p>x</p>");

            using var service = new Basalt.Workspace.RoslynLanguageService();

            var preview = await service.PreviewTidyImportsAsync(view);

            Assert.NotNull(preview.Problem);
            Assert.Contains("already", preview.Problem, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void IgnoresTheCaseOfTheKeyword()
    {
        var sorted = Sort("@imports Zebra\n@Imports Alpha\n<p>x</p>");

        Assert.StartsWith("@Imports Alpha", sorted, StringComparison.Ordinal);
    }
}
