namespace Basalt.Tests;

/// <summary>
/// The Blazor component generator, run the way the compiler runs it.
/// </summary>
public class ComponentGeneratorTests
{
    private const string Path = @"C:\Site\Components\Open.vbrazor";

    [Fact]
    public void ReportsWhatTheParserFoundWrongOnTheTemplate()
    {
        // An @If never closed. The parser says so; the generator used to drop
        // what it said, and the build failed later on generated code, or
        // rendered half the markup and said nothing at all.
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "<p>start</p>\n@If True Then\n    <p>never closed</p>\n"));

        Assert.Null(outcome.Exception);

        var problem = Assert.Single(outcome.Diagnostics, d => d.Id == "VBRZ013");

        Assert.Equal(Microsoft.CodeAnalysis.DiagnosticSeverity.Error, problem.Severity);
        Assert.Equal(Path, problem.Location.GetLineSpan().Path);
        Assert.Equal(1, problem.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public void WarnsRatherThanFailsOnADirectiveNotSupportedYet()
    {
        // @rendermode is skipped by the parser today. The component still
        // builds, as it always did; failing the build over it would break
        // projects that compiled before the parser's findings were reported.
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "@rendermode InteractiveServer\n<p>hi</p>\n"));

        Assert.Null(outcome.Exception);
        Assert.DoesNotContain(outcome.Diagnostics,
            d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Contains(outcome.Diagnostics, d => d.Id == "VBRZ014");
    }

    [Fact]
    public void TiesTheGeneratedCodeToTheTemplate()
    {
        var outcome = GeneratorRun.Run("VbComponentGenerator",
            (Path, "<p>@(1 + 1)</p>\n"));

        Assert.Null(outcome.Exception);

        var code = Assert.Single(outcome.Sources).Value;

        Assert.Contains($"#ExternalSource(\"{Path}\", 1)", code, StringComparison.Ordinal);
        Assert.Contains($"#ExternalChecksum(\"{Path}\"", code, StringComparison.Ordinal);
    }
}
