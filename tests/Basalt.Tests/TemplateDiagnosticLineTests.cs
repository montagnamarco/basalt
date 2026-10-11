using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Workspace.Web;
using CoreDiagnostic = Basalt.Core.Model.IdeDiagnostic;

namespace Basalt.Tests;

/// <summary>
/// A compiler error inside a multi-line code block is reported on the template line the
/// author wrote it on, in a component and in a view.
/// </summary>
/// <remarks>
/// The diagnostic provider mapped the generated position back with the span map alone,
/// which drifts on a code block by the writer's re-indentation: an error on a component's
/// @Code line 8 came back on line 9. It now tries the line mapping first, as
/// go-to-definition does. With the span map alone the component case fails by one line; the
/// view case passed either way and stays as a guard.
/// The compiler is stood in for by an ask that reports BC30456 exactly where the generated
/// code holds "Title.Lenxgth", so the test measures the mapping and nothing else.
/// </remarks>
public sealed class TemplateDiagnosticLineTests
{
    private const string Component =
        "<h1>@Title</h1>\n\n@Code\n    <Global.Microsoft.AspNetCore.Components.Parameter>\n" +
        "    Public Property Title As String\n\n    Private Function Size() As Integer\n" +
        "        Return Title.Lenxgth\n    End Function\nEnd Code\n";

    private const string View =
        "@ModelType String\n<p>hi</p>\n@Code\n    Dim title As String = \"x\"\n\n" +
        "    Dim size = title.Lenxgth\nEnd Code\n";

    [Theory]
    [InlineData("/x/C.vbrazor", Component)]
    [InlineData("/x/V.vbhtml", View)]
    public async Task AnErrorInACodeBlockIsReportedOnItsTemplateLine(string path, string template)
    {
        var typo = template.Contains("Title.Lenxgth", StringComparison.Ordinal) ? "Title.Lenxgth" : "title.Lenxgth";
        var typoAt = template.IndexOf(typo, StringComparison.Ordinal);
        var expectedLine = template[..typoAt].Count(c => c == '\n') + 1;
        var expectedColumn = typoAt - (template.LastIndexOf('\n', Math.Max(0, typoAt - 1)) + 1) + 1;

        var provider = new VbHtmlDiagnosticProvider((generated, _) =>
        {
            var at = generated.IndexOf(typo, StringComparison.Ordinal);
            var line = generated[..at].Count(c => c == '\n') + 1;
            var column = at - (generated.LastIndexOf('\n', Math.Max(0, at - 1)) + 1) + 1;
            IReadOnlyList<CoreDiagnostic> found =
                [new CoreDiagnostic("BC30456", "'Lenxgth' is not a member of 'String'.",
                    Basalt.Core.Model.DiagnosticSeverity.Error, "generated.vb", line, column)];
            return Task.FromResult(found);
        });

        var diagnostics = await provider.GetDiagnosticsAsync(new LanguageDocument(path, template));

        var error = Assert.Single(diagnostics, d => d.Id == "BC30456");
        Assert.Equal(expectedLine, error.Range.Start.Line);
        Assert.Equal(expectedColumn, error.Range.Start.Column);
    }
}
