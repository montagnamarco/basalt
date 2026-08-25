using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The directives that make a template a Razor Page or give it services.
/// </summary>
public sealed class VbHtmlDirectiveTests
{
    private static VbHtmlDocument Parse(string template)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return document;
    }

    [Fact]
    public void InjectAcceptsRazorsOrder()
    {
        // "@Inject IClock Clock" is how a C# view is written, and a view
        // translated line for line must keep working.
        var service = Assert.Single(Parse("@Inject IClock Clock\n<p>x</p>").Injected);

        Assert.Equal("IClock", service.Type);
        Assert.Equal("Clock", service.Name);
    }

    [Fact]
    public void InjectAcceptsVisualBasicsOrder()
    {
        var service = Assert.Single(Parse("@Inject Clock As IClock\n<p>x</p>").Injected);

        Assert.Equal("IClock", service.Type);
        Assert.Equal("Clock", service.Name);
    }

    [Fact]
    public void InjectWithOnlyAName()
    {
        // Reported rather than guessed at: a service with no type would
        // generate a property that does not compile, against a line the user
        // did write.
        var document = VbHtmlParser.Parse("@Inject Clock\n<p>x</p>");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH010");
    }

    [Fact]
    public void AnInjectedServiceBecomesAProperty()
    {
        var code = VbHtmlCodeWriter.Write(
            Parse("@Inject App.IClock Clock\n<p>@Clock.Now</p>"),
            "View", "Generated", ViewHost.AspNetCore);

        Assert.Contains("RazorInject", code);
        Assert.Contains("Public Property Clock As Global.App.IClock", code);
    }

    [Fact]
    public void ABarePageDirectiveMarksARazorPage()
    {
        // Empty, not null: the page routes on its own path.
        Assert.Equal("", Parse("@Page\n<h1>x</h1>").PageRoute);
    }

    [Fact]
    public void AViewWithoutPageIsNotARazorPage()
    {
        Assert.Null(Parse("<h1>x</h1>").PageRoute);
    }

    [Fact]
    public void APageCarriesItsRouteTemplate()
    {
        Assert.Equal("{id:int}", Parse("@Page \"{id:int}\"\n<h1>x</h1>").PageRoute);
    }

    [Fact]
    public void ARazorPageInheritsTheRightBase()
    {
        // RazorPages.Page, not MVC's RazorPage(Of T): with the wrong one the
        // page compiles and is never routed to.
        var code = VbHtmlCodeWriter.Write(
            Parse("@Page\n<h1>x</h1>"), "Index", "Pages", ViewHost.RazorPage);

        Assert.Contains("Inherits Microsoft.AspNetCore.Mvc.RazorPages.Page", code);
        Assert.DoesNotContain("RazorPage(Of", code);
    }

    [Fact]
    public void APageWithoutAModelGetsAConcreteOne()
    {
        // PageModel is abstract, and the container reports that at the first
        // request rather than at startup.
        var code = VbHtmlCodeWriter.Write(
            Parse("@Page\n<h1>x</h1>"), "Index", "Pages", ViewHost.RazorPage);

        Assert.Contains("Public Class IndexGeneratedModel", code);
        Assert.Contains("Inherits Global.Microsoft.AspNetCore.Mvc.RazorPages.PageModel", code);
    }

    [Fact]
    public void APageDeclaringAModelUsesIt()
    {
        var code = VbHtmlCodeWriter.Write(
            Parse("@Page\n@ModelType App.IndexModel\n<h1>x</h1>"),
            "Index", "Pages", ViewHost.RazorPage);

        Assert.Contains("Public ReadOnly Property Model As Global.App.IndexModel", code);
        Assert.DoesNotContain("GeneratedModel", code);
    }
}
