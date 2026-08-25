using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Runtime;

namespace Basalt.Tests;

/// <summary>
/// Layouts, sections and the Html helper.
///
/// The largest gap the audit found: without these a view is a single
/// self-contained page, and the MVC-shaped projects the templates scaffold
/// hit a wall on their first day.
/// </summary>
public sealed class RazorCompositionTests
{
    // Parsing

    [Fact]
    public void ReadsASection()
    {
        var document = VbHtmlParser.Parse("""
            @Section Scripts
                <script src="a.js"></script>
            @End Section
            """);

        Assert.Empty(document.Diagnostics);

        var section = Assert.Single(document.Nodes.OfType<SectionNode>());

        Assert.Equal("Scripts", section.Name);
        Assert.NotEmpty(section.Body);
    }

    [Fact]
    public void ASectionMayHoldExpressions()
    {
        var document = VbHtmlParser.Parse("""
            @Section Head
                <title>@Model.Title</title>
            @End Section
            """);

        var section = Assert.Single(document.Nodes.OfType<SectionNode>());

        Assert.Contains(section.Body, n => n is ExpressionNode);
    }

    [Fact]
    public void SaysWhenASectionIsNeverClosed()
    {
        var document = VbHtmlParser.Parse("@Section Scripts\n<p>a</p>");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH007");
    }

    [Fact]
    public void SaysWhenASectionHasNoName()
    {
        var document = VbHtmlParser.Parse("@Section\n@End Section");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH006");
    }

    [Fact]
    public void ReadsAFunctionsBlock()
    {
        var document = VbHtmlParser.Parse("""
            @Functions
                Public Function Twice(n As Integer) As Integer
                    Return n * 2
                End Function
            @End Functions
            <p>@Twice(21)</p>
            """);

        Assert.Empty(document.Diagnostics);

        var members = Assert.Single(document.Functions);

        Assert.Contains("Public Function Twice", members, StringComparison.Ordinal);
    }

    [Fact]
    public void SaysWhenAFunctionsBlockIsNeverClosed()
    {
        var document = VbHtmlParser.Parse("@Functions\n    Public Sub M()\n    End Sub");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH005");
    }

    // Generating

    [Fact]
    public void WritesASectionAsADeferredBody()
    {
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("@Section Scripts\n<p>a</p>\n@End Section"),
            "Page", "App.Views");

        Assert.Contains("DefineSection(\"Scripts\", Sub()", code, StringComparison.Ordinal);
        Assert.Contains("End Sub)", code, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesFunctionsIntoTheClassNotIntoExecute()
    {
        // A method cannot be declared inside a method.
        var code = VbHtmlCodeWriter.Write(
            VbHtmlParser.Parse("""
                @Functions
                    Public Function Twice(n As Integer) As Integer
                        Return n * 2
                    End Function
                @End Functions
                """),
            "Page", "App.Views");

        var executeEnd = code.IndexOf("        End Sub", StringComparison.Ordinal);
        var functionAt = code.IndexOf("Public Function Twice", StringComparison.Ordinal);

        Assert.True(functionAt > executeEnd,
            $"The function landed inside Execute:\n{code}");
    }

    // The runtime

    [Fact]
    public void ALayoutPlacesWhatTheInnerViewProduced()
    {
        var inner = new StubView(v => v.Say("inner"));
        var layout = new StubView(v => { v.Say("<main>"); v.PlaceBody(); v.Say("</main>"); });

        Assert.Equal("<main>inner</main>", inner.RenderWith(layout));
    }

    [Fact]
    public void ALayoutPlacesASectionTheInnerViewDefined()
    {
        var inner = new StubView(v =>
        {
            v.Define("Scripts", () => v.Say("<script></script>"));
            v.Say("body");
        });

        var layout = new StubView(v => { v.PlaceBody(); v.Place("Scripts"); });

        Assert.Equal("body<script></script>", inner.RenderWith(layout));
    }

    [Fact]
    public void AMissingRequiredSectionIsAnError()
    {
        // The author should see it rather than wonder where the scripts went.
        var inner = new StubView(v => v.Say("body"));
        var layout = new StubView(v => v.Place("Scripts"));

        var error = Assert.Throws<InvalidOperationException>(() => inner.RenderWith(layout));

        Assert.Contains("Scripts", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingOptionalSectionWritesNothing()
    {
        var inner = new StubView(v => v.Say("body"));
        var layout = new StubView(v => { v.PlaceBody(); v.Place("Scripts", required: false); });

        Assert.Equal("body", inner.RenderWith(layout));
    }

    [Fact]
    public void ALayoutCanAskWhetherASectionExists()
    {
        var withSection = new StubView(v => v.Define("Scripts", () => v.Say("x")));
        var without = new StubView(v => v.Say(""));

        var layout = new StubView(v => v.Say(v.Knows("Scripts") ? "yes" : "no"));

        Assert.Equal("yes", withSection.RenderWith(layout));
        Assert.Equal("no", without.RenderWith(new StubView(v => v.Say(v.Knows("Scripts") ? "yes" : "no"))));
    }

    // The Html helper

    [Fact]
    public void HtmlRawIsACallableThingNow()
    {
        // It used to be a trick in the parser: "Html.Raw" was recognised by
        // name and never existed as an object.
        var view = new StubView(v => v.WriteValue(v.Helper.Raw("<b>bold</b>")));

        Assert.Equal("<b>bold</b>", view.Render());
    }

    [Fact]
    public void HtmlEncodeIsCallableToo()
    {
        // Signature help advertised it while it could not be called at all.
        var view = new StubView(v => v.Say(v.Helper.Encode("<b>")));

        Assert.Equal("&lt;b&gt;", view.Render());
    }

    [Fact]
    public void HtmlEncodeMatchesWhatThePageWouldDo()
    {
        // Two encoders that could disagree would be a security question.
        var written = new StubView(v => v.WriteValue("<b>&\"'"));
        var encoded = new StubView(v => v.Say(v.Helper.Encode("<b>&\"'")));

        Assert.Equal(written.Render(), encoded.Render());
    }

    /// <summary>A view whose body is given as a lambda, for testing.</summary>
    private sealed class StubView(Action<StubView> body) : VbHtmlView
    {
        public override void Execute() => body(this);

        public HtmlHelper Helper => Html;

        public void Say(string text) => WriteLiteral(text);
        public void WriteValue(object? value) => Write(value);
        public void PlaceBody() => RenderBody();
        public void Define(string name, Action content) => DefineSection(name, content);
        public void Place(string name, bool required = true) => RenderSection(name, required);
        public bool Knows(string name) => IsSectionDefined(name);
    }
}
