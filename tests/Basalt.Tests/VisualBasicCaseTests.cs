using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Visual Basic typing conventions, matching the behaviour of Visual Basic on
/// Windows: keyword casing, spacing around operators, and indentation applied
/// to the line as it is left.
///
/// The whole line is corrected rather than the word last typed. In "end if"
/// the word "end" was finished earlier, so a word-at-a-time approach leaves
/// "end If" — which is exactly the defect these tests were written for.
/// </summary>
public sealed class VisualBasicCaseTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    private Task<Core.Services.TypingFormattingResult> ApplyAsync(string text) =>
        _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, text.Length);

    private const string ClassHeader = "Public Class A\n    Sub M()\n";

    [Fact]
    public async Task CorrectsBothWordsOfEndIf()
    {
        // Reported: "end if" became "End if" — the second word was corrected
        // but the first, finished earlier, was left alone.
        var result = await ApplyAsync($"{ClassHeader}        If x > 0 Then\n        end if");

        Assert.EndsWith("        End If", result.Text);
    }

    [Fact]
    public async Task CorrectsBothWordsOfEndSub()
    {
        var result = await ApplyAsync($"{ClassHeader}        Dim n As Integer = 0\n    end sub");

        Assert.EndsWith("    End Sub", result.Text);
    }

    [Fact]
    public async Task CorrectsEveryKeywordOnTheLineAndSpacesTheOperator()
    {
        // Reported: "if ciao=1 then" kept a lowercase "if" and no spaces.
        var result = await ApplyAsync($"{ClassHeader}        if ciao=1 then");

        Assert.EndsWith("        If ciao = 1 Then", result.Text);
    }

    [Fact]
    public async Task CorrectsADeclarationCompletely()
    {
        var result = await ApplyAsync($"{ClassHeader}        dim n as integer=0");

        Assert.EndsWith("        Dim n As Integer = 0", result.Text);
    }

    [Theory]
    [InlineData("        for each i in items", "        For Each i In items")]
    [InlineData("        while x<10", "        While x < 10")]
    [InlineData("        select case v", "        Select Case v")]
    [InlineData("        return nothing", "        Return Nothing")]
    [InlineData("        dim b as boolean=true", "        Dim b As Boolean = True")]
    [InlineData("        if a andalso b orelse c then", "        If a AndAlso b OrElse c Then")]
    [InlineData("        try", "        Try")]
    public async Task CorrectsCommonConstructs(string typed, string expected)
    {
        var result = await ApplyAsync(ClassHeader + typed);

        Assert.EndsWith(expected, result.Text);
    }

    [Fact]
    public async Task SpacesArithmeticAndComparisonOperators()
    {
        var result = await ApplyAsync($"{ClassHeader}        x=x+1");

        Assert.EndsWith("        x = x + 1", result.Text);
    }

    [Fact]
    public async Task LeavesKeywordsInsideStringsAlone()
    {
        var result = await ApplyAsync($"{ClassHeader}        Dim s As String = \"if then end\"");

        Assert.Contains("\"if then end\"", result.Text);
    }

    [Fact]
    public async Task LeavesKeywordsInsideCommentsAlone()
    {
        var result = await ApplyAsync($"{ClassHeader}        ' remember to add if and then");

        Assert.Contains("' remember to add if and then", result.Text);
    }

    [Fact]
    public async Task LeavesIdentifiersThatMerelyStartWithAKeywordAlone()
    {
        var result = await ApplyAsync($"{ClassHeader}        Dim ifCounter As Integer = 0");

        Assert.Contains("ifCounter", result.Text);
    }

    public void Dispose() => _service.Dispose();
}
