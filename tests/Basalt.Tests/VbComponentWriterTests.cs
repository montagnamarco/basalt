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

    [Fact]
    public void MapsACaretIntoTheExpressionItIsOn()
    {
        // The mappings are what the editor runs on: a caret in a component is
        // moved through them into the generated code, asked about there, and
        // the answer moved back. Without them a component could be coloured —
        // that comes from the parser — and could answer nothing else.
        const string template = "<p>@Model.Nome</p>\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "C", "N", "/x/C.vbrazor");

        var caret = template.IndexOf("Model.", StringComparison.Ordinal) + "Model.".Length;
        var mapped = generated.Map.ToGenerated(caret, template, generated.Code);

        Assert.NotNull(mapped);

        // Inside the expression, not at the start of the statement carrying
        // it: mapping the whole line put the caret before "__builder", far
        // enough out that Roslyn was asked about the wrong token.
        var around = generated.Code.Substring(mapped!.Value - 12, 12);

        Assert.Contains("Model.", around);
    }

    [Fact]
    public void MapsACaretInsideABlocksCondition()
    {
        // The opening clause carries the interesting expression — the
        // condition of an If, the source of a For Each — so a caret there has
        // to reach the compiler. Mapping only the body left every question
        // asked inside a condition unanswered.
        const string template = "@If ok Then\n<b>s</b>\n@End If\n";

        var generated = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "C", "N", "/x/C.vbrazor");

        var caret = template.IndexOf("ok", StringComparison.Ordinal) + 2;

        Assert.NotNull(generated.Map.ToGenerated(caret, template, generated.Code));
    }

    [Fact]
    public void KeepsEveryDirectiveOnItsOwnLine()
    {
        // #ExternalSource has to start a line. Writing an expression with
        // Append rather than AppendLine left the builder mid-line, and the
        // directive was glued into the middle of the call it was meant to
        // wrap — a file that did not compile.
        var written = VbComponentWriter.WriteWithMap(
            VbHtmlParser.Parse("<p>@Nome</p>\n"), "C", "N", "/x/C.vbrazor").Code;

        foreach (var line in written.Split('\n'))
        {
            var trimmed = line.TrimStart();

            if (!trimmed.StartsWith("#", StringComparison.Ordinal)) continue;

            Assert.Equal(trimmed, line.Trim());
        }

        Assert.DoesNotContain("AddContent(1, #", written);
    }

    [Fact]
    public void RegistersAnEventHandlerWrittenInAnAttribute()
    {
        // Three defects in a row, each hiding the next. The parser stopped at
        // the keyword, so onclick="@AddressOf Go" produced AddAttribute(..,
        // AddressOf) with the method name stranded in the markup. Then
        // AddAttribute had no overload for a bare delegate. Then the element
        // was written as literal markup, so the attribute followed no element
        // frame and Blazor refused it at render.
        var written = Write("<button onclick=\"@AddressOf Vai\">x</button>");

        Assert.Contains("OpenElement", written);
        Assert.Contains("EventCallback.Factory.Create(Me, AddressOf Vai)", written);

        // The tag's own bracket is Blazor's to write: leaving the one from the
        // template put a stray > in the page.
        Assert.DoesNotContain("\">", written.Substring(written.IndexOf("AddAttribute", StringComparison.Ordinal)));
    }

    [Fact]
    public void KeepsAddressOfWithWhatItPointsAt()
    {
        // Read as a member chain it stopped at the keyword, leaving the method
        // name behind as text.
        var written = Write("<p>@AddressOf Vai</p>");

        Assert.Contains("AddressOf Vai", written);
    }

    [Fact]
    public void PlacesAnotherComponentRatherThanInventingATag()
    {
        // <Saluto Nome="x" /> was written out as an HTML element called
        // "Saluto", so the browser received an invented tag and the parameter
        // was dropped: the page said <Saluto Nome="Marco"></Saluto> in plain
        // sight and rendered nothing of the component.
        var written = Write("<Saluto Nome=\"Marco\" />");

        Assert.Contains("OpenComponent(Of Saluto)", written);
        Assert.Contains("AddComponentParameter", written);
        Assert.Contains("CloseComponent()", written);

        // Unqualified: rooting the name at the namespace we wrote misses the
        // RootNamespace the compiler prepends, and the generator deliberately
        // does not know it.
        Assert.DoesNotContain("OpenComponent(Of Global.", written);
    }

    [Fact]
    public void ClosesEachTagTheWayItWasOpened()
    {
        // A component closes with CloseComponent and an element with
        // CloseElement. Calling the wrong one leaves the render tree
        // unbalanced for everything after it.
        var written = Write("<div><Saluto /></div>");

        var open = written.Split("OpenComponent").Length - 1;
        var close = written.Split("CloseComponent").Length - 1;

        Assert.Equal(open, close);
        Assert.Equal(
            written.Split("OpenElement").Length - 1,
            written.Split("CloseElement").Length - 1);
    }

    [Fact]
    public void PassesWhatIsInsideAComponentAsChildContent()
    {
        // Written straight into the tree it became the parent's own frames and
        // vanished: the box rendered and everything inside it was gone.
        var written = Write("<Box><p>dentro</p></Box>");

        Assert.Contains("\"ChildContent\"", written);
        Assert.Contains("RenderFragment", written);

        // And the children go to the lambda's builder, not the outer one.
        Assert.Contains("__child.OpenElement", written);
    }

    [Fact]
    public void TreatsALowerCaseTagAsMarkup()
    {
        // The first letter is the whole distinction, the way it is in Razor:
        // element names are lower case and a component is a class.
        var written = Write("<div><span>a</span></div>");

        Assert.DoesNotContain("OpenComponent", written);
    }

    [Fact]
    public void BindsAnInputBothWays()
    {
        // @bind is two attributes, not one: the value going out and the
        // handler bringing the change back. Written as a single attribute
        // called "bind" the browser received a meaningless one and nothing was
        // ever read back — a form that looked right and lost every keystroke.
        var written = Write("<input @bind=\"@nome\" />");

        Assert.Contains("\"value\", Global.Microsoft.AspNetCore.Components.BindConverter.FormatValue(nome)", written);
        Assert.Contains("\"onchange\"", written);

        // Called as the shared method it is: Visual Basic does not apply an
        // extension to a fully qualified chain, so the obvious spelling failed
        // to resolve even with the namespace imported.
        Assert.Contains("EventCallbackFactoryBinderExtensions.CreateBinder", written);
    }

    [Fact]
    public void BindsAComponentParameterToItsChangedCallback()
    {
        var written = Write("<TextBox @bind-Value=\"@nome\" />");

        Assert.Contains("\"Value\"", written);
        Assert.Contains("\"ValueChanged\"", written);
        Assert.Contains("OpenComponent(Of TextBox)", written);
    }

    [Fact]
    public void NamesAFrameworkComponentInFull()
    {
        // A sibling is named unqualified so the compiler's RootNamespace is
        // applied for us; the framework's own components are not siblings and
        // resolve to nothing that way.
        var written = Write("<CascadingValue Value=\"@tema\"><p>a</p></CascadingValue>");

        Assert.Contains("Global.Microsoft.AspNetCore.Components.CascadingValue(Of String)", written);
    }

    [Fact]
    public void PassesATypeArgumentThroughToAGenericComponent()
    {
        // The C# compiler infers this from the parameter values, which needs
        // the type system. Here the template says it — and read a character at
        // a time the name stopped at the bracket, leaving "(Of String)" to be
        // taken for attributes.
        var written = Write("<Elenco(Of String) Voci=\"@nomi\" />");

        Assert.Contains("OpenComponent(Of Elenco(Of String))", written);
        Assert.Contains("\"Voci\", nomi", written);
    }

    [Fact]
    public void DoesNotOpenChildContentForASelfClosingComponent()
    {
        // It opened a lambda nothing ever closed, so the generated file ended
        // mid-statement: "End Sub expected", against generated code.
        var written = Write("<Elenco Voci=\"@nomi\" />");

        Assert.DoesNotContain("ChildContent", written);
        Assert.Equal(
            written.Split("Sub(__child").Length,
            written.Split("End Sub,").Length);
    }
}
