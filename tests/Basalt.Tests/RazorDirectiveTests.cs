using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// The directives a template can carry.
///
/// The parser knew three; Razor has a dozen that matter for server-rendered
/// views. These cover the ones a Visual Basic project actually needs, and
/// leave the Blazor ones out on purpose — Basalt renders on the server.
/// </summary>
public sealed class RazorDirectiveTests
{
    [Fact]
    public void ReadsANamespace()
    {
        var document = VbHtmlParser.Parse("@Namespace App.Custom.Views\n<p>a</p>");

        Assert.Equal("App.Custom.Views", document.Namespace);
    }

    [Fact]
    public void TheTemplatesNamespaceWinsOverTheFolders()
    {
        // A project with an unusual layout needs to say so.
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("@Namespace App.Custom.Views"), "Page", "App.Views");

        Assert.Contains("Namespace App.Custom.Views", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Namespace App.Views", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsImplements()
    {
        var document = VbHtmlParser.Parse("@Implements IDisposable\n<p>a</p>");

        Assert.Contains("IDisposable", document.Implements);
    }

    [Fact]
    public void TheGeneratedClassImplementsWhatWasAsked()
    {
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("@Implements IDisposable"), "Page", "App.Views");

        Assert.Contains("Implements IDisposable", code, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAnAttribute()
    {
        var document = VbHtmlParser.Parse("@Attribute Obsolete\n<p>a</p>");

        Assert.Contains("Obsolete", document.Attributes);
    }

    [Fact]
    public void TheAttributeLandsOnTheClass()
    {
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("@Attribute Obsolete"), "Page", "App.Views");

        var attributeAt = code.IndexOf("<Obsolete>", StringComparison.Ordinal);
        var classAt = code.IndexOf("Partial Public Class", StringComparison.Ordinal);

        Assert.True(attributeAt >= 0, $"The attribute was not written:\n{code}");
        Assert.True(attributeAt < classAt, "The attribute landed after the class.");
    }

    [Fact]
    public void AcceptsTheCSharpSpellingOfTheModelDirective()
    {
        // Someone arriving from Razor writes "@Model Customer"; a template
        // should not fail over a keyword.
        var document = VbHtmlParser.Parse("@Model Customer\n<p>a</p>");

        Assert.Equal("Customer", document.ModelType);
    }

    [Fact]
    public void TheVisualBasicSpellingStillWorks()
    {
        var document = VbHtmlParser.Parse("@ModelType Customer\n<p>a</p>");

        Assert.Equal("Customer", document.ModelType);
    }

    [Fact]
    public void UsingStillOpensABlockRatherThanImporting()
    {
        // Using means both things in Visual Basic, and the block is the more
        // common one. @Imports is the spelling that always works.
        var document = VbHtmlParser.Parse("@Using resource\n<p>a</p>\n@End Using");

        Assert.Empty(document.Diagnostics);
        Assert.Single(document.Nodes.OfType<BlockNode>());
    }

    [Fact]
    public void SeveralDirectivesTogether()
    {
        var document = VbHtmlParser.Parse("""
            @ModelType Customer
            @Imports System.Text
            @Namespace App.Views
            @Implements IDisposable
            <p>@Model.Name</p>
            """);

        Assert.Empty(document.Diagnostics);
        Assert.Equal("Customer", document.ModelType);
        Assert.Contains("System.Text", document.Imports);
        Assert.Equal("App.Views", document.Namespace);
        Assert.Contains("IDisposable", document.Implements);
    }
}
