using Basalt.Core.Model;
using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Runtime;
using Basalt.Workspace.Web;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using IdeSeverity = Basalt.Core.Model.DiagnosticSeverity;
using ViewDiagnostic = Basalt.Extensibility.Diagnostic;

namespace Basalt.Tests;

/// <summary>
/// What the Visual Basic compiler finds in a view, reported on the view.
///
/// The parser only sees structure: it accepts <c>@Model.Naem</c> because that
/// is a well-formed expression. Only the compiler knows the property does not
/// exist, and its complaint arrives against generated code the author never
/// wrote. These tests check the complaint travels back.
/// </summary>
public class VbHtmlSemanticDiagnosticTests
{
    /// <summary>
    /// Everything the generated code needs to resolve: the runtime, and the
    /// assembly holding the view base class it inherits from. Without the
    /// latter every call to a base member reads as an undefined name, and
    /// those errors land inside mapped regions — indistinguishable, to the
    /// map, from a real mistake by the author.
    /// </summary>
    private static readonly MetadataReference[] References =
    [
        .. (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator)
            .Where(path => path.Length > 0)
            .Select(path => MetadataReference.CreateFromFile(path)),
        MetadataReference.CreateFromFile(typeof(VbHtmlView).Assembly.Location),
    ];

    /// <summary>
    /// Really compiles the generated Visual Basic. A stub here would pass
    /// while the mapping was broken — the failure mode this suite exists for.
    /// </summary>
    private static Task<IReadOnlyList<IdeDiagnostic>> CompileAsync(
        string code, CancellationToken ct)
    {
        var tree = VisualBasicSyntaxTree.ParseText(code, cancellationToken: ct);

        var compilation = VisualBasicCompilation.Create(
            "Generated",
            [tree],
            References,
            new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var results = compilation
            .GetDiagnostics(ct)
            .Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            .Select(d =>
            {
                var position = d.Location.GetLineSpan().StartLinePosition;

                return new IdeDiagnostic(
                    d.Id,
                    d.GetMessage(),
                    IdeSeverity.Error,
                    null,
                    position.Line + 1,
                    position.Character + 1);
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<IdeDiagnostic>>(results);
    }

    private static async Task<IReadOnlyList<ViewDiagnostic>> CheckAsync(string view) =>
        await new VbHtmlDiagnosticProvider(CompileAsync)
            .GetDiagnosticsAsync(new LanguageDocument("/Views/Index.vbhtml", view));

    [Fact]
    public async Task ReportsAnUndefinedNameOnTheLineThatUsesIt()
    {
        // Line 4 is the only line naming something that does not exist.
        const string view = """
            @Code
                Dim greeting = "hello"
            End Code
            <p>@missing</p>
            """;

        var problem = Assert.Single(await CheckAsync(view));

        Assert.Equal(4, problem.Range.Start.Line);
    }

    [Fact]
    public async Task ReportsAnErrorBelowTheFirstLineOfACodeBlock()
    {
        // The whole block is one mapped region. Matching on the region's first
        // line would place this on line 2, or discard it.
        const string view = """
            @Code
                Dim a = 1
                Dim b = undefinedName
            End Code
            <p>@a</p>
            """;

        var problem = Assert.Single(await CheckAsync(view));

        Assert.Equal(3, problem.Range.Start.Line);
    }

    [Fact]
    public async Task SaysNothingAboutAViewThatCompiles()
    {
        const string view = """
            @Code
                Dim name = "world"
            End Code
            <p>Hello @name</p>
            """;

        Assert.Empty(await CheckAsync(view));
    }

    [Fact]
    public async Task DoesNotReportErrorsInGeneratedScaffolding()
    {
        // Nothing here is wrong, yet the generated file references a base
        // class and helpers this bare compilation has no reference for. Every
        // one of those errors is about code the author never wrote, so none
        // may reach them.
        const string view = "<p>plain markup</p>";

        Assert.Empty(await CheckAsync(view));
    }

    [Fact]
    public async Task StaysQuietWhenTheViewDoesNotParse()
    {
        // A broken template generates wreckage; errors about the wreckage
        // would bury the one real problem.
        const string view = """
            @Code
                Dim a = 1
            <p>unterminated</p>
            """;

        // The unclosed block, and nothing else: no cascade from compiling
        // code the parser could not read.
        var problem = Assert.Single(await CheckAsync(view));

        Assert.Contains("End Code", problem.Message);
    }

    [Fact]
    public async Task WithoutACompilerOnlyStructuralChecksRun()
    {
        const string view = "<p>@missing</p>";

        Assert.Empty(await new VbHtmlDiagnosticProvider()
            .GetDiagnosticsAsync(new LanguageDocument("/Views/Index.vbhtml", view)));
    }
}
