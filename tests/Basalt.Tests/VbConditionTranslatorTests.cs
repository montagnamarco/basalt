using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// Turning a breakpoint condition written in VB into the C# the debugger's
/// evaluator understands.
/// </summary>
public class VbConditionTranslatorTests
{
    private static string Translate(string condition) =>
        VbConditionTranslator.ToEvaluatorSyntax(condition);

    [Fact]
    public void TurnsAComparisonIntoTheOneCSharpUses()
    {
        // The whole point: as "i = 4" the evaluator reads an assignment, which
        // is always true, so the breakpoint stops on the first pass.
        Assert.Equal("i == 4", Translate("i = 4"));
    }

    [Fact]
    public void TurnsNotEqualIntoItsCSharpForm()
    {
        Assert.Equal("name != 4", Translate("name <> 4"));
    }

    [Theory]
    [InlineData("i <= 4")]
    [InlineData("i >= 4")]
    [InlineData("i < 4")]
    [InlineData("i > 4")]
    public void LeavesComparisonsThatAlreadyMatchAlone(string condition)
    {
        Assert.Equal(condition, Translate(condition));
    }

    [Fact]
    public void DoesNotDoubleUpAnEqualityThatIsAlreadyCSharp()
    {
        Assert.Equal("i == 4", Translate("i == 4"));
    }

    [Fact]
    public void TranslatesTheLogicalOperators()
    {
        Assert.Equal("a == 1 && b == 2", Translate("a = 1 AndAlso b = 2"));
        Assert.Equal("a == 1 || b == 2", Translate("a = 1 OrElse b = 2"));
        Assert.Equal("a == 1 && b == 2", Translate("a = 1 And b = 2"));
        Assert.Equal("a == 1 || b == 2", Translate("a = 1 Or b = 2"));
    }

    [Fact]
    public void TranslatesRegardlessOfHowTheKeywordIsCased()
    {
        // VB is case-insensitive, so all of these are the same keyword.
        Assert.Equal("a == 1 && b == 2", Translate("a = 1 andalso b = 2"));
        Assert.Equal("a == 1 && b == 2", Translate("a = 1 ANDALSO b = 2"));
    }

    [Fact]
    public void TranslatesNothingIntoNull()
    {
        Assert.Equal("customer == null", Translate("customer Is Nothing"));
    }

    [Fact]
    public void TranslatesIsNot()
    {
        Assert.Equal("customer != null", Translate("customer IsNot Nothing"));
    }

    [Fact]
    public void TranslatesNot()
    {
        Assert.Equal("! found", Translate("Not found"));
    }

    [Fact]
    public void TranslatesMod()
    {
        Assert.Equal("i % 2 == 0", Translate("i Mod 2 = 0"));
    }

    [Fact]
    public void LeavesAnIdentifierThatContainsAKeywordAlone()
    {
        // "android" contains "and", and "notes" contains "not": replacing
        // inside words would corrupt perfectly good variable names.
        Assert.Equal("android == 1", Translate("android = 1"));
        Assert.Equal("notes == 1", Translate("notes = 1"));
        Assert.Equal("nothingness == 1", Translate("nothingness = 1"));
    }

    [Fact]
    public void LeavesTheInsideOfAStringAlone()
    {
        // What is between quotes is data: an "=" there is a character to
        // compare against, not an operator to translate.
        Assert.Equal("name == \"a = b\"", Translate("name = \"a = b\""));
    }

    [Fact]
    public void KeepsAnEscapedQuoteInsideAString()
    {
        // "" inside a VB literal is one quote character, not the end of it.
        Assert.Equal("name == \"say \"\"hi\"\"\"", Translate("name = \"say \"\"hi\"\"\""));
    }

    [Fact]
    public void LeavesAKeywordInsideAStringAlone()
    {
        Assert.Equal("tag == \"and\"", Translate("tag = \"and\""));
    }

    [Fact]
    public void HandsBackAnEmptyConditionUnchanged()
    {
        Assert.Equal("", Translate(""));
    }

    [Fact]
    public void LeavesAMethodCallAlone()
    {
        Assert.Equal("name.Length == 3", Translate("name.Length = 3"));
    }
}
