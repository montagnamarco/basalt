using Basalt.Extensibility;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>Completion for markup, offered through the language interfaces.</summary>
public class HtmlCompletionProviderTests
{
    private static async Task<IReadOnlyList<CompletionItem>> CompleteAsync(string markup)
    {
        var caret = markup.IndexOf('|');
        Assert.True(caret >= 0, "The test markup must mark the caret with |.");

        var document = new LanguageDocument("/test.html", markup.Remove(caret, 1));

        return await new HtmlCompletionProvider().GetCompletionsAsync(document, caret);
    }

    [Fact]
    public async Task OffersElementsAfterAnAngleBracket()
    {
        var items = await CompleteAsync("<|");

        Assert.Contains(items, i => i.DisplayText == "div");
        Assert.Contains(items, i => i.DisplayText == "span");
    }

    [Fact]
    public async Task NarrowsElementsToWhatWasTyped()
    {
        var items = await CompleteAsync("<inp|");

        Assert.Contains(items, i => i.DisplayText == "input");
        Assert.DoesNotContain(items, i => i.DisplayText == "div");
    }

    [Fact]
    public async Task PutsTheBestElementMatchFirst()
    {
        var items = await CompleteAsync("<di|");

        Assert.Equal("div", items[0].DisplayText);
    }

    [Fact]
    public async Task OffersTheAttributesOfTheElementBeingWritten()
    {
        var items = await CompleteAsync("<img |");

        Assert.Contains(items, i => i.DisplayText == "src");
        Assert.Contains(items, i => i.DisplayText == "alt");
    }

    [Fact]
    public async Task OffersTheValuesOfAnAttribute()
    {
        var items = await CompleteAsync("<input type=\"|");

        Assert.Contains(items, i => i.DisplayText == "checkbox");
    }

    [Fact]
    public async Task OffersNothingInRunningText()
    {
        // The whole vocabulary offered into prose would be noise.
        Assert.Empty(await CompleteAsync("<p>hello |</p>"));
    }

    [Fact]
    public async Task OffersNothingInsideAComment()
    {
        Assert.Empty(await CompleteAsync("<!-- <di|"));
    }

    [Fact]
    public async Task SaysWhichElementsHaveNoClosingTag()
    {
        var items = await CompleteAsync("<b|");

        var br = items.First(i => i.DisplayText == "br");

        Assert.Contains("without a closing tag", br.Description);
    }
}

/// <summary>Completion for stylesheets.</summary>
public class CssCompletionProviderTests
{
    private static async Task<IReadOnlyList<CompletionItem>> CompleteAsync(string css)
    {
        var caret = css.IndexOf('|');
        Assert.True(caret >= 0, "The test stylesheet must mark the caret with |.");

        var document = new LanguageDocument("/test.css", css.Remove(caret, 1));

        return await new CssCompletionProvider().GetCompletionsAsync(document, caret);
    }

    [Fact]
    public async Task OffersPropertiesInsideARule()
    {
        var items = await CompleteAsync(".button { disp|");

        Assert.Contains(items, i => i.DisplayText == "display");
    }

    [Fact]
    public async Task WritesTheColonWithTheProperty()
    {
        // Saves a keystroke, and is what every other editor does.
        var items = await CompleteAsync(".button { disp|");

        Assert.Equal("display: ", items.First(i => i.DisplayText == "display").InsertionText);
    }

    [Fact]
    public async Task OffersTheValuesOfTheProperty()
    {
        var items = await CompleteAsync(".button { display: fl|");

        Assert.Contains(items, i => i.DisplayText == "flex");
        Assert.DoesNotContain(items, i => i.DisplayText == "block");
    }

    [Fact]
    public async Task OffersAtRulesAtTheTopLevel()
    {
        var items = await CompleteAsync("@me|");

        Assert.Contains(items, i => i.DisplayText == "@media");
    }

    [Fact]
    public async Task OffersNothingInsideAComment()
    {
        Assert.Empty(await CompleteAsync("/* disp|"));
    }
}

/// <summary>Finding the tags in a document.</summary>
public class HtmlTagScannerTests
{
    [Fact]
    public void FindsOpeningAndClosingTags()
    {
        var tags = HtmlTagScanner.Scan("<div></div>").ToList();

        Assert.Equal(2, tags.Count);
        Assert.False(tags[0].Closing);
        Assert.True(tags[1].Closing);
    }

    [Fact]
    public void ReadsTheNameWithoutItsAttributes()
    {
        var tag = HtmlTagScanner.Scan("<div class=\"a b\">").Single();

        Assert.Equal("div", tag.Name);
    }

    [Fact]
    public void RecognisesASelfClosingTag()
    {
        Assert.True(HtmlTagScanner.Scan("<br />").Single().SelfClosing);
    }

    [Fact]
    public void SkipsCommentsAndDoctypes()
    {
        var tags = HtmlTagScanner.Scan("<!DOCTYPE html><!-- <div> --><p>").ToList();

        Assert.Single(tags);
        Assert.Equal("p", tags[0].Name);
    }

    [Fact]
    public void ReportsWhereEachTagIs()
    {
        var tags = HtmlTagScanner.Scan("<html>\n  <body>").ToList();

        Assert.Equal(1, tags[0].Line);
        Assert.Equal(2, tags[1].Line);
        Assert.Equal(3, tags[1].Column);
    }

    [Fact]
    public void StopsAtAnUnfinishedTag()
    {
        // Half-typed markup is the normal case, not a fault.
        var tags = HtmlTagScanner.Scan("<div><spa").ToList();

        Assert.Single(tags);
    }
}

/// <summary>Structural problems in markup.</summary>
public class HtmlDiagnosticProviderTests
{
    private static async Task<IReadOnlyList<Diagnostic>> CheckAsync(string markup) =>
        await new HtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/test.html", markup));

    [Fact]
    public async Task AcceptsWellFormedMarkup()
    {
        Assert.Empty(await CheckAsync("<div><p>hello</p></div>"));
    }

    [Fact]
    public async Task ReportsATagThatIsNeverClosed()
    {
        var diagnostics = await CheckAsync("<div><p>hello</p>");

        Assert.Contains(diagnostics, d => d.Message.Contains("'<div>' is never closed"));
    }

    [Fact]
    public async Task ReportsAMismatchedClosingTag()
    {
        var diagnostics = await CheckAsync("<div><p>hello</div></p>");

        Assert.Contains(diagnostics, d => d.Message.Contains("Expected '</p>'"));
    }

    [Fact]
    public async Task ReportsAClosingTagWithNothingOpen()
    {
        var diagnostics = await CheckAsync("</div>");

        Assert.Contains(diagnostics, d => d.Message.Contains("no matching opening tag"));
    }

    [Fact]
    public async Task AcceptsElementsThatNeverClose()
    {
        // A <br> without a closing tag is correct, not an error.
        Assert.Empty(await CheckAsync("<div><br><img src=\"a\"></div>"));
    }

    [Fact]
    public async Task AcceptsASelfClosingTag()
    {
        Assert.Empty(await CheckAsync("<div><span /></div>"));
    }

    [Fact]
    public async Task ReportsAProblemAsAWarningNotAnError()
    {
        // A Razor view is not well-formed markup until its code has run, so
        // these must not stop a build.
        var diagnostics = await CheckAsync("<div>");

        Assert.All(diagnostics, d => Assert.Equal(DiagnosticSeverity.Warning, d.Severity));
    }
}

/// <summary>Registering the web languages.</summary>
public class WebLanguageRegistrationTests
{
    [Fact]
    public void RegistersHtmlCssAndVbRazor()
    {
        var registry = new LanguageRegistry();

        WebLanguageProviders.RegisterAll(registry);

        Assert.NotNull(registry.ForFile("/a/page.html"));
        Assert.NotNull(registry.ForFile("/a/site.css"));
        Assert.NotNull(registry.ForFile("/a/view.vbhtml"));
    }

    [Fact]
    public void GivesRazorForVisualBasicTheMarkupCompletion()
    {
        // The markup half of a .vbhtml file is ordinary HTML.
        var provider = WebLanguageProviders.VbRazor;

        Assert.NotNull(provider.Completion);
        Assert.NotNull(provider.Diagnostics);
    }

    [Fact]
    public void LeavesColouringToTheGrammars()
    {
        // TextMate does this in the shell, with the grammars VS Code uses.
        Assert.Null(WebLanguageProviders.Html.Highlighting);
    }

    [Fact]
    public void MatchesExtensionsWhateverTheirCase()
    {
        var registry = new LanguageRegistry();
        WebLanguageProviders.RegisterAll(registry);

        Assert.NotNull(registry.ForFile("/a/PAGE.HTML"));
    }
}
