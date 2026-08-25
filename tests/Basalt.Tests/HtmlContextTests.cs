using Basalt.Razor.Vb.Web;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Reading what the caret is in, in markup that is half-typed.
///
/// Half-typed is the only state this is ever asked about, so most of these
/// give it markup no parser would accept.
/// </summary>
public class HtmlContextReaderTests
{
    private static HtmlContext At(string markup)
    {
        // The caret is written as "|" in the markup.
        var caret = markup.IndexOf('|');

        Assert.True(caret >= 0, "The test markup must mark the caret with |.");

        return HtmlContextReader.At(markup.Remove(caret, 1), caret);
    }

    [Fact]
    public void ReadsTextBetweenTagsAsContent()
    {
        Assert.Equal(HtmlContextKind.Content, At("<p>hello |</p>").Kind);
    }

    [Fact]
    public void ReadsAnElementNameBeingTyped()
    {
        var context = At("<di|");

        Assert.Equal(HtmlContextKind.ElementName, context.Kind);
        Assert.Equal("di", context.Prefix);
    }

    [Fact]
    public void ReadsAnElementNameRightAfterTheAngle()
    {
        var context = At("<|");

        Assert.Equal(HtmlContextKind.ElementName, context.Kind);
        Assert.Equal("", context.Prefix);
    }

    [Fact]
    public void ReadsAClosingTagAsAnElementName()
    {
        Assert.Equal("di", At("</di|").Element);
    }

    [Fact]
    public void ReadsAnAttributeNameAndWhatItBelongsTo()
    {
        var context = At("<input ty|");

        Assert.Equal(HtmlContextKind.AttributeName, context.Kind);
        Assert.Equal("input", context.Element);
        Assert.Equal("ty", context.Prefix);
    }

    [Fact]
    public void ReadsAnAttributeNameAfterAnotherAttribute()
    {
        var context = At("<input type=\"text\" na|");

        Assert.Equal(HtmlContextKind.AttributeName, context.Kind);
        Assert.Equal("na", context.Prefix);
    }

    [Fact]
    public void ReadsAnAttributeValueAndWhichAttributeItIs()
    {
        var context = At("<input type=\"te|");

        Assert.Equal(HtmlContextKind.AttributeValue, context.Kind);
        Assert.Equal("type", context.Attribute);
        Assert.Equal("input", context.Element);
        Assert.Equal("te", context.Prefix);
    }

    [Fact]
    public void ReadsAnEmptyAttributeValue()
    {
        var context = At("<input type=\"|");

        Assert.Equal(HtmlContextKind.AttributeValue, context.Kind);
        Assert.Equal("", context.Prefix);
    }

    [Fact]
    public void ReadsAValueInSingleQuotes()
    {
        Assert.Equal(HtmlContextKind.AttributeValue, At("<input type='te|").Kind);
    }

    [Fact]
    public void TreatsAClosedValueAsBackInsideTheTag()
    {
        // The quotes are balanced, so the caret is between attributes again.
        Assert.Equal(HtmlContextKind.AttributeName, At("<input type=\"text\" |").Kind);
    }

    [Fact]
    public void TreatsTextAfterAClosedTagAsContent()
    {
        Assert.Equal(HtmlContextKind.Content, At("<div class=\"a\">|").Kind);
    }

    [Fact]
    public void OffersNothingInsideAComment()
    {
        Assert.Equal(HtmlContextKind.Comment, At("<!-- <div cla|").Kind);
    }

    [Fact]
    public void ReadsMarkupAfterAFinishedComment()
    {
        Assert.Equal(HtmlContextKind.ElementName, At("<!-- a --><di|").Kind);
    }

    [Fact]
    public void ReadsTheNearestTagWhenSeveralArePresent()
    {
        var context = At("<div class=\"x\"><input ty|");

        Assert.Equal("input", context.Element);
    }

    [Fact]
    public void CopesWithAnEmptyDocument()
    {
        Assert.Equal(HtmlContextKind.Content, HtmlContextReader.At("", 0).Kind);
    }

    [Fact]
    public void CopesWithACaretPastTheEnd()
    {
        Assert.Equal(HtmlContextKind.Content, HtmlContextReader.At("<p>a</p>", 999).Kind);
    }

    [Fact]
    public void ReadsAHyphenatedAttributeName()
    {
        Assert.Equal("data-id", At("<div data-id=\"|").Attribute);
    }
}

/// <summary>What HTML offers where.</summary>
public class HtmlLanguageTests
{
    [Fact]
    public void KnowsTheCommonElements()
    {
        Assert.Contains("div", HtmlLanguage.Elements);
        Assert.Contains("input", HtmlLanguage.Elements);
        Assert.Contains("table", HtmlLanguage.Elements);
    }

    [Fact]
    public void KnowsWhichElementsNeverClose()
    {
        Assert.Contains("br", HtmlLanguage.VoidElements);
        Assert.Contains("img", HtmlLanguage.VoidElements);
        Assert.DoesNotContain("div", HtmlLanguage.VoidElements);
    }

    [Fact]
    public void OffersAnElementsOwnAttributesAndTheGlobalOnes()
    {
        var attributes = HtmlLanguage.AttributesFor("img");

        Assert.Contains("src", attributes);
        Assert.Contains("alt", attributes);
        Assert.Contains("class", attributes);
    }

    [Fact]
    public void OffersTheGlobalAttributesForAnUnknownElement()
    {
        // A custom element or a typo should not leave the user with nothing.
        var attributes = HtmlLanguage.AttributesFor("my-widget");

        Assert.Contains("class", attributes);
        Assert.Contains("id", attributes);
    }

    [Fact]
    public void OffersEachAttributeOnlyOnce()
    {
        // "style" is both global and, for some elements, listed again.
        var attributes = HtmlLanguage.AttributesFor("div");

        Assert.Equal(attributes.Count, attributes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void OffersTheValuesOfAnInputType()
    {
        var values = HtmlLanguage.ValuesFor("input", "type");

        Assert.Contains("checkbox", values);
        Assert.Contains("password", values);
    }

    [Fact]
    public void OffersDifferentValuesForAButtonType()
    {
        // A button takes three types, not the twenty an input takes.
        Assert.Equal(3, HtmlLanguage.ValuesFor("button", "type").Count);
    }

    [Fact]
    public void OffersNoValuesWhereTheSetIsOpen()
    {
        Assert.Empty(HtmlLanguage.ValuesFor("div", "class"));
    }

    [Fact]
    public void MatchesAttributeNamesWhateverTheirCase()
    {
        Assert.NotEmpty(HtmlLanguage.ValuesFor("INPUT", "TYPE"));
    }
}
