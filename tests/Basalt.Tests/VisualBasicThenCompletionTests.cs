using Basalt.Core.Model;
using Basalt.Core.Services;
using Basalt.Workspace;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Tests;

public sealed class VisualBasicThenCompletionTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    private static string Wrap(string header) => header.StartsWith("ElseIf", StringComparison.Ordinal)
        ? $"Class C\n    Sub M()\n        If first Then\n        {header}\n        End If\n    End Sub\nEnd Class"
        : $"Class C\n    Sub M()\n        {header}\n        End If\n    End Sub\nEnd Class";

    [Theory]
    [InlineData("If flag")]
    [InlineData("ElseIf flag")]
    public async Task RoslynRepresentsOmittedThenAsAnOptionalAbsentToken(string header)
    {
        var root = await VisualBasicSyntaxTree.ParseText(Wrap(header)).GetRootAsync();
        var node = root.DescendantNodes().Last(candidate => candidate is IfStatementSyntax or ElseIfStatementSyntax);
        var then = node is IfStatementSyntax conditional ? conditional.ThenKeyword : ((ElseIfStatementSyntax)node).ThenKeyword;
        var condition = node is IfStatementSyntax statement ? statement.Condition : ((ElseIfStatementSyntax)node).Condition;

        Assert.Equal(0, then.RawKind);
        Assert.False(condition.ContainsDiagnostics);
    }

    [Theory]
    [InlineData("If flag", "If flag Then")]
    [InlineData("If value > 0", "If value > 0 Then")]
    [InlineData("If value = \"Then\"", "If value = \"Then\" Then")]
    [InlineData("If Check(value)", "If Check(value) Then")]
    [InlineData("If flag ' keep this Then comment", "If flag Then ' keep this Then comment")]
    [InlineData("ElseIf flag", "ElseIf flag Then")]
    [InlineData("ElseIf value > 0 ' keep", "ElseIf value > 0 Then ' keep")]
    public async Task CompletesOnlyTheFinishedConditionalHeader(string header, string expectedHeader)
    {
        var text = Wrap(header);
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Replace(header, expectedHeader, StringComparison.Ordinal), result.Text);
        Assert.Equal(caret + expectedHeader.Length - header.Length, result.Caret);
        Assert.True(result.Changed);
    }

    [Fact]
    public async Task AppliesSpacingAndKeywordCasingAfterCompletingThen()
    {
        const string header = "if x=0";
        var text = Wrap(header);
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Replace(header, "If x = 0 Then", StringComparison.Ordinal), result.Text);
        Assert.Equal(caret + 7, result.Caret);
    }

    [Theory]
    [InlineData("If flag Then")]
    [InlineData("If flag Then Work() Else Other()")]
    [InlineData("If flag AndAlso")]
    [InlineData("If value =")]
    [InlineData("If (value > 0")]
    [InlineData("If \"unfinished")]
    [InlineData("If")]
    [InlineData("ElseIf flag Then")]
    [InlineData("ElseIf value =")]
    [InlineData("' If flag")]
    [InlineData("Dim result = If(flag, 1, 0)")]
    public async Task DoesNotCompleteExistingOrIncompleteOrUnrelatedSyntax(string header)
    {
        var text = Wrap(header);
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    [Theory]
    [InlineData("If first AndAlso _\n            second", "If first AndAlso _")]
    [InlineData("If first AndAlso\n            second", "If first AndAlso")]
    [InlineData("If flag : Work()", "If flag : Work()")]
    public async Task PreservesContinuedAndAmbiguousHeaders(string header, string caretLine)
    {
        var text = Wrap(header);
        var caret = text.IndexOf(caretLine, StringComparison.Ordinal) + caretLine.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task CompletesAnEofHeaderAndPreservesLineEndings(string newline)
    {
        var text = $"Class C{newline}    Sub M(){newline}        If flag";

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, text.Length);

        Assert.Equal(text + " Then", result.Text);
        Assert.Equal(text.Length + 5, result.Caret);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task OrdinaryLineFormattingPreservesContinuationDelimiters(string newline)
    {
        const string firstLine = "If first AndAlso _";
        var text = Wrap(firstLine + "\n            second").Replace("\n", newline, StringComparison.Ordinal);
        var caret = text.IndexOf(firstLine, StringComparison.Ordinal) + firstLine.Length;

        var result = await _service.FormatLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    [Fact]
    public async Task OrdinaryTypingDoesNotFinishACondition()
    {
        const string text = "Class C\n    Sub M()\n        If flag ";
        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, text.Length);

        Assert.Equal(text, result.Text);
        Assert.Equal(text.Length, result.Caret);
    }

    [Theory]
    [InlineData("If x AndAlso y", "If x ")]
    [InlineData("If value > 0", "If va")]
    public async Task DoesNotFinishUnconsumedConditionText(string header, string beforeCaret)
    {
        var text = Wrap(header);
        var caret = text.IndexOf(header, StringComparison.Ordinal) + beforeCaret.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    [Fact]
    public async Task NonVisualBasicCompletionKeepsTheExistingBehavior()
    {
        const string text = "if (flag)";
        IFormattingService service = _service;
        var result = await service.CompleteLineAsync(text, SourceLanguage.Unknown, text.Length);

        Assert.Equal(text, result.Text);
        Assert.Equal(text.Length, result.Caret);
        Assert.False(result.Changed);
    }

    public void Dispose() => _service.Dispose();
}
