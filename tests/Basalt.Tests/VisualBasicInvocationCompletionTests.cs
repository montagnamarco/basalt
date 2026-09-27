using Basalt.Core.Model;
using Basalt.Workspace;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Tests;

public sealed class VisualBasicInvocationCompletionTests : IDisposable
{
    private readonly AdhocWorkspace _workspace = new();
    private readonly RoslynFormattingService _service = new();

    private static string Wrap(string statement, string declarations = "    Private Sub Work()\n    End Sub") =>
        $"Class C\n    Sub Caller()\n        {statement}\n    End Sub\n{declarations}\nEnd Class";

    private Document CreateDocument(string text) => _workspace
        .AddProject($"Invocation_{Guid.NewGuid():N}", LanguageNames.VisualBasic)
        .WithCompilationOptions(new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
        .WithMetadataReferences([MetadataReference.CreateFromFile(typeof(object).Assembly.Location)])
        .AddDocument("Input.vb", SourceText.From(text));

    private async Task<IReadOnlyList<TextChange>> GetChangesAsync(string text, int caret)
    {
        var source = SourceText.From(text);
        return await VisualBasicInvocationCompleter.GetChangesAsync(
            CreateDocument(text), source.Lines.GetLineFromPosition(caret), caret, CancellationToken.None);
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Me.Work")]
    [InlineData("Call Work")]
    [InlineData("Call Me.Work")]
    [InlineData("System.GC.Collect")]
    public async Task CompilerAcceptsOmittedArgumentlessInvocationParentheses(string statement)
    {
        var document = CreateDocument(Wrap(statement));
        var root = await document.GetSyntaxRootAsync();
        var call = root!.DescendantNodes().OfType<StatementSyntax>()
            .Single(node => node.ToString() == statement);
        var expression = call is CallStatementSyntax explicitCall
            ? explicitCall.Invocation
            : ((ExpressionStatementSyntax)call).Expression;
        Assert.False(expression.ContainsDiagnostics);
        Assert.False(expression is InvocationExpressionSyntax { ArgumentList: not null });
        var model = await document.GetSemanticModelAsync();
        Assert.IsAssignableFrom<IMethodSymbol>(model!.GetSymbolInfo(expression).Symbol);
        var compilation = await document.Project.GetCompilationAsync();
        using var assembly = new MemoryStream();
        var emission = compilation!.Emit(assembly);
        Assert.True(emission.Success, string.Join("\n", emission.Diagnostics));
    }

    [Theory]
    [InlineData("Work", "Work()")]
    [InlineData("Me.Work", "Me.Work()")]
    [InlineData("Call Work", "Call Work()")]
    [InlineData("Call Me.Work", "Call Me.Work()")]
    [InlineData("[Work]", "[Work]()")]
    [InlineData("Work ' keep Work() in this comment", "Work() ' keep Work() in this comment")]
    [InlineData("System.GC.Collect", "System.GC.Collect()")]
    public async Task CompletesResolvedArgumentlessStatementsOnly(string statement, string expected)
    {
        var text = Wrap(statement);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;
        var changes = await GetChangesAsync(text, caret);

        var change = Assert.Single(changes);
        Assert.Equal("()", change.NewText);
        Assert.Equal(text.Replace($"        {statement}", $"        {expected}", StringComparison.Ordinal),
            SourceText.From(text).WithChanges(changes).ToString());
    }

    [Theory]
    [InlineData("Work(Of Integer)")]
    [InlineData("Me.Work(Of Integer)")]
    [InlineData("Call Work(Of Integer)")]
    [InlineData("Call Me.Work(Of Integer)")]
    public async Task CompletesResolvedGenericInvocations(string statement)
    {
        var text = Wrap(statement, "    Private Sub Work(Of T)()\n    End Sub");
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;

        var changes = await GetChangesAsync(text, caret);

        Assert.Equal(text.Insert(caret, "()"), SourceText.From(text).WithChanges(changes).ToString());
    }

    [Theory]
    [InlineData("Dim value = Work")]
    [InlineData("Return Work")]
    [InlineData("Work()")]
    [InlineData("Call Work()")]
    [InlineData("Work(1)")]
    [InlineData("Work(")]
    [InlineData("Me.")]
    [InlineData("Work : Work")]
    [InlineData("Work _")]
    [InlineData("' Work")]
    [InlineData("Dim value = \"Work\"")]
    [InlineData("Missing")]
    [InlineData("System.Console.WriteLine")]
    public async Task LeavesExpressionsExistingCallsAndAmbiguousSyntaxAlone(string statement)
    {
        var text = Wrap(statement);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;

        Assert.Empty(await GetChangesAsync(text, caret));
    }

    [Theory]
    [InlineData("    Private Property Work As Integer")]
    [InlineData("    Private Work As Integer")]
    [InlineData("    Private Sub Work(value As Integer)\n    End Sub")]
    [InlineData("    Private Sub Work(Optional value As Integer = 0)\n    End Sub")]
    [InlineData("    Private Sub Work()\n    End Sub\n    Private Sub Work()\n    End Sub")]
    [InlineData("    Private Sub Work(Of T)()\n    End Sub")]
    public async Task RequiresAnUnambiguousZeroParameterMethod(string declarations)
    {
        var text = Wrap("Work", declarations);
        var caret = text.IndexOf("        Work", StringComparison.Ordinal) + "        Work".Length;

        Assert.Empty(await GetChangesAsync(text, caret));
    }

    [Theory]
    [InlineData("Work", 2)]
    [InlineData("Me.Work", 3)]
    public async Task DoesNotCompleteUnconsumedInvocationText(string statement, int consumed)
    {
        var text = Wrap(statement);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + consumed;

        Assert.Empty(await GetChangesAsync(text, caret));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task FinishedLineConventionsPreserveDelimitersCommentsAndCaret(string newline)
    {
        const string statement = "Work ' keep this comment";
        var text = Wrap(statement).Replace("\n", newline, StringComparison.Ordinal);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Replace(statement, "Work() ' keep this comment", StringComparison.Ordinal), result.Text);
        Assert.Equal(caret + 2, result.Caret);
        Assert.True(result.Changed);
    }

    [Theory]
    [InlineData("Work")]
    [InlineData("Me.Work")]
    [InlineData("Call Work")]
    [InlineData("Call Me.Work")]
    public async Task FinishedLineConventionsCompleteSameFileMethods(string statement)
    {
        var text = Wrap(statement);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text.Insert(caret, "()"), result.Text);
        Assert.Equal(caret + 2, result.Caret);
        Assert.True(result.Changed);
    }

    [Fact]
    public async Task FinishedLineConventionsPreserveUnresolvedFrameworkCalls()
    {
        const string statement = "System.GC.Collect";
        var text = Wrap(statement);
        var caret = text.IndexOf(statement, StringComparison.Ordinal) + statement.Length;

        var result = await _service.CompleteLineAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
        Assert.False(result.Changed);
    }

    [Fact]
    public async Task OrdinaryTypingDoesNotCompleteInvocations()
    {
        const string statement = "Work";
        var text = Wrap(statement);
        var caret = text.IndexOf("        Work", StringComparison.Ordinal) + "        Work".Length;

        var result = await _service.ApplyTypingConventionsAsync(text, SourceLanguage.VisualBasic, caret);

        Assert.Equal(text, result.Text);
        Assert.Equal(caret, result.Caret);
    }

    public void Dispose()
    {
        _service.Dispose();
        _workspace.Dispose();
    }
}
