using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Turning a parsed template into Visual Basic source.
///
/// The generated code has to compile, so the tests check the shape of what is
/// emitted rather than its exact formatting.
/// </summary>
public class VbHtmlCodeWriterTests
{
    private static string Generate(string template) =>
        VbHtmlCodeWriter.Write(VbHtmlParser.Parse(template), "TestView", "App.Views");

    [Fact]
    public void EmitsAClassInTheRequestedNamespace()
    {
        var code = Generate("<p>hello</p>");

        Assert.Contains("Namespace App.Views", code);
        Assert.Contains("Partial Public Class TestView", code);
        Assert.Contains($"Inherits {VbHtmlCodeWriter.BaseTypeName}", code);
    }

    [Fact]
    public void WritesLiteralMarkupAsAStringLiteral()
    {
        var code = Generate("<p>hello</p>");

        Assert.Contains("WriteLiteral(\"<p>hello</p>\")", code);
    }

    [Fact]
    public void DoublesQuotesInsideLiteralMarkup()
    {
        // Visual Basic escapes a quote by doubling it; there are no backslashes.
        var code = Generate("<a href=\"/home\">go</a>");

        Assert.Contains("\"\"/home\"\"", code);
    }

    [Fact]
    public void SplitsMultiLineMarkupOnNewlines()
    {
        // A Visual Basic literal cannot span lines, so newlines are joined in.
        var code = Generate("<p>one</p>\n<p>two</p>");

        Assert.Contains("vbCrLf", code);
        Assert.Contains("\"<p>one</p>\"", code);
        Assert.Contains("\"<p>two</p>\"", code);
    }

    [Fact]
    public void EmitsEncodedWritesForExpressions()
    {
        var code = Generate("<p>@Model.Name</p>");

        Assert.Contains("Write(Model.Name)", code);
    }

    [Fact]
    public void EmitsUnencodedWritesForHtmlRaw()
    {
        var code = Generate("<div>@Html.Raw(Model.Body)</div>");

        Assert.Contains("WriteRaw(Model.Body)", code);
    }

    [Fact]
    public void EmitsBlocksAsVisualBasicControlFlow()
    {
        var code = Generate("""
            @If Model.IsActive Then
                <p>yes</p>
            @End If
            """);

        Assert.Contains("If Model.IsActive Then", code);
        Assert.Contains("End If", code);
    }

    [Fact]
    public void EmitsContinuationClauses()
    {
        var code = Generate("""
            @If n > 5 Then
                <p>big</p>
            @Else
                <p>small</p>
            @End If
            """);

        var ifIndex = code.IndexOf("If n > 5 Then", StringComparison.Ordinal);
        var elseIndex = code.IndexOf("\n            Else", StringComparison.Ordinal);
        var endIndex = code.IndexOf("End If", StringComparison.Ordinal);

        Assert.True(ifIndex < elseIndex && elseIndex < endIndex,
            "the Else clause must sit between the If and its End If");
    }

    [Fact]
    public void EmitsForEachClosedByNext()
    {
        var code = Generate("""
            @For Each item In Model.Items
                <li>@item</li>
            @Next
            """);

        Assert.Contains("For Each item In Model.Items", code);
        Assert.Contains("Next", code);
    }

    [Fact]
    public void EmitsAModelPropertyWhenTheTemplateDeclaresOne()
    {
        var code = Generate("@ModelType App.Customer\n<p>@Model.Name</p>");

        // Rooted at the global namespace: a view generated into
        // App.Views.Home would otherwise resolve "App.Customer" against its
        // own namespace and fail to find it.
        Assert.Contains("Public Property Model As Global.App.Customer", code);
    }

    [Fact]
    public void EmitsAnUntypedModelWhenNoTypeIsDeclared()
    {
        // Razor allows @Model without @ModelType. Omitting the property would
        // leave such a template unable to compile.
        var code = Generate("<p>@Model</p>");

        Assert.Contains("Public Property Model As Object", code);
    }

    [Fact]
    public void EmitsImportsFromDirectives()
    {
        var code = Generate("@Imports System.Linq\n<p>x</p>");

        Assert.Contains("Imports System.Linq", code);
    }

    [Fact]
    public void EmitsStatementsFromCodeBlocks()
    {
        var code = Generate("""
            @Code
                Dim total = 1 + 2
            End Code
            <p>@total</p>
            """);

        Assert.Contains("Dim total = 1 + 2", code);
        Assert.Contains("Write(total)", code);
    }

    [Fact]
    public void EmitsNestedBlocksInOrder()
    {
        var code = Generate("""
            @For Each item In items
                @If item.Visible Then
                    <li>@item.Name</li>
                @End If
            @Next
            """);

        var forIndex = code.IndexOf("For Each item In items", StringComparison.Ordinal);
        var ifIndex = code.IndexOf("If item.Visible Then", StringComparison.Ordinal);
        var endIfIndex = code.IndexOf("End If", StringComparison.Ordinal);
        var nextIndex = code.IndexOf("\n            Next", StringComparison.Ordinal);

        Assert.True(forIndex < ifIndex && ifIndex < endIfIndex && endIfIndex < nextIndex,
            "the inner If must be fully contained in the For Each");
    }
}
