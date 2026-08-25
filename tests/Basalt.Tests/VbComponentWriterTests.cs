using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Templates compiled into Blazor components.
/// </summary>
/// <remarks>
/// A component does not write HTML out as text: it builds a render tree that
/// Blazor diffs against the previous one. Everything here is about producing
/// that tree, because a component that wrote markup as text would compile,
/// render nothing, and say nothing about why.
/// </remarks>
public class VbComponentWriterTests
{
    private static string Write(string template, string? route = null) =>
        VbComponentWriter.Write(VbHtmlParser.Parse(template), "C", "N", route);

    [Fact]
    public void BuildsATreeRatherThanWritingMarkup()
    {
        var written = Write("<h1>Ciao</h1>");

        Assert.Contains("OpenElement(0, \"h1\")", written);
        Assert.Contains("CloseElement()", written);

        // The base class and the method Blazor calls.
        Assert.Contains("Inherits Global.Microsoft.AspNetCore.Components.ComponentBase", written);
        Assert.Contains("Protected Overrides Sub BuildRenderTree", written);
    }

    [Fact]
    public void ClosesAVoidElementItself()
    {
        // A void element has no closing tag to find, so without this the
        // builder is left with an element open and everything after it nests
        // inside a <br> — the tree stays unbalanced to the end.
        var written = Write("<br /><p>dopo</p>");

        var open = written.Split("OpenElement").Length - 1;
        var close = written.Split("CloseElement").Length - 1;

        Assert.Equal(open, close);
    }

    [Fact]
    public void WritesLiteralMarkupWithoutEncodingIt()
    {
        // AddContent encodes what it is given, so the line breaks between
        // elements came out as &#xA; in the page.
        var written = Write("<p>a</p>\n<p>b</p>");

        Assert.Contains("AddMarkupContent", written);
    }

    [Fact]
    public void BreaksALiteralAcrossLinesIntoVbLf()
    {
        // Visual Basic has no multi-line string, so markup spanning lines —
        // which is all markup — produced a literal broken across lines and a
        // file that did not compile.
        var written = Write("<p>a</p>\n<p>b</p>");

        Assert.DoesNotContain("\"\n", written);
        Assert.Contains("vbLf", written);
    }

    [Fact]
    public void RoutesAComponentThatAsksFor()
    {
        Assert.Contains("Route(\"/ciao\")", Write("<p>a</p>", "/ciao"));

        // And a component without one is only reachable by being placed
        // inside another, which is not an error.
        Assert.DoesNotContain("Route(", Write("<p>a</p>"));
    }

    [Fact]
    public void KeepsAnAttributeWhoseValueIsAnExpressionWhole()
    {
        // The parser splits on the @ before the tag is ever whole, so an
        // attribute arrives as three nodes. Writing them one at a time emitted
        // the tag as text with the expression stranded in the middle: an
        // unterminated string, and a component that did not compile.
        var written = Write("<div class=\"@Kind\">a</div>");

        Assert.Contains("AddAttribute", written);
        Assert.Contains("\"class\", Kind", written);
        Assert.DoesNotContain("class=\"\"", written);
    }

    [Fact]
    public void PutsTheTemplatesOwnMembersOnTheClass()
    {
        var written = Write("@Functions\n    Private n As Integer = 1\n@End Functions\n");

        Assert.Contains("Private n As Integer = 1", written);

        // Not the stray @ the parser used to leave behind: the scan matches
        // "End Functions" while the template writes "@End Functions", and the
        // marker was written into the class as a line of its own.
        Assert.DoesNotContain("\n@\n", written);
    }
}
