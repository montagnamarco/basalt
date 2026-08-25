using Basalt.Razor.Vb;

namespace Basalt.Tests;

/// <summary>
/// Where an implicit expression ends.
///
/// In C# Razor an implicit expression cannot contain generics: "&lt;" opens
/// HTML. Visual Basic writes generics as "(Of T)" — parentheses, which the
/// reader already balances — so the restriction does not apply. What does
/// apply is the two places where Visual Basic's own syntax fooled the
/// reader: the conditional operator and "New".
/// </summary>
public class ImplicitExpressionTests
{
    private static string Body(string template)
    {
        var generated = VbHtmlCodeWriter.WriteWithMap(
            VbHtmlParser.Parse(template), "V", "N", "/v.vbhtml");

        var from = generated.Code.IndexOf("Execute()", StringComparison.Ordinal);
        var to = generated.Code.IndexOf("End Sub", StringComparison.Ordinal);

        return generated.Code.Substring(from, to - from);
    }

    [Fact]
    public void KeepsAGenericArgumentInsideTheExpression()
    {
        // "(Of String)" is parentheses, so the balanced reader takes it whole.
        // The C# equivalent would have to be wrapped in "@(...)".
        Assert.Contains(
            "Write(items.Cast(Of String)().Count)",
            Body("<p>@items.Cast(Of String)().Count</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsAGenericTypeInsideACast()
    {
        Assert.Contains(
            "Write(DirectCast(o, IEnumerable(Of Integer)).Count)",
            Body("<p>@DirectCast(o, IEnumerable(Of Integer)).Count</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsALessThanInsideParentheses()
    {
        // Balanced, so the "<" is an operator rather than the start of a tag.
        Assert.Contains(
            "Write(list.Where(Function(x) x.Age < 30).Count)",
            Body("<p>@list.Where(Function(x) x.Age < 30).Count</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EndsAtABareLessThan()
    {
        // Unbalanced, so markup resumes — the same rule Razor uses.
        var body = Body("<p>@a < b</p>");

        Assert.Contains("Write(a)", body, StringComparison.Ordinal);
        Assert.Contains(@"WriteLiteral("" < b</p>"")", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheConditionalOperatorAsAnExpression()
    {
        // "@If(a, b, c)" is Visual Basic's ternary. It used to be read as an
        // If statement, which opened a block that never closed and swallowed
        // the rest of the line.
        Assert.Contains(
            "Write(If(a < b, 1, 2))",
            Body("<p>@If(a < b, 1, 2)</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsTheNullCoalescingOperatorAsAnExpression()
    {
        Assert.Contains(
            "Write(If(name, \"unknown\"))",
            Body("<p>@If(name, \"unknown\")</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void StillReadsIfAsAStatementWhenItOpensABlock()
    {
        // The space is the whole difference: "If (" is a parenthesised
        // condition on a statement, "If(" is the operator.
        var body = Body("""
            @If ready Then
            <p>yes</p>
            @End If
            """);

        Assert.Contains("If ready Then", body, StringComparison.Ordinal);
        Assert.Contains("End If", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAParenthesisedConditionAsAStatement()
    {
        var body = Body("""
            @If (ready) Then
            <p>yes</p>
            @End If
            """);

        Assert.Contains("If (ready) Then", body, StringComparison.Ordinal);
        Assert.Contains("End If", body, StringComparison.Ordinal);
    }

    [Fact]
    public void KeepsTheSpaceAfterNew()
    {
        // A space ends a member chain, and "New" requires one. The expression
        // used to stop at the word itself, writing the rest as markup.
        Assert.Contains(
            "Write(New List(Of String)().Count)",
            Body("<p>@New List(Of String)().Count</p>"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotTreatAWordStartingWithNewAsTheKeyword()
    {
        // "Newsletter" begins with "New" but is one identifier.
        Assert.Contains(
            "Write(Newsletter.Title)",
            Body("<p>@Newsletter.Title</p>"),
            StringComparison.Ordinal);
    }
}
