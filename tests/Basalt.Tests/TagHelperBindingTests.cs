using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Which elements the tag helper engine takes, and how it reads them.
/// </summary>
/// <remarks>
/// Against a catalog written here, so each rule is tested alone; the HTTP
/// tests run the framework's own tag helpers end to end.
/// </remarks>
public sealed class TagHelperBindingTests
{
    private static readonly TagHelperCatalog Catalog = new(
    [
        new TagHelperDescriptor(
            "Global.Test.InputTagHelper", "Test",
            [new TagHelperRule("input", ["asp-for"])],
            [new TagHelperProperty("asp-for", "For", "Global.X", TagHelperPropertyKind.ModelExpression)]),
        new TagHelperDescriptor(
            "Global.Test.PanelTagHelper", "Test",
            [new TagHelperRule("div", ["panel"])],
            [new TagHelperProperty("panel", "Title", "Global.System.String", TagHelperPropertyKind.String)]),
    ]);

    private const string AddAll = "@addTagHelper *, Test\n";

    private static string Generate(string template) =>
        VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "View", "Generated", null,
            ViewHost.AspNetCore, tagHelpers: Catalog).Code;

    private static List<TagHelperElementNode> Bound(string template)
    {
        var document = VbHtmlParser.Parse(template);

        TagHelperBinder.Bind(document, Catalog.Scoped(document.TagHelperDirectives));

        return TagHelperBinder.ElementsIn(document.Nodes).ToList();
    }

    [Fact]
    public void AnElementNoTagHelperMatchesIsLeftAlone()
    {
        Assert.Empty(Bound(AddAll + "<p class=\"x\">hi</p>\n<input name=\"a\" />\n"));
    }

    [Fact]
    public void NothingIsBoundWithoutAnAddTagHelper()
    {
        // As in C#: the tag helpers of an assembly apply only once added.
        Assert.Empty(Bound("<input asp-for=\"Name\" />\n"));
    }

    [Fact]
    public void RemoveTagHelperTakesItAwayAgain()
    {
        Assert.Empty(Bound(AddAll + "@removeTagHelper Test.InputTagHelper, Test\n<input asp-for=\"Name\" />\n"));
    }

    [Fact]
    public void AnAttributeWithAnExpressionInItIsOneValue()
    {
        // The parser cuts the markup at "@b"; the binder puts the tag back
        // together, the expression one part of the class value.
        var element = Assert.Single(Bound(AddAll + "<input asp-for=\"Name\" class=\"a @b\" />\n"));

        var css = Assert.Single(element.Attributes, a => a.Name == "class");

        Assert.False(css.IsLiteral);
        Assert.Equal(TagHelperElementMode.SelfClosing, element.Mode);

        var code = Generate(AddAll + "<input asp-for=\"Name\" class=\"a @b\" />\n");

        Assert.Contains("TagHelperValues.Html(New Global.Microsoft.AspNetCore.Html.HtmlString(\"a \"), (b))", code);
    }

    [Fact]
    public void AnExclamationMarkOptsAnElementOut()
    {
        var code = Generate(AddAll + "<!input asp-for=\"Name\" />\n");

        Assert.DoesNotContain("CreateTagHelper", code);
        Assert.Contains("input asp-for=", code.Replace("\"\"", "\""));
        Assert.DoesNotContain("<!input", code);
    }

    [Fact]
    public void ADoctypeKeepsItsExclamationMark()
    {
        var code = Generate(AddAll + "<!DOCTYPE html>\n<input asp-for=\"Name\" />\n");

        Assert.Contains("<!DOCTYPE html>", code);
    }

    [Fact]
    public void WithAPrefixOnlyPrefixedElementsAreTaken()
    {
        var bound = Bound(AddAll + "@tagHelperPrefix th:\n<input asp-for=\"A\" />\n<th:input asp-for=\"B\" />\n");

        var element = Assert.Single(bound);

        Assert.Equal("input", element.Name);
        Assert.Equal("B", element.Attributes[0].Text);
    }

    [Fact]
    public void NestedElementsOfTheSameNameCloseWhereTheyShould()
    {
        var element = Assert.Single(Bound(AddAll + "<div panel=\"t\"><div>inner</div>after</div>\n<p>outside</p>\n"));

        var content = string.Concat(element.Children.OfType<HtmlNode>().Select(h => h.Text));

        Assert.Equal("<div>inner</div>after", content);
    }

    [Fact]
    public void AnElementInsideABlockIsTaken()
    {
        Assert.Single(Bound(AddAll + "@If ok Then\n    <input asp-for=\"Name\" />\nEnd If\n"));
    }

    [Fact]
    public void AModelExpressionBecomesALambdaOnTheModel()
    {
        var code = Generate(AddAll + "<input asp-for=\"Address.Lines[0]\" />\n");

        Assert.Contains(
            "ModelExpressionProvider.CreateModelExpression(ViewData, Function(__model) __model.Address.Lines(0))",
            code);
    }

    [Fact]
    public void AModelExpressionWrittenAsAnExpressionIsTheLambdasBody()
    {
        // As in C#: asp-for="@item.Name" is item.Name, not __model.item.Name.
        var code = Generate(AddAll + "<input asp-for=\"@item.Name\" />\n");

        Assert.Contains("Function(__model) item.Name)", code);
    }

    [Fact]
    public void ElementIdsAreUniqueAcrossViewsNotOnlyWithinOne()
    {
        // CacheTagHelper keys on the id: two views whose <cache> sat at the
        // same offset served each other's HTML.
        var template = AddAll + "<input asp-for=\"Name\" />\n";

        var first = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "First", "Views", null, ViewHost.AspNetCore, tagHelpers: Catalog).Code;
        var second = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "Second", "Views", null, ViewHost.AspNetCore, tagHelpers: Catalog).Code;

        string Id(string code) => System.Text.RegularExpressions.Regex
            .Match(code, "TagMode\\.SelfClosing, \"([^\"]+)\"").Groups[1].Value;

        Assert.NotEqual(Id(first), Id(second));
    }

    [Fact]
    public void ACaretInAnAspForValueReachesThePropertyNotTheField()
    {
        // The mapping used to start at the beginning of the generated line,
        // so hover on "Customer" was asked about the tag helper's field.
        var template = AddAll + "<input asp-for=\"Customer\" />\n";

        var generated = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "View", "Views", "/Views/View.vbhtml",
            ViewHost.AspNetCore, tagHelpers: Catalog);

        var caret = template.IndexOf("Customer", StringComparison.Ordinal);
        var at = generated.Map.ToGenerated(caret);

        Assert.NotNull(at);
        Assert.StartsWith("Customer)", generated.Code[at.Value..], StringComparison.Ordinal);
    }

    [Fact]
    public void TheClosingTagOfAnOptedOutElementLosesItsMarkToo()
    {
        var code = Generate(AddAll + "<!div panel=\"t\">x</!div>\n");

        Assert.Contains("x</div>", code);
        Assert.DoesNotContain("</!div>", code);
    }

    [Fact]
    public void AnElementWhoseEndTagIsMissingIsLeftAlone()
    {
        Assert.Empty(Bound(AddAll + "<div panel=\"t\">never closed\n"));
    }
}
