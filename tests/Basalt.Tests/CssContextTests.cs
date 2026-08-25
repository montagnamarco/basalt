using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>Reading what the caret is in, in a half-written stylesheet.</summary>
public class CssContextReaderTests
{
    private static CssContext At(string css)
    {
        var caret = css.IndexOf('|');

        Assert.True(caret >= 0, "The test stylesheet must mark the caret with |.");

        return CssContextReader.At(css.Remove(caret, 1), caret);
    }

    [Fact]
    public void ReadsTheTopLevelAsASelector()
    {
        Assert.Equal(CssContextKind.Selector, At(".butt|").Kind);
    }

    [Fact]
    public void ReadsInsideARuleAsAPropertyName()
    {
        var context = At(".button { colo|");

        Assert.Equal(CssContextKind.PropertyName, context.Kind);
        Assert.Equal("colo", context.Prefix);
    }

    [Fact]
    public void ReadsAfterAColonAsAValue()
    {
        var context = At(".button { display: fl|");

        Assert.Equal(CssContextKind.PropertyValue, context.Kind);
        Assert.Equal("display", context.Property);
        Assert.Equal("fl", context.Prefix);
    }

    [Fact]
    public void ReadsAnEmptyValue()
    {
        var context = At(".button { display: |");

        Assert.Equal(CssContextKind.PropertyValue, context.Kind);
        Assert.Equal("display", context.Property);
    }

    [Fact]
    public void ReadsAPropertyNameAgainAfterASemicolon()
    {
        // The previous declaration is finished, so this is a new property.
        var context = At(".button { display: flex; colo|");

        Assert.Equal(CssContextKind.PropertyName, context.Kind);
    }

    [Fact]
    public void ReadsASelectorAgainAfterAClosedRule()
    {
        Assert.Equal(CssContextKind.Selector, At(".a { color: red; } .b|").Kind);
    }

    [Fact]
    public void ReadsAHyphenatedPropertyName()
    {
        Assert.Equal("flex-direction", At(".a { flex-direction: col|").Property);
    }

    [Fact]
    public void OffersNothingInsideAComment()
    {
        Assert.Equal(CssContextKind.Comment, At("/* .a { colo|").Kind);
    }

    [Fact]
    public void ReadsAStylesheetAfterAFinishedComment()
    {
        Assert.Equal(CssContextKind.Selector, At("/* note */ .butt|").Kind);
    }

    [Fact]
    public void KeepsASelectorPrefixWithItsPunctuation()
    {
        // ".butt" and "#butt" select different things; the prefix says which.
        Assert.Equal(".butt", At(".butt|").Prefix);
        Assert.Equal("#main", At("#main|").Prefix);
    }

    [Fact]
    public void CopesWithAnEmptyStylesheet()
    {
        Assert.Equal(CssContextKind.Selector, CssContextReader.At("", 0).Kind);
    }

    [Fact]
    public void CopesWithACaretPastTheEnd()
    {
        Assert.Equal(CssContextKind.Selector, CssContextReader.At(".a { }", 999).Kind);
    }
}

/// <summary>What a stylesheet offers where.</summary>
public class CssLanguageTests
{
    [Fact]
    public void KnowsTheCommonProperties()
    {
        Assert.Contains("display", CssLanguage.Properties);
        Assert.Contains("margin", CssLanguage.Properties);
        Assert.Contains("grid-template-columns", CssLanguage.Properties);
    }

    [Fact]
    public void OffersTheValuesOfAClosedProperty()
    {
        var values = CssLanguage.ValuesFor("display");

        Assert.Contains("flex", values);
        Assert.Contains("grid", values);
    }

    [Fact]
    public void OffersNoValuesWhereTheSetIsOpen()
    {
        // A width can be anything; a list would be misleading.
        Assert.Empty(CssLanguage.ValuesFor("width"));
    }

    [Fact]
    public void MatchesAPropertyWhateverItsCase()
    {
        Assert.NotEmpty(CssLanguage.ValuesFor("DISPLAY"));
    }

    [Fact]
    public void KnowsTheAtRules()
    {
        Assert.Contains("@media", CssLanguage.AtRules);
        Assert.Contains("@keyframes", CssLanguage.AtRules);
    }

    [Fact]
    public void ListsEachPropertyOnlyOnce()
    {
        Assert.Equal(
            CssLanguage.Properties.Count,
            CssLanguage.Properties.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
