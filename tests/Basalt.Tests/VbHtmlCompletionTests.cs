using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Completion inside a .vbhtml.
///
/// The template used to offer three hard-coded members of System.Object
/// whatever the model was. Now the code half is generated to Visual Basic and
/// the language service is asked, which is how Razor itself works.
/// </summary>
public sealed class VbHtmlCompletionTests
{
    private static LanguageDocument Document(string text) =>
        new("/a/Views/Page.vbhtml", text);

    /// <summary>Where a fragment sits in the text.</summary>
    private static int At(string text, string fragment) =>
        text.IndexOf(fragment, StringComparison.Ordinal) + fragment.Length;

    // Which half the caret is in

    [Fact]
    public void MarkupIsNotCode()
    {
        const string template = "<p>hello</p>";

        Assert.False(VbHtmlCompletionProvider.IsInCode(template, At(template, "hel")));
    }

    [Fact]
    public void AnExpressionIsCode()
    {
        const string template = "<p>@Model.Name</p>";

        Assert.True(VbHtmlCompletionProvider.IsInCode(template, At(template, "@Model.")));
    }

    [Fact]
    public void AStatementInACodeBlockIsCode()
    {
        const string template = "@Code\n    Dim x = 1\nEnd Code";

        Assert.True(VbHtmlCompletionProvider.IsInCode(template, At(template, "Dim x")));
    }

    [Fact]
    public void MarkupInsideABlockIsStillMarkup()
    {
        // The body of an If is HTML, and asking Visual Basic about a tag name
        // would offer nonsense.
        const string template = "@If x Then\n<p>inside</p>\n@End If";

        Assert.False(VbHtmlCompletionProvider.IsInCode(template, At(template, "<p>ins")));
    }

    [Fact]
    public void ABlocksOpeningClauseIsCode()
    {
        const string template = "@If x > 0 Then\n<p>a</p>\n@End If";

        Assert.True(VbHtmlCompletionProvider.IsInCode(template, At(template, "@If x")));
    }

    [Fact]
    public void AnAtSignInsideAStringDoesNotMakeMarkupCode()
    {
        // A scan counting at signs would get this wrong; the parse tree does
        // not.
        const string template = "<p>write to info@example.com</p>";

        Assert.False(VbHtmlCompletionProvider.IsInCode(template, At(template, "example")));
    }

    // What each half answers

    [Fact]
    public async Task TheMarkupHalfGetsHtmlCompletion()
    {
        var provider = new VbHtmlCompletionProvider();

        var items = await provider.GetCompletionsAsync(Document("<"), 1);

        Assert.NotEmpty(items);
        Assert.Contains(items, i => i.DisplayText == "div");
    }

    [Fact]
    public async Task WithoutALanguageServiceTheCodeHalfOffersNothing()
    {
        // Rather than inventing members: a template edited outside a solution
        // has no compilation behind it, and a guess would be worse than
        // silence.
        var provider = new VbHtmlCompletionProvider();

        const string template = "<p>@Model.</p>";

        var items = await provider.GetCompletionsAsync(
            Document(template), At(template, "@Model."));

        Assert.Empty(items);
    }

    [Fact]
    public async Task TheCodeHalfAsksTheLanguageService()
    {
        // What the compiler knows, rather than three members of System.Object.
        string? askedCode = null;
        var askedAt = -1;

        var provider = new VbHtmlCompletionProvider((code, at, _) =>
        {
            askedCode = code;
            askedAt = at;

            return Task.FromResult<IReadOnlyList<CompletionItem>>(
            [
                new CompletionItem("Name", "Name", SymbolKind.Property)
            ]);
        });

        const string template = "@ModelType Customer\n<p>@Model.</p>";

        var items = await provider.GetCompletionsAsync(
            Document(template), At(template, "@Model."));

        Assert.Contains(items, i => i.DisplayText == "Name");

        // It was asked about generated Visual Basic, at a real position.
        Assert.NotNull(askedCode);
        Assert.Contains("Public Property Model As Customer", askedCode,
            StringComparison.Ordinal);
        Assert.True(askedAt > 0, "The caret was not carried across.");
    }

    [Fact]
    public async Task ThePositionItAsksAboutIsInsideTheGeneratedCode()
    {
        var asked = -1;
        string? code = null;

        var provider = new VbHtmlCompletionProvider((generated, at, _) =>
        {
            code = generated;
            asked = at;

            return Task.FromResult<IReadOnlyList<CompletionItem>>([]);
        });

        const string template = "<p>@Model.Name</p>";

        await provider.GetCompletionsAsync(Document(template), At(template, "@Model."));

        Assert.NotNull(code);
        Assert.InRange(asked, 0, code.Length);
    }
}
