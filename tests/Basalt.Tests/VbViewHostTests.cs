using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The two shapes a view is generated in.
/// </summary>
/// <remarks>
/// A view for ASP.NET Core must inherit RazorPage(Of TModel) and override the
/// async ExecuteAsync; the standalone runtime wants its own base and a plain
/// Execute. Generating the wrong one fails to compile, and it failed against
/// the framework's own base class, which reads as a broken install rather
/// than as a wrong choice here.
/// </remarks>
public sealed class VbViewHostTests
{
    private static string Generate(string template, ViewHost host)
    {
        var document = VbHtmlParser.Parse(template);
        Assert.Empty(document.Diagnostics);

        return VbHtmlCodeWriter.Write(document, "Index", "Web.Views.Home", host);
    }

    [Fact]
    public void AspNetCoreViewsInheritRazorPage()
    {
        var code = Generate("<p>hello</p>", ViewHost.AspNetCore);

        Assert.Contains("Inherits Microsoft.AspNetCore.Mvc.Razor.RazorPage(Of Object)", code);
        Assert.Contains("Public Overrides Async Function ExecuteAsync()", code);
    }

    [Fact]
    public void StandaloneViewsKeepTheirOwnBase()
    {
        var code = Generate("<p>hello</p>", ViewHost.Standalone);

        Assert.Contains(VbHtmlCodeWriter.BaseTypeName, code);
        Assert.Contains("Public Overrides Sub Execute()", code);
    }

    [Fact]
    public void TheModelTypeReachesTheBaseClass()
    {
        var code = Generate("@ModelType Web.Models.Customer\n<p>@Model.Name</p>",
            ViewHost.AspNetCore);

        Assert.Contains("RazorPage(Of Global.Web.Models.Customer)", code);
    }

    [Fact]
    public void AspNetCoreViewsDoNotDeclareTheirOwnModel()
    {
        // The base class carries Model. Declaring it again shadows the one
        // the framework populates, and every view sees Nothing.
        var code = Generate("@ModelType Web.Models.Customer\n<p>@Model.Name</p>",
            ViewHost.AspNetCore);

        Assert.DoesNotContain("Public Property Model", code);
    }

    [Fact]
    public void StandaloneViewsDoDeclareTheirModel()
    {
        var code = Generate("@ModelType Web.Models.Customer\n<p>@Model.Name</p>",
            ViewHost.Standalone);

        Assert.Contains("Public Property Model As Global.Web.Models.Customer", code);
    }

    [Theory]
    [InlineData("Object")]
    [InlineData("String")]
    public void UnqualifiedTypesAreLeftAlone(string typeName)
    {
        // Nothing to root: a keyword or a one-word type resolves as written,
        // and prefixing Global. to Object would not compile.
        var code = Generate($"@ModelType {typeName}\n<p>ok</p>", ViewHost.AspNetCore);

        Assert.Contains($"RazorPage(Of {typeName})", code);
    }

    [Fact]
    public void AnAlreadyRootedTypeIsNotRootedTwice()
    {
        var code = Generate("@ModelType Global.Web.Models.Customer\n<p>ok</p>",
            ViewHost.AspNetCore);

        Assert.Contains("RazorPage(Of Global.Web.Models.Customer)", code);
        Assert.DoesNotContain("Global.Global.", code);
    }
}
