using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

/// <summary>
/// What the standalone server offers in the markup half.
///
/// It used to be 27 hardcoded tag names, returned wherever the caret was:
/// in running text, inside an attribute value, anywhere. Now it reads the
/// context and uses the same vocabulary as the IDE, from the shared library.
/// </summary>
public class LanguageServerMarkupCompletionTests
{
    private static IReadOnlyList<string> At(string text, int offset)
    {
        var document = new OpenDocument("file:///v.vbhtml", text, 1);

        return [.. VbHtmlCompletionHandler.Suggest(document, offset).Select(i => i.Label)];
    }

    [Fact]
    public void OffersElementsAfterAnAngleBracket()
    {
        var offered = At("<", 1);

        Assert.Contains("div", offered);
        Assert.Contains("section", offered);
    }

    [Fact]
    public void OffersMoreThanTheOldHardcodedList()
    {
        // The old list had 27 names and no way to grow.
        Assert.True(At("<", 1).Count > 27);
    }

    [Fact]
    public void OffersNothingInRunningText()
    {
        // The old handler offered the whole tag list here, into prose.
        Assert.Empty(At("<p>hello ", 9));
    }

    [Fact]
    public void OffersAttributesInsideATag()
    {
        var offered = At("<input ", 7);

        Assert.Contains("type", offered);

        // A global attribute applies everywhere.
        Assert.Contains("id", offered);
    }

    [Fact]
    public void OffersAttributesThatBelongToTheElement()
    {
        var offered = At("<a ", 3);

        Assert.Contains("href", offered);
    }

    [Fact]
    public void OffersValuesForAnAttributeThatHasThem()
    {
        var offered = At("<input type=\"", 13);

        Assert.Contains("checkbox", offered);
    }

    [Fact]
    public void StillOffersDirectivesAfterAnAtSign()
    {
        Assert.Contains("ModelType", At("@", 1));
    }
}
