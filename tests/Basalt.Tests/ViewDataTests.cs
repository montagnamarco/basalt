using Basalt.Razor.Vb.Runtime;

namespace Basalt.Tests;

/// <summary>
/// The loose values a view is handed.
///
/// A layout reads them without knowing which view it is wrapping, which is
/// why a missing key gives Nothing rather than throwing: a page should not
/// fail to render over an unset heading.
/// </summary>
public sealed class ViewDataTests
{
    private sealed class StubView(Action<StubView> body) : VbHtmlView
    {
        public override void Execute() => body(this);

        public void Say(string text) => WriteLiteral(text);
        public void PlaceBody() => RenderBody();
    }

    [Fact]
    public void KeepsWhatWasPutIn()
    {
        var data = new ViewDataDictionary { ["Title"] = "Home" };

        Assert.Equal("Home", data["Title"]);
    }

    [Fact]
    public void AMissingKeyIsNothingRatherThanAnError()
    {
        Assert.Null(new ViewDataDictionary()["Absent"]);
    }

    [Fact]
    public void TellsAnUnsetKeyFromOneSetToNothing()
    {
        var data = new ViewDataDictionary { ["Set"] = null };

        Assert.True(data.Contains("Set"));
        Assert.False(data.Contains("Unset"));
    }

    [Fact]
    public void TheKeysAreNotCaseSensitive()
    {
        // Razor treats them that way, and a template written by hand should
        // not fail over a capital letter.
        var data = new ViewDataDictionary { ["Title"] = "Home" };

        Assert.Equal("Home", data["title"]);
    }

    [Fact]
    public void ReadsThroughViewBagAsWell()
    {
        var data = new ViewDataDictionary { ["Title"] = "Home" };

        dynamic bag = data;

        Assert.Equal("Home", bag.Title);
    }

    [Fact]
    public void WritesThroughViewBagAsWell()
    {
        dynamic bag = new ViewDataDictionary();

        bag.Title = "Home";

        Assert.Equal("Home", ((ViewDataDictionary)bag)["Title"]);
    }

    [Fact]
    public void AViewBagKeyNeverSetIsNothing()
    {
        dynamic bag = new ViewDataDictionary();

        Assert.Null(bag.Absent);
    }

    [Fact]
    public void TheLayoutSeesWhatTheViewSet()
    {
        // The case that matters: a layout writes the title, and only the view
        // knows what it is.
        var inner = new StubView(v =>
        {
            v.ViewData["Title"] = "Customers";
            v.Say("body");
        });

        var layout = new StubView(v =>
        {
            v.Say($"<title>{v.ViewData["Title"]}</title>");
            v.PlaceBody();
        });

        Assert.Equal("<title>Customers</title>body", inner.RenderWith(layout));
    }

    [Fact]
    public void ALayoutReadingAnUnsetTitleStillRenders()
    {
        var inner = new StubView(v => v.Say("body"));

        var layout = new StubView(v =>
        {
            v.Say($"<title>{v.ViewData["Title"]}</title>");
            v.PlaceBody();
        });

        Assert.Equal("<title></title>body", inner.RenderWith(layout));
    }
}
