using Basalt.Workspace.Debugging;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic expressions from a watch, a hover or the Immediate window, turned into the C# netcoredbg
/// evaluates. Before, every one of these reached the evaluator as VB and came back "not available".
/// </summary>
public class VbExpressionTranslatorTests
{
    private static string First(string vb) => VbExpressionTranslator.Candidates(vb)[0];

    private static IReadOnlyList<string> All(string vb) => VbExpressionTranslator.Candidates(vb);

    [Theory]
    [InlineData("total", "total")]
    [InlineData("total Mod 4", "total % 4")]
    [InlineData("total <> 2", "total != 2")]
    [InlineData("total = 2", "total == 2")]
    [InlineData("total > 1 AndAlso total < 10", "(total > 1) && (total < 10)")]
    [InlineData("a OrElse b", "a || b")]
    [InlineData("a And b", "a & b")]
    [InlineData("a Or b", "a | b")]
    [InlineData("a Xor b", "a ^ b")]
    [InlineData("-count", "-count")]
    public void TranslatesOperators(string vb, string csharp) => Assert.Equal(csharp, First(vb));

    [Fact]
    public void DividesWholeNumbersAsVbDoesEvenForDoubles() =>
        // 7.5 \ 2 is 4 \ 2 in VB, 2; "/" on a Double would give 3.75.
        Assert.Equal("System.Convert.ToInt64(a) / System.Convert.ToInt64(b)", First("a \\ b"));

    [Fact]
    public void KeepsACastOperandTogetherBeforeAMember() =>
        Assert.Equal("(a + b).ToString()", First("CObj(a + b).ToString()"));

    [Fact]
    public void ReadsDictionaryAccessAsTheDefaultProperty() =>
        Assert.Equal("row[\"Name\"]", First("row!Name"));

    [Fact]
    public void ConvertsTrueToMinusOneAsVbDoes() =>
        Assert.Equal("System.Convert.ToInt32(-1)", First("CInt(True)"));

    [Fact]
    public void LeavesTryCastUntranslated() =>
        Assert.Equal(["TryCast(o, String)"], All("TryCast(o, String)"));

    [Fact]
    public void ReadsADirectCastAsItsOperandKeptTogether() =>
        Assert.Equal("(a + b).ToString()", First("DirectCast(a + b, Object).ToString()"));

    [Fact]
    public void KnowsWhichReadingsCallSomething()
    {
        Assert.True(VbExpressionTranslator.Calls("!(calc.Compute(n))"));
        Assert.False(VbExpressionTranslator.Calls("Name.get_Length()"));
        Assert.True(VbExpressionTranslator.Calls("Name.get_Chars(1)"));
        Assert.False(VbExpressionTranslator.Calls("numbers[1] * (a + b)"));
        Assert.False(VbExpressionTranslator.Calls("\"f(x)\" + total"));
    }

    [Fact]
    public void DividesAsADoubleAsVbDoes() =>
        // "/" gives 1.5 for 3 / 2 in VB; C# on two integers would give 1.
        Assert.Equal("1.0 * count / 2", First("count / 2"));

    [Fact]
    public void JoinsAsTextWhateverTheOperands() =>
        Assert.Equal("\"\" + \"t=\" + total", First("\"t=\" & total"));

    [Fact]
    public void RaisesToAPowerThroughMath() =>
        Assert.Equal("System.Math.Pow(System.Convert.ToDouble(count), System.Convert.ToDouble(2))", First("count ^ 2"));

    [Fact]
    public void KeepsWhatVbMeansWhereThePrecedenceDiffers() =>
        // Not binds looser than = in VB: "Not a = b" is Not (a = b).
        Assert.Equal("!(a == b)", First("Not a = b"));

    [Fact]
    public void TriesBothNegationsForNot()
    {
        var candidates = All("Not found");

        Assert.Equal("!found", candidates[0]);
        Assert.Contains("~found", candidates);
    }

    [Fact]
    public void WritesMeAsThisAndMyBaseAsBase()
    {
        Assert.Equal("this.Name", First("Me.Name"));
        Assert.Equal("base.ToString()", First("MyBase.ToString()"));
    }

    [Fact]
    public void ReadsAPropertyThenItsGetter()
    {
        // netcoredbg reads a string's Length only through get_Length().
        Assert.Equal(["Name.Length", "Name.get_Length()"], All("Name.Length"));
    }

    [Fact]
    public void TriesTheIndexBeforeTheCallBecauseReadingItRunsNothing()
    {
        Assert.Equal(["numbers[1]", "numbers(1)"], All("numbers(1)"));
        Assert.Equal(["dt.Rows[0]", "dt.Rows(0)"], All("dt.Rows(0)"));
        Assert.Equal(["this.items[0]", "this.items(0)"], All("Me.items(0)"));
    }

    [Fact]
    public void TriesEveryReadingThatRunsNothingBeforeAnyThatCalls()
    {
        // A refused reading that calls something ends the attempts, so both negations come before it.
        var candidates = All("Not obj.Flags");

        Assert.Contains("~obj.Flags", candidates);
        Assert.True(candidates.TakeWhile(c => !VbExpressionTranslator.Calls(c)).Count() == candidates.Count,
            "A getter counted as a call: " + string.Join(" | ", candidates));
        Assert.True(candidates.ToList().IndexOf("!(calc.Compute(n))") < 0);
    }

    [Fact]
    public void CallsFirstWhenTheTargetAlreadyCallsSomething()
    {
        // The index would run Trim() too; refused, it would end the attempts before the call was sent.
        Assert.Equal("(s.Trim()).Substring(1)", First("s.Trim().Substring(1)"));
        Assert.Equal("(name.Substring(0, 3)).ToUpper()", First("name.Substring(0, 3).ToUpper()"));
    }

    [Fact]
    public void KeepsBothNegationsOfTheLikeliestOperandUnderTheCap()
    {
        var candidates = All("Not a.b.c.d");

        Assert.Contains("!a.b.c.d", candidates);
        Assert.Contains("~a.b.c.d", candidates);
    }

    [Fact]
    public void ConvertsTrueToTheLargestValueOfAnUnsignedType() =>
        Assert.Equal("System.Convert.ToByte(255)", First("CByte(True)"));

    [Fact]
    public void TranslatesInsideAnIndexAndAroundIt() =>
        Assert.Equal("(numbers[1]) * 10", First("numbers(1) * 10"));

    [Fact]
    public void CallsAMemberWithoutArgumentsWithoutTryingAGetter() =>
        Assert.Equal(["Name.ToUpper()"], All("Name.ToUpper()"));

    [Fact]
    public void NamesPredefinedTypesInFull() =>
        Assert.Contains("!(System.String.IsNullOrEmpty(Name))", All("Not String.IsNullOrEmpty(Name)"));

    [Fact]
    public void ConvertsThroughSystemConvert()
    {
        Assert.Equal("System.Convert.ToString(total)", First("CStr(total)"));
        Assert.Equal("System.Convert.ToInt32(price)", First("CInt(price)"));
        Assert.Equal("System.Convert.ToDouble(total)", First("CDbl(total)"));
    }

    [Fact]
    public void ReadsLenAsTheLength() =>
        Assert.Equal(["Name.get_Length()", "Name.Length"], All("Len(Name)"));

    [Fact]
    public void TriesTheFormsOfIsNothingTheEvaluatorMayTake()
    {
        Assert.Equal(
            ["Name == null", "System.Object.ReferenceEquals(Name, null)", "System.Object.Equals(Name, null)"],
            All("Name Is Nothing"));
        Assert.Equal("Name != null", First("Name IsNot Nothing"));
    }

    [Theory]
    [InlineData("Nothing", "null")]
    [InlineData("True", "true")]
    [InlineData("10L", "10L")]
    [InlineData("&HFF", "255")]
    [InlineData("1.5D", "1.5m")]
    [InlineData("1.5F", "1.5f")]
    [InlineData("3.0", "3.0")]
    [InlineData("\"a\"c", "'a'")]
    [InlineData("\"say \"\"hi\"\"\"", "\"say \\\"hi\\\"\"")]
    [InlineData("\"C:\\temp\"", "\"C:\\\\temp\"")]
    public void WritesLiteralsAsCSharpReadsThem(string vb, string csharp) => Assert.Equal(csharp, First(vb));

    [Fact]
    public void WritesIfAsAConditional() =>
        Assert.Equal("((total > 1) ? \"big\" : \"small\")", First("If(total > 1, \"big\", \"small\")"));

    [Fact]
    public void EscapesANameThatIsACSharpKeyword() => Assert.Equal("@string", First("[string]"));

    [Theory]
    [InlineData("i == 4")]
    [InlineData("a = = b")]
    [InlineData("#1/1/2020#")]
    [InlineData("TypeOf x Is String")]
    [InlineData(".Name")]
    public void PassesThroughWhatItCannotTranslate(string text) => Assert.Equal([text], All(text));

    [Fact]
    public void ReturnsNothingForAnEmptyExpression() => Assert.Empty(All("   "));

    [Fact]
    public void NeverReturnsMoreThanTheCap() =>
        Assert.True(All("a(1)(2)(3)(4).B.C.D").Count <= VbExpressionTranslator.MaxCandidates);
}
