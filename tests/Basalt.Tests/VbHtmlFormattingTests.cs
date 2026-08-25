using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Tidying a .vbhtml.
///
/// The provider was hard-null: a template could not be formatted at all. It
/// is deliberately narrow — the code half gets indented, the markup is left
/// exactly as written, because a formatter that rearranges someone's HTML is
/// a formatter they turn off.
/// </summary>
public sealed class VbHtmlFormattingTests
{
    private static LanguageDocument Document(string text) => new("/a/Page.vbhtml", text);

    [Fact]
    public async Task TheProviderExistsNow()
    {
        Assert.NotNull(WebLanguageProviders.VbRazor.Formatting);
    }

    [Fact]
    public async Task IndentsStatementsInsideACodeBlock()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nDim x = 1\nDim y = 2\nEnd Code"));

        Assert.True(result.Changed);
        Assert.Contains("    Dim x = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Dim y = 2", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IndentsANestedBlockOneLevelFurther()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nIf x > 0 Then\nDim y = 1\nEnd If\nEnd Code"));

        Assert.Contains("    If x > 0 Then", result.Text, StringComparison.Ordinal);
        Assert.Contains("        Dim y = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    End If", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LinesUpAClosingKeywordWithWhatItCloses()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nFor i = 1 To 3\nDim y = i\nNext\nEnd Code"));

        Assert.Contains("    For i = 1 To 3", result.Text, StringComparison.Ordinal);
        Assert.Contains("        Dim y = i", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Next", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesTheMarkupExactlyAsWritten()
    {
        // Someone laid this out on purpose.
        const string template = "<div>\n      <p>deliberately indented</p>\n</div>";

        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(template));

        Assert.False(result.Changed);
        Assert.Equal(template, result.Text);
    }

    [Fact]
    public async Task LeavesAlreadyTidyCodeAlone()
    {
        // Reporting a change where there is none makes the editor mark the
        // file dirty for nothing.
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\n    Dim x = 1\nEnd Code"));

        Assert.False(result.Changed);
    }

    [Fact]
    public async Task LeavesATemplateThatDoesNotParseAlone()
    {
        // Reformatting something half-understood is how a formatter eats
        // someone's work.
        const string broken = "@Code\n    Dim x = 1";

        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(broken));

        Assert.False(result.Changed);
        Assert.Equal(broken, result.Text);
    }

    [Fact]
    public async Task DoesNotTreatAnIdentifierAsABlockKeyword()
    {
        // "Iffy" starts with "If" and opens nothing.
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nIffy = 1\nDim y = 2\nEnd Code"));

        Assert.Contains("    Iffy = 1", result.Text, StringComparison.Ordinal);
        Assert.Contains("    Dim y = 2", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsBlankLinesWhereTheyWere()
    {
        var provider = new VbHtmlFormattingProvider();

        var result = await provider.FormatDocumentAsync(Document(
            "@Code\nDim x = 1\n\nDim y = 2\nEnd Code"));

        Assert.Contains("\n\n", result.Text, StringComparison.Ordinal);
    }
}
