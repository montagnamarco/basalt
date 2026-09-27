using Basalt.Core.Model;
using Basalt.Extensibility;
using Basalt.Workspace;
using Basalt.Workspace.Languages;
using Basalt.Workspace.Web;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using ExtSeverity = Basalt.Extensibility.DiagnosticSeverity;
using CoreSeverity = Basalt.Core.Model.DiagnosticSeverity;

namespace Basalt.Tests;

public sealed class VbPageLanguageServiceTests
{
    private static LanguageDocument Document(string text) => new("/Pages/Test.vbpage", text);

    private static (SyntaxTree Tree, SemanticModel Model, VisualBasicCompilation Compilation) Compile(string code)
    {
        var tree = VisualBasicSyntaxTree.ParseText(code, path: "__BasaltGeneratedView.vb");
        var compilation = VisualBasicCompilation.Create("PageLanguageService", [tree],
            VbPageSourceMappingTests.References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithGlobalImports(new[] { GlobalImport.Parse("Microsoft.VisualBasic") }));
        return (tree, compilation.GetSemanticModel(tree), compilation);
    }

    private static Task<IReadOnlyList<IdeDiagnostic>> DiagnosticsAsync(string code, CancellationToken ct)
    {
        var (_, _, compilation) = Compile(code);
        return Task.FromResult<IReadOnlyList<IdeDiagnostic>>(compilation.GetDiagnostics(ct)
            .Where(problem => problem.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(problem =>
            {
                var at = problem.Location.GetLineSpan().StartLinePosition;
                return new IdeDiagnostic(problem.Id, problem.GetMessage(), CoreSeverity.Error,
                    null, at.Line + 1, at.Character + 1);
            }).ToArray());
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task CompilerDiagnosticsLandOnTheExactPageTokensAcrossCodeMembersAndExpressions(string newline)
    {
        var text = string.Join(newline,
            "<h1>header 😀</h1>",
            "<%@ Import System.Text %>",
            "<%",
            "    Dim value = missingCode",
            "%>",
            "<%!",
            "Private Function Broken() As Integer",
            "    Return missingMember",
            "End Function",
            "%>",
            "<p>😀 <%= missingExpression %></p>");
        var diagnostics = await new VbHtmlDiagnosticProvider(DiagnosticsAsync).GetDiagnosticsAsync(Document(text));

        foreach (var token in new[] { "missingCode", "missingMember", "missingExpression" })
        {
            var expected = Position(text, text.IndexOf(token, StringComparison.Ordinal));
            Assert.Contains(diagnostics, problem => problem.Id == "BC30451" &&
                problem.Range.Start == expected && problem.FilePath == Document(text).FilePath);
        }
        Assert.Equal(3, diagnostics.Count(problem => problem.Severity == ExtSeverity.Error));
    }

    [Fact]
    public async Task PageParserDiagnosticsKeepTheirOwnIdentity()
    {
        var diagnostics = await new VbHtmlDiagnosticProvider(DiagnosticsAsync)
            .GetDiagnosticsAsync(Document("<h1>header</h1>\n<% Dim value = 1"));

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "VBP001" && diagnostic.Range.Start.Line == 2);
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("VBHTML", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<% Dim value = \"hello\"\nvalue.ToU %>", "value.ToU")]
    [InlineData("<%= \"hello\".ToU %>", ".ToU")]
    [InlineData("<%== \"hello\".ToU %>", ".ToU")]
    [InlineData("<%! Private Function Greeting() As String\nReturn \"hello\".ToU\nEnd Function %>", ".ToU")]
    [InlineData("<%= String.Concat(\n    \"hello\".ToU,\n    \"world\") %>", ".ToU")]
    [InlineData("<%= String.Concat(\r\n    \"hello\".ToU,\r\n    \"world\") %>", ".ToU")]
    [InlineData("<%\n    \n%>", "\n    ")]
    [InlineData("<%\n    \nDim value = 1 %>", "\n    ")]
    [InlineData("<% Dim value = 1\n    \n%>", "\n    ")]
    [InlineData("<% Dim value = \"hello\"\nvalue.ToU", ".ToU")]
    public async Task CompletionAsksRealRoslynInEveryPageCodeKind(string body, string caretToken)
    {
        var text = "<h1>header 😀</h1>\n<%@ Import System.Text %>\n" + body;
        var caret = text.IndexOf(caretToken, StringComparison.Ordinal) + caretToken.Length;
        using var language = new RoslynLanguageService();
        var provider = new VbHtmlCompletionProvider(async (code, position, ct) =>
        {
            var items = await language.GetCompletionsAsync("__vbpage_generated.vb", position, code, ct);
            return items.Select(RoslynCompletionProvider.Convert).ToArray();
        });

        var items = await provider.GetCompletionsAsync(Document(text), caret);
        Assert.Contains(items, item => item.DisplayText == (caretToken == ".ToU" || caretToken == "value.ToU" ? "ToUpper" : "Dim"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public async Task DefinitionsReturnToRealLocalAndMemberDeclarations(string newline)
    {
        var text = string.Join(newline,
            "<h1>header 😀</h1>",
            "<%",
            "    Dim number = 3",
            "%>",
            "<%!",
            "Private Function Twice(value As Integer) As Integer",
            "    Return value * 2",
            "End Function",
            "%>",
            "<p>😀 <%= Twice(number) %></p>");
        var provider = new VbHtmlNavigationProvider(new VbHtmlNavigationProvider.Questions
        {
            Definition = async (code, position, ct) =>
            {
                var (tree, model, _) = Compile(code);
                var root = await tree.GetRootAsync(ct);
                var token = root.FindToken(position);
                var identifier = token.Parent!.AncestorsAndSelf().OfType<IdentifierNameSyntax>().First();
                var symbol = model.GetSymbolInfo(identifier, ct).Symbol!;
                var location = symbol.Locations.First(location => location.IsInSource).GetLineSpan();
                return new SourceLocation(location.Path,
                    SourceRange.At(new SourcePosition(location.StartLinePosition.Line + 1,
                        location.StartLinePosition.Character + 1)));
            }
        });
        foreach (var name in new[] { "Twice", "number" })
        {
            var use = text.LastIndexOf(name, StringComparison.Ordinal);
            var definition = await provider.GoToDefinitionAsync(Document(text), use + 1);
            Assert.NotNull(definition);
            Assert.Equal(Document(text).FilePath, definition.FilePath);
            Assert.Equal(Position(text, text.IndexOf(name, StringComparison.Ordinal)), definition.Range.Start);
        }
    }

    [Fact]
    public async Task ReferencesComeBackFromTheCompilerToBothPageUses()
    {
        const string text = "<h1>header 😀</h1>\n<% Dim number = 3\nDim result = Twice(number) %>\n"
            + "<%! Private Function Twice(value As Integer) As Integer\nReturn value * 2\nEnd Function %>\n"
            + "<p>😀 <%= Twice(number) %></p>";
        var provider = new VbHtmlNavigationProvider(new VbHtmlNavigationProvider.Questions
        {
            References = async (code, position, ct) =>
            {
                var (tree, model, _) = Compile(code);
                var root = await tree.GetRootAsync(ct);
                var selected = root.FindToken(position).Parent!.AncestorsAndSelf()
                    .OfType<IdentifierNameSyntax>().First();
                var symbol = model.GetSymbolInfo(selected, ct).Symbol;
                IReadOnlyList<SourceLocation> locations = root.DescendantNodes().OfType<IdentifierNameSyntax>()
                    .Where(identifier => SymbolEqualityComparer.Default.Equals(
                        model.GetSymbolInfo(identifier, ct).Symbol, symbol))
                    .Select(identifier =>
                    {
                        var at = identifier.GetLocation().GetLineSpan();
                        return new SourceLocation(at.Path, SourceRange.At(new SourcePosition(
                            at.StartLinePosition.Line + 1, at.StartLinePosition.Character + 1)));
                    }).ToArray();
                return locations;
            }
        });

        var references = await provider.FindReferencesAsync(Document(text), text.LastIndexOf("Twice", StringComparison.Ordinal) + 1);
        Assert.Equal(2, references.Count);
        var first = text.IndexOf("Twice(number)", StringComparison.Ordinal);
        var last = text.LastIndexOf("Twice(number)", StringComparison.Ordinal);
        Assert.Contains(references, reference => reference.Range.Start == Position(text, first));
        Assert.Contains(references, reference => reference.Range.Start == Position(text, last));
        Assert.All(references, reference => Assert.Equal(Document(text).FilePath, reference.FilePath));
    }

    [Fact]
    public async Task MarkupRemainsHtmlAndNeverAsksTheVbCompiler()
    {
        var provider = new VbHtmlCompletionProvider((code, position, ct) =>
            throw new InvalidOperationException("Markup was sent to the VB compiler."));
        var items = await provider.GetCompletionsAsync(Document("<di"), 3);
        Assert.Contains(items, item => item.DisplayText == "div");
    }

    private static SourcePosition Position(string text, int offset)
    {
        var at = SourceText.From(text).Lines.GetLinePosition(offset);
        return new SourcePosition(at.Line + 1, at.Character + 1);
    }
}
