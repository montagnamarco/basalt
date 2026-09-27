using Basalt.Core.Model;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Tests;

public sealed class VisualBasicContinuationIndentationTests : IDisposable
{
    private readonly RoslynFormattingService _service = new();

    [Theory]
    [InlineData("Dim total = 1 + _", "2", 12)]
    [InlineData("Dim total = 1 +", "2", 12)]
    [InlineData("Dim total = Add(", "1, 2)", 12)]
    [InlineData("Dim total = Add(1,", "2)", 24)]
    public async Task MeasuresIndentationFromARealContinuationExpression(string header, string continuation, int expected)
    {
        var text = $"Class C\n    Sub M()\n        {header}\n        Dim nextValue = 0\n    End Sub\nEnd Class";
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        var completed = text.Insert(caret, "\n" + continuation);
        var root = await VisualBasicSyntaxTree.ParseText(completed).GetRootAsync();
        var statement = root.FindToken(caret + 1).Parent?.FirstAncestorOrSelf<StatementSyntax>();
        var declaration = Assert.IsType<LocalDeclarationStatementSyntax>(statement);
        Assert.True(declaration.Span.Start < caret);
        Assert.False(declaration.ContainsDiagnostics);

        Assert.Equal(expected, await _service.GetIndentationAsync(text, SourceLanguage.VisualBasic, caret));
    }

    [Theory]
    [InlineData("\n", "Dim total = 1 + _")]
    [InlineData("\r\n", "Dim total = 1 + _")]
    [InlineData("\n", "Dim total = 1 +")]
    [InlineData("\r\n", "Dim total = 1 +")]
    [InlineData("\n", "Dim total = Add(")]
    [InlineData("\r\n", "Dim total = Add(")]
    public async Task SupportsBothLineDelimitersAtTheFinishedHeader(string newline, string header)
    {
        var text = $"Class C{newline}    Sub M(){newline}        {header}{newline}        Dim nextValue = 0{newline}    End Sub{newline}End Class";
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        Assert.Equal(12, await _service.GetIndentationAsync(text, SourceLanguage.VisualBasic, caret));
    }

    [Theory]
    [InlineData("Dim total = 1", 8)]
    [InlineData("If True Then", 12)]
    [InlineData("Dim total = Add(1, 2)", 8)]
    public async Task CompletedStatementsAndBlocksKeepTheirExistingIndentation(string header, int expected)
    {
        var text = $"Class C\n    Sub M()\n        {header}\n    End Sub\nEnd Class";
        var caret = text.IndexOf(header, StringComparison.Ordinal) + header.Length;

        Assert.Equal(expected, await _service.GetIndentationAsync(text, SourceLanguage.VisualBasic, caret));
    }

    public void Dispose() => _service.Dispose();
}
