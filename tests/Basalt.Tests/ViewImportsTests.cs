using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The files a folder shares with every view inside it.
///
/// They exist so a project does not repeat the same Imports at the top of
/// forty views. The rule that matters is which side wins: a view that
/// declares its own model means it, and a shared file cannot know better.
/// </summary>
public sealed class ViewImportsTests
{
    [Fact]
    public void RecognisesTheSharedFiles()
    {
        // They are not views: generating them as classes would produce a page
        // nobody asked for.
        Assert.True(ViewImports.IsShared("/a/Views/_ViewImports.vbhtml"));
        Assert.True(ViewImports.IsShared("/a/Views/_ViewStart.vbhtml"));
        Assert.False(ViewImports.IsShared("/a/Views/Index.vbhtml"));
    }

    [Fact]
    public void RecognisesThemWhateverTheCase()
    {
        Assert.True(ViewImports.IsShared("/a/Views/_viewimports.vbhtml"));
    }

    [Fact]
    public void AddsTheSharedImportsToAView()
    {
        var view = VbHtmlParser.Parse("<p>a</p>");
        var shared = VbHtmlParser.Parse("@Imports System.Text\n@Imports System.Linq");

        ViewImports.ApplyTo(view, shared);

        Assert.Contains("System.Text", view.Imports);
        Assert.Contains("System.Linq", view.Imports);
    }

    [Fact]
    public void DoesNotRepeatAnImportTheViewAlreadyHas()
    {
        var view = VbHtmlParser.Parse("@Imports System.Text");
        var shared = VbHtmlParser.Parse("@Imports System.Text");

        ViewImports.ApplyTo(view, shared);

        Assert.Single(view.Imports);
    }

    [Fact]
    public void TheViewsOwnModelTypeWins()
    {
        // A template that declares its model means it.
        var view = VbHtmlParser.Parse("@ModelType Customer");
        var shared = VbHtmlParser.Parse("@ModelType Object");

        ViewImports.ApplyTo(view, shared);

        Assert.Equal("Customer", view.ModelType);
    }

    [Fact]
    public void TheSharedModelTypeAppliesWhereTheViewDeclaresNone()
    {
        var view = VbHtmlParser.Parse("<p>a</p>");
        var shared = VbHtmlParser.Parse("@ModelType Customer");

        ViewImports.ApplyTo(view, shared);

        Assert.Equal("Customer", view.ModelType);
    }

    [Fact]
    public void TheViewsOwnBaseClassWins()
    {
        var view = VbHtmlParser.Parse("@Inherits App.MyBase");
        var shared = VbHtmlParser.Parse("@Inherits App.OtherBase");

        ViewImports.ApplyTo(view, shared);

        Assert.Equal("App.MyBase", view.Inherits);
    }

    [Fact]
    public void ReadsTheLayoutAViewStartSets()
    {
        var viewStart = VbHtmlParser.Parse("""
            @Code
                Layout = "_Layout.vbhtml"
            End Code
            """);

        Assert.Equal("_Layout.vbhtml", ViewImports.LayoutFrom(viewStart));
    }

    [Fact]
    public void SaysNothingWhenNoLayoutIsSet()
    {
        var viewStart = VbHtmlParser.Parse("@Code\n    Dim x = 1\nEnd Code");

        Assert.Null(ViewImports.LayoutFrom(viewStart));
    }

    [Fact]
    public void SaysNothingForAnEmptyViewStart()
    {
        Assert.Null(ViewImports.LayoutFrom(VbHtmlParser.Parse("")));
    }

    [Fact]
    public void TheGeneratedViewSetsTheLayoutTheViewStartAsked()
    {
        var document = VbHtmlParser.Parse("<p>a</p>");

        document.DefaultLayout = "_Layout.vbhtml";

        var code = VbHtmlCodeWriter.Write(document, "Page", "App.Views");

        Assert.Contains("Layout = \"_Layout.vbhtml\"", code, StringComparison.Ordinal);
    }

    [Fact]
    public void AViewWithNoDefaultLayoutSetsNone()
    {
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("<p>a</p>"), "Page", "App.Views");

        Assert.DoesNotContain("Layout =", code, StringComparison.Ordinal);
    }
}
