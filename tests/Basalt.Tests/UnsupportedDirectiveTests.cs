using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Directives Basalt does not have.
///
/// About twenty Razor constructs used to be written into the page as
/// expressions, so a template author could not tell an unsupported feature
/// from a typo. Now each says what it is and, where there is one, what to use
/// instead.
/// </summary>
public sealed class UnsupportedDirectiveTests
{
    private static VbHtmlDiagnostic? Problem(string template) =>
        VbHtmlParser.Parse(template).Diagnostics.FirstOrDefault(d => d.Id == "VBH008");

    [Fact]
    public void PageIsSupportedNow()
    {
        // It used to be refused. Kept as a test rather than deleted: the two
        // directives are the ones people ask about, and a regression would
        // otherwise show up as a silent 404 in someone's application.
        Assert.Null(Problem("@page \"/customers\"\n<p>a</p>"));
    }

    [Fact]
    public void InjectIsSupportedNow()
    {
        Assert.Null(Problem("@inject IService Service\n<p>a</p>"));
    }

    [Fact]
    public void PointsHelperAtFunctions()
    {
        // ASP.NET Core never carried @helper forward either.
        var problem = Problem("@helper Row(x)\n<p>a</p>");

        Assert.NotNull(problem);
        Assert.Contains("@Functions", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PointsLayoutAtTheWayThatWorks()
    {
        var problem = Problem("@layout MainLayout\n<p>a</p>");

        Assert.NotNull(problem);
        Assert.Contains("_ViewStart", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysBlazorDirectivesBelongToBlazor()
    {
        // A template copied from a component will contain them.
        var problem = Problem("@rendermode InteractiveServer\n<p>a</p>");

        Assert.NotNull(problem);
        Assert.Contains("Blazor", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysSoAboutTagHelpers()
    {
        Assert.NotNull(Problem("@addTagHelper *, MyApp\n<p>a</p>"));
    }

    [Fact]
    public void TheUnsupportedDirectiveDoesNotReachThePage()
    {
        // It used to be written out as an expression.
        var document = VbHtmlParser.Parse("@page \"/x\"\n<p>after</p>");

        Assert.DoesNotContain(document.Nodes.OfType<ExpressionNode>(),
            e => e.Expression.Contains("page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheMarkupAfterItIsStillRead()
    {
        var document = VbHtmlParser.Parse("@page \"/x\"\n<p>after</p>");

        Assert.Contains(document.Nodes.OfType<HtmlNode>(),
            h => h.Text.Contains("after", StringComparison.Ordinal));
    }

    // What must not be mistaken for a directive

    [Fact]
    public void AnOrdinaryWordInProseIsNotADirective()
    {
        // "page", "layout" and "helper" are ordinary English, and a template
        // is mostly prose.
        Assert.Null(Problem("<p>Go to the next page of results.</p>"));
        Assert.Null(Problem("<p>The layout is fixed.</p>"));
        Assert.Null(Problem("<p>Ask a helper for assistance.</p>"));
    }

    [Fact]
    public void AnExpressionNamedLikeADirectiveStillWorks()
    {
        // "@page" is a directive; "@pageCount" is a variable.
        var document = VbHtmlParser.Parse("<p>@pageCount items</p>");

        Assert.Empty(document.Diagnostics);
        Assert.Contains(document.Nodes.OfType<ExpressionNode>(),
            e => e.Expression == "pageCount");
    }

    [Fact]
    public void TheDirectivesBasaltDoesHaveAreUnaffected()
    {
        var document = VbHtmlParser.Parse("""
            @ModelType Customer
            @Imports System.Text
            <p>@Model.Name</p>
            """);

        Assert.Empty(document.Diagnostics);
    }
}
