using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Parsing .vbhtml templates: Razor's markup model written in Visual Basic.
/// </summary>
public class VbHtmlParserTests
{
    private static T Single<T>(VbHtmlDocument document) where T : VbHtmlNode =>
        document.Nodes.OfType<T>().Single();

    [Fact]
    public void ReadsPlainMarkupAsOneLiteral()
    {
        var document = VbHtmlParser.Parse("<h1>Hello</h1>");

        Assert.Equal("<h1>Hello</h1>", Single<HtmlNode>(document).Text);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void ReadsASimpleExpression()
    {
        var document = VbHtmlParser.Parse("<p>@name</p>");

        Assert.Equal("name", Single<ExpressionNode>(document).Expression);
    }

    [Fact]
    public void ReadsAMemberChain()
    {
        var document = VbHtmlParser.Parse("<p>@Model.Customer.Name</p>");

        Assert.Equal("Model.Customer.Name", Single<ExpressionNode>(document).Expression);
    }

    [Fact]
    public void ReadsACallWithArguments()
    {
        var document = VbHtmlParser.Parse("<p>@Model.Total.ToString(\"C\")</p>");

        Assert.Equal("Model.Total.ToString(\"C\")", Single<ExpressionNode>(document).Expression);
    }

    [Fact]
    public void StopsAnExpressionAtSentencePunctuation()
    {
        // The full stop ends the sentence, not the member chain.
        var document = VbHtmlParser.Parse("Hello @name. Welcome.");

        Assert.Equal("name", Single<ExpressionNode>(document).Expression);
        Assert.Contains(document.Nodes.OfType<HtmlNode>(), h => h.Text.StartsWith(". Welcome"));
    }

    [Fact]
    public void ReadsAParenthesisedExpression()
    {
        var document = VbHtmlParser.Parse("<p>@(price * quantity)</p>");

        Assert.Equal("price * quantity", Single<ExpressionNode>(document).Expression);
    }

    [Fact]
    public void TreatsADoubledAtSignAsALiteralOne()
    {
        var document = VbHtmlParser.Parse("<p>name@@example.com</p>");

        Assert.Empty(document.Nodes.OfType<ExpressionNode>());
        Assert.Contains("name@example.com", string.Concat(
            document.Nodes.OfType<HtmlNode>().Select(h => h.Text)));
    }

    [Fact]
    public void ReadsAnIfBlockWithItsBody()
    {
        var document = VbHtmlParser.Parse("""
            @If Model.IsActive Then
                <p>Active</p>
            @End If
            """);

        var block = Single<BlockNode>(document);
        Assert.Equal("If Model.IsActive Then", block.Opening);
        Assert.Equal("End If", block.Closing);
        Assert.Contains(block.Body.OfType<HtmlNode>(), h => h.Text.Contains("<p>Active</p>"));
    }

    [Fact]
    public void ReadsElseIfAndElseAsContinuations()
    {
        var document = VbHtmlParser.Parse("""
            @If n > 10 Then
                <p>big</p>
            @ElseIf n > 5 Then
                <p>medium</p>
            @Else
                <p>small</p>
            @End If
            """);

        var block = Single<BlockNode>(document);

        Assert.Equal(2, block.Clauses.Count);
        Assert.Equal("ElseIf n > 5 Then", block.Clauses[0].Keyword);
        Assert.Equal("Else", block.Clauses[1].Keyword);
        Assert.Contains(block.Clauses[1].Body.OfType<HtmlNode>(), h => h.Text.Contains("small"));
    }

    [Fact]
    public void ReadsForEachClosedByNext()
    {
        var document = VbHtmlParser.Parse("""
            @For Each item In Model.Items
                <li>@item.Title</li>
            @Next
            """);

        var block = Single<BlockNode>(document);

        Assert.Equal("For Each item In Model.Items", block.Opening);
        Assert.Equal("Next", block.Closing);
        Assert.Contains(block.Body.OfType<ExpressionNode>(), e => e.Expression == "item.Title");
    }

    [Theory]
    [InlineData("@While x < 10\n<p>a</p>\n@End While", "End While")]
    [InlineData("@Do\n<p>a</p>\n@Loop", "Loop")]
    [InlineData("@Using r = New X()\n<p>a</p>\n@End Using", "End Using")]
    [InlineData("@With obj\n<p>a</p>\n@End With", "End With")]
    [InlineData("@SyncLock o\n<p>a</p>\n@End SyncLock", "End SyncLock")]
    [InlineData("@Select Case v\n<p>a</p>\n@End Select", "End Select")]
    public void ReadsEveryBlockKind(string template, string expectedClosing)
    {
        var document = VbHtmlParser.Parse(template);

        Assert.Equal(expectedClosing, Single<BlockNode>(document).Closing);
    }

    [Fact]
    public void DistinguishesForEachFromFor()
    {
        // "For Each" must not be read as "For" followed by stray markup.
        var document = VbHtmlParser.Parse("@For Each i In items\n<p>a</p>\n@Next");

        Assert.StartsWith("For Each", Single<BlockNode>(document).Opening);
    }

    [Fact]
    public void ReadsNestedBlocks()
    {
        var document = VbHtmlParser.Parse("""
            @For Each item In items
                @If item.Visible Then
                    <li>@item.Name</li>
                @End If
            @Next
            """);

        var outer = Single<BlockNode>(document);
        var inner = outer.Body.OfType<BlockNode>().Single();

        Assert.Equal("Next", outer.Closing);
        Assert.Equal("End If", inner.Closing);
        Assert.Contains(inner.Body.OfType<ExpressionNode>(), e => e.Expression == "item.Name");
    }

    [Fact]
    public void ReadsACodeBlock()
    {
        var document = VbHtmlParser.Parse("""
            @Code
                Dim total = 0
                total += 1
            End Code
            <p>@total</p>
            """);

        var statement = Single<StatementNode>(document);

        Assert.Contains("Dim total = 0", statement.Code);
        Assert.Contains("total += 1", statement.Code);
    }

    [Fact]
    public void ReadsTheModelTypeDirective()
    {
        var document = VbHtmlParser.Parse("@ModelType MyApp.Customer\n<p>@Model.Name</p>");

        Assert.Equal("MyApp.Customer", document.ModelType);
    }

    [Fact]
    public void ReadsImportsDirectives()
    {
        var document = VbHtmlParser.Parse("@Imports System.Linq\n@Imports MyApp.Models\n<p>x</p>");

        Assert.Equal(["System.Linq", "MyApp.Models"], document.Imports);
    }

    [Fact]
    public void MarksHtmlRawAsUnencoded()
    {
        var document = VbHtmlParser.Parse("<div>@Html.Raw(Model.Body)</div>");

        var expression = Single<ExpressionNode>(document);

        Assert.True(expression.IsRaw);
        Assert.Equal("Model.Body", expression.Expression);
    }

    [Fact]
    public void KeywordsInsideStringLiteralsDoNotEndABlock()
    {
        var document = VbHtmlParser.Parse("""
            @If x Then
                <p>@("End If is just text")</p>
            @End If
            """);

        var block = Single<BlockNode>(document);

        Assert.Equal("End If", block.Closing);
        Assert.Contains(block.Body.OfType<ExpressionNode>(),
            e => e.Expression.Contains("End If is just text"));
    }

    [Fact]
    public void ReportsAnUnclosedBlock()
    {
        var document = VbHtmlParser.Parse("@If x Then\n<p>a</p>");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH002");
    }

    [Fact]
    public void ReportsAnUnclosedCodeBlock()
    {
        var document = VbHtmlParser.Parse("@Code\nDim x = 1");

        Assert.Contains(document.Diagnostics, d => d.Id == "VBH001");
    }

    [Fact]
    public void MatchesKeywordsRegardlessOfCase()
    {
        // Visual Basic is case-insensitive, and so is its template syntax.
        var document = VbHtmlParser.Parse("@if x Then\n<p>a</p>\n@END IF");

        Assert.Equal("End If", Single<BlockNode>(document).Closing);
        Assert.Empty(document.Diagnostics);
    }

    [Fact]
    public void DoesNotMistakeALongerWordForAKeyword()
    {
        // "@Ifford" is an expression, not an If block.
        var document = VbHtmlParser.Parse("<p>@Ifford</p>");

        Assert.Equal("Ifford", Single<ExpressionNode>(document).Expression);
        Assert.Empty(document.Nodes.OfType<BlockNode>());
    }

    [Fact]
    public void RecordsLineNumbersForDiagnostics()
    {
        var document = VbHtmlParser.Parse("<p>a</p>\n<p>b</p>\n@If x Then\n<p>c</p>");

        var diagnostic = Assert.Single(document.Diagnostics);
        Assert.Equal(3, diagnostic.Line);
    }
}
