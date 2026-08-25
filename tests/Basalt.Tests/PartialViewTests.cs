using Basalt.Razor.Vb.Runtime;

namespace Basalt.Tests;

/// <summary>
/// Rendering one view inside another.
///
/// A row template used by a table is the everyday case, and without partials
/// the only way to write one is to repeat it. The base class cannot find a
/// view by name — it knows nothing about folders — so the host supplies the
/// lookup, and a missing partial says so rather than leaving a silent gap.
/// </summary>
public sealed class PartialViewTests
{
    private sealed class StubView(Action<StubView> body) : VbHtmlView
    {
        public override void Execute() => body(this);

        public object? Model { get; set; }

        public void Say(string text) => WriteLiteral(text);
        public void SayValue(object? value) => Write(value);
        public void Place(string name, object? model = null) => RenderPartial(name, model);
        public HtmlHelper Helper => Html;
    }

    [Fact]
    public void RendersAPartialInPlace()
    {
        var row = new StubView(v => v.Say("<td>a row</td>"));

        var page = new StubView(v =>
        {
            v.Say("<table>");
            v.Place("_Row");
            v.Say("</table>");
        })
        {
            PartialLookup = name => name == "_Row" ? row : null
        };

        Assert.Equal("<table><td>a row</td></table>", page.Render());
    }

    [Fact]
    public void AMissingPartialSaysSo()
    {
        // Rather than leaving a gap the author has to hunt for.
        var page = new StubView(v => v.Place("_Absent"))
        {
            PartialLookup = _ => null
        };

        var error = Assert.Throws<InvalidOperationException>(() => page.Render());

        Assert.Contains("_Absent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APartialGetsTheModelItWasHanded()
    {
        var row = new StubView(v => v.SayValue(v.Model));

        var page = new StubView(v => v.Place("_Row", "given"))
        {
            PartialLookup = _ => row
        };

        Assert.Equal("given", page.Render());
    }

    [Fact]
    public void APartialSeesTheSameViewData()
    {
        // A row template is often told what to draw through ViewData.
        var row = new StubView(v => v.SayValue(v.ViewData["Heading"]));

        var page = new StubView(v =>
        {
            v.ViewData["Heading"] = "Customers";
            v.Place("_Row");
        })
        {
            PartialLookup = _ => row
        };

        Assert.Equal("Customers", page.Render());
    }

    [Fact]
    public void APartialCanRenderAPartialItself()
    {
        // The lookup is passed down, or a nested partial would fail.
        var inner = new StubView(v => v.Say("inner"));
        var outer = new StubView(v => { v.Say("["); v.Place("_Inner"); v.Say("]"); });

        var page = new StubView(v => v.Place("_Outer"))
        {
            PartialLookup = name => name == "_Outer" ? outer : inner
        };

        Assert.Equal("[inner]", page.Render());
    }

    [Fact]
    public void HtmlPartialWritesTheMarkupUnencoded()
    {
        // "@Html.Partial" is how a template writes it, and the result is
        // markup: encoding it would put tags on the page as text.
        var row = new StubView(v => v.Say("<td>a</td>"));

        var page = new StubView(v => v.SayValue(v.Helper.Partial("_Row")))
        {
            PartialLookup = _ => row
        };

        Assert.Equal("<td>a</td>", page.Render());
    }
}
