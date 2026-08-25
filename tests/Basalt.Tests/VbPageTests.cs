using Basalt.Razor.Vb.Classic;

namespace Basalt.Tests;

/// <summary>
/// Pages written the Classic ASP way: markup with Visual Basic in it.
/// </summary>
/// <remarks>
/// A different idea from Razor rather than a worse one. A Razor view is a
/// class with a model, compiled into an application that must already exist;
/// a page here is a file that answers on its own path and reads top to
/// bottom. That is what made ASP and PHP easy to start with.
/// </remarks>
public sealed class VbPageTests
{
    private static string Generate(string source)
    {
        var page = VbPageParser.Parse(source);

        Assert.Empty(page.Diagnostics);

        return VbPageWriter.Write(page, "Index", "Pages");
    }

    [Fact]
    public void MarkupIsWrittenAsItStands()
    {
        var code = Generate("<h1>Hello</h1>");

        Assert.Contains("""WriteLiteral("<h1>Hello</h1>")""", code);
    }

    [Fact]
    public void AnExpressionIsWrittenOut()
    {
        var code = Generate("<p><%= name %></p>");

        Assert.Contains("Write(name)", code);
    }

    [Fact]
    public void StatementsRunForTheirEffect()
    {
        var code = Generate("<% Dim x = 1 %>");

        Assert.Contains("Dim x = 1", code);
        Assert.DoesNotContain("Write(Dim", code);
    }

    [Fact]
    public void ControlFlowSpansSeveralBlocks()
    {
        // The shape that makes this style what it is: a loop opened in one
        // block, markup, and the loop closed in another.
        var code = Generate("<% For Each n In names %><li><%= n %></li><% Next %>");

        Assert.Contains("For Each n In names", code);
        Assert.Contains("Write(n)", code);
        Assert.Contains("Next", code);
    }

    [Fact]
    public void RawOutputIsAskedForExplicitly()
    {
        // Classic ASP encoded nothing, and Response.Write of a query string
        // was the normal way to write a page — and a cross-site scripting
        // hole. Here it is the other way round.
        var code = Generate("<%== trusted %>");

        Assert.Contains("WriteRaw(trusted)", code);
    }

    [Fact]
    public void MembersGoOnTheClass()
    {
        var code = Generate("<%! Private Function Twice(n As Integer) As Integer\n"
                          + "        Return n * 2\n"
                          + "    End Function %>");

        Assert.Contains("Private Function Twice", code);

        // On the class, not inside the render method.
        var render = code.IndexOf("RenderAsync", StringComparison.Ordinal);
        var member = code.IndexOf("Private Function Twice", StringComparison.Ordinal);

        Assert.True(member > render, "the member landed inside the render method");
    }

    [Fact]
    public void ImportsBecomeImports()
    {
        var code = Generate("""<%@ Import Namespace="System.Linq" %><p>x</p>""");

        Assert.Contains("Imports System.Linq", code);
    }

    [Fact]
    public void TheBareImportSpellingWorksToo()
    {
        // Classic ASP wrote the namespace bare; ASP.NET wrote Namespace="…".
        // Someone porting a page should not have to guess which is wanted.
        var code = Generate("<%@ Import System.Linq %><p>x</p>");

        Assert.Contains("Imports System.Linq", code);
    }

    [Fact]
    public void OptionStrictStaysOn()
    {
        // Where this parts company with Classic ASP: a page that adds a
        // number to a string and gets away with it is a page that fails on
        // the one input nobody tried.
        var code = Generate("<p>x</p>");

        Assert.Contains("Option Strict On", code);
        Assert.Contains("Option Explicit On", code);
    }

    [Fact]
    public void AnUnclosedBlockIsReported()
    {
        var page = VbPageParser.Parse("<p><% Dim x = 1");

        Assert.Contains(page.Diagnostics, d => d.Id == "VBP001");
    }

    [Fact]
    public void AnUnclosedBlockDoesNotSpillCodeOntoThePage()
    {
        // The failure that matters: read as markup, the unfinished statement
        // is served to the browser as text.
        var page = VbPageParser.Parse("<p><% Dim secret = Config.Password");

        Assert.DoesNotContain(page.Parts.OfType<VbPageParser.Markup>(),
            m => m.Text.Contains("secret", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownDirectiveIsReported()
    {
        var page = VbPageParser.Parse("<%@ Page Language=\"VB\" %><p>x</p>");

        Assert.Contains(page.Diagnostics, d => d.Id == "VBP002");
    }

    [Theory]
    [InlineData("<%= a %>", 1)]
    [InlineData("<% a %>", 1)]
    [InlineData("<%! a %>", 0)]
    public void EachOpeningIsToldApart(string source, int expressionsAndCode)
    {
        var page = VbPageParser.Parse(source);

        Assert.Equal(expressionsAndCode,
            page.Parts.Count(p => p is VbPageParser.Expression or VbPageParser.Code));
    }
}
