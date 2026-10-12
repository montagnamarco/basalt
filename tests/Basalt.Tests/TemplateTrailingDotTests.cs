using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// A dot typed right after an expression in a template's markup. Razor reads it as
/// markup, so it may start a member ("@Model.") or end a sentence ("@Model.Name.").
/// </summary>
/// <remarks>
/// Defect reintroduced once: with the check removed from CodeEditor, the list opened
/// on "Hi @Model.Name." with its first member selected, and a space or a full stop
/// committed that member into the sentence. IsTrailingDotInMarkup is what makes the
/// editor open the list with nothing selected; without it the first two cases fail.
/// </remarks>
public sealed class TemplateTrailingDotTests
{
    [AvaloniaTheory]
    [InlineData("Index.vbhtml", "<p>Hi @Model.Name.</p>", "Hi @Model.Name.", true)]
    [InlineData("Counter.vbrazor", "<p>@Model.</p>", "@Model.", true)]
    [InlineData("Index.vbhtml", "<p>Hello.</p>", "Hello.", false)]
    [InlineData("Index.vbhtml", "@Code\n  Dim name = Model.\nEnd Code\n", "Model.", false)]
    [InlineData("Module1.vb", "<p>Hi @Model.Name.</p>", "Hi @Model.Name.", false)]
    public void TheEditorSelectsNothingOnlyAfterAnExpressionsTrailingDotInMarkup(
        string file, string text, string typed, bool unselected)
    {
        var caret = text.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        Assert.Equal(unselected, CodeEditor.IsTrailingDotInMarkup(file, text, caret));
    }

    [Fact]
    public void TheDotTypedInMarkupIsPutBackAfterTheExpressionInTheGeneratedCode()
    {
        const string template = "<p>@Model.</p>";
        const string code = "Write(Model)";
        var at = code.IndexOf("Model", StringComparison.Ordinal) + "Model".Length;

        var (completed, caret) = VbHtmlCompletionProvider.WithTrailingDot(template, template.IndexOf("</p>"), code, at);

        Assert.Equal("Write(Model.)", completed);
        Assert.Equal(at + 1, caret);
    }

    [Theory]
    [InlineData("<p>@Model.</p>", "@Model.", "Model")]
    [InlineData("<p>@Model.Items.</p>", "@Model.Items.", "Model.Items")]
    [InlineData("<p>@(Model.)</p>", "@(Model.", null)]
    [InlineData("<p>Hello.</p>", "Hello.", null)]
    public void TheExpressionBeforeATrailingDotIsTheOneTheParserRead(string text, string typed, string? expression)
    {
        var position = text.IndexOf(typed, StringComparison.Ordinal) + typed.Length;

        Assert.Equal(expression, Basalt.Razor.Vb.VbHtmlCodeRegions.ExpressionBeforeTrailingDot(text, position));
    }

    /// <remarks>
    /// The language server builds this provider without the flag: VS Code, Rider and
    /// Visual Studio select the first entry of a list, and Enter would commit a member
    /// into "Hi @Model.Name.". Reintroduced once (WithTrailingDot applied whatever the
    /// flag): the first assertion failed, the code asked about had the dot.
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyAProviderThatAsksForItPutsTheTrailingDotBack(bool completesAfterTrailingDot)
    {
        string? askedCode = null;
        var askedAt = -1;
        var provider = new VbHtmlCompletionProvider((code, at, _) =>
        {
            askedCode = code;
            askedAt = at;
            return Task.FromResult<IReadOnlyList<Basalt.Extensibility.CompletionItem>>([]);
        })
        { CompletesAfterTrailingDot = completesAfterTrailingDot };
        const string template = "@ModelType Customer\n<p>@Model.</p>";
        var position = template.IndexOf("@Model.", StringComparison.Ordinal) + "@Model.".Length;

        await provider.GetCompletionsAsync(new Basalt.Extensibility.LanguageDocument("Index.vbhtml", template), position);

        Assert.NotNull(askedCode);
        Assert.Equal(completesAfterTrailingDot, askedCode![askedAt - 1] == '.');
    }
}
