using Basalt.Workspace.Completion;

namespace Basalt.Tests;

/// <summary>
/// Where the name being written begins, which a completion list replaces, in
/// each kind of file. The list and the entry must agree on the whole name:
/// Enter on "background-c" wrote "background-background-color" when the word
/// stopped at the dash.
/// </summary>
public sealed class CompletionWordTests
{
    /// <summary>The word's text, with the caret written as the end of the text given.</summary>
    private static string WordIn(string file, string text) =>
        text[CompletionWord.StartOf(file, text, text.Length)..];

    [Theory]
    [InlineData("Site.css", "body {\n    background-c", "background-c")]
    [InlineData("Site.css", "body {\n    background-color: red;\n    margin-t", "margin-t")]
    [InlineData("Site.css", "@me", "@me")]
    [InlineData("Site.css", "body {\n    color: bl", "bl")]
    public void ACssNameKeepsItsDashes(string file, string text, string word) =>
        Assert.Equal(word, WordIn(file, text));

    [Theory]
    [InlineData("Index.vbhtml", "<a asp-a", "asp-a")]
    [InlineData("Index.vbhtml", "<div data-", "data-")]
    [InlineData("Index.vbhtml", "<button @onc", "@onc")]
    [InlineData("Counter.vbrazor", "<button @oncl", "@oncl")]
    [InlineData("Index.html", "<a aria-la", "aria-la")]
    [InlineData("Index.html", "<my-el", "my-el")]
    public void AMarkupNameKeepsItsDashesAndADirectiveItsAt(string file, string text, string word) =>
        Assert.Equal(word, WordIn(file, text));

    [Theory]
    [InlineData("Index.html", "<p>well-kn", "kn")]
    [InlineData("Index.vbhtml", "<p>Hi @Model.Na", "Na")]
    public void OutsideATagADashIsNotPartOfTheName(string file, string text, string word) =>
        Assert.Equal(word, WordIn(file, text));

    [Theory]
    [InlineData("Module1.vb", "Console.Wr", "Wr")]
    [InlineData("Module1.vb", "Dim total = a-b", "b")]
    [InlineData("Module1.vb", "Dim x = Foo_1", "Foo_1")]
    public void VisualBasicNamesAreLettersDigitsAndUnderscores(string file, string text, string word) =>
        Assert.Equal(word, WordIn(file, text));

    [Fact]
    public void InTheCodeOfATemplateADashIsSubtraction()
    {
        const string text = "@Code\n    Dim total = a-b\nEnd Code\n";
        var caret = text.IndexOf("a-b", StringComparison.Ordinal) + 3;

        Assert.Equal("b", text[CompletionWord.StartOf("Index.vbhtml", text, caret)..caret]);
    }

    [Fact]
    public void AComparisonInTheCodeOfATemplateIsNotTakenForATag()
    {
        // "a < b" is Visual Basic: read as markup it opened a tag, and "x-y" after it became one name.
        const string text = "@Code\n    Dim ok = a < b\n    Dim total = x-y\nEnd Code\n";
        var caret = text.IndexOf("x-y", StringComparison.Ordinal) + 3;

        Assert.Equal("y", text[CompletionWord.StartOf("Index.vbhtml", text, caret)..caret]);
    }

    [Fact]
    public void AnExpressionAfterAComparisonKeepsItsAtOutOfTheName()
    {
        // "@Mod" is an expression in the page's text, not a directive attribute: Enter must keep the "@".
        const string text = "<p>@(If(a < b, 1, 2)) @Mod";

        Assert.Equal("Mod", WordIn("Index.vbhtml", text));
    }

    [Theory]
    [InlineData("Counter.vbrazor", "<button @oncl", "@oncl")]
    [InlineData("Counter.vbrazor", "@If a < b Then\n<button @oncl", "@oncl")]
    public void ADirectiveAttributeInsideATagKeepsItsAt(string file, string text, string word) =>
        Assert.Equal(word, WordIn(file, text));

    [Fact]
    public void ACaretAfterASpaceHasNoWordToReplace()
    {
        const string text = "body {\n    color: red ";

        Assert.Equal(text.Length, CompletionWord.StartOf("Site.css", text, text.Length));
    }
}
