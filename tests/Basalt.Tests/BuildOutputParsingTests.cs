using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The output samples used here are copied from real runs of
/// "dotnet build -v normal" on .NET 10, including the indentation and node
/// prefixes that MSBuild inserts.
/// </summary>
public class BuildOutputParsingTests
{
    [Fact]
    public void RiconosceUnErroreVisualBasicConIndentazioneEPrefissoDiNodo()
    {
        const string output = """
             1>/percorso/Program.vb(4,27): error BC30451: 'y' non è dichiarato. [/percorso/VbProbe.vbproj]
            """;

        var diagnostics = MsBuildBuildService.ParseDiagnostics(output);

        var d = Assert.Single(diagnostics);
        Assert.Equal("BC30451", d.Id);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Equal("/percorso/Program.vb", d.FilePath);
        Assert.Equal(4, d.Line);
        Assert.Equal(27, d.Column);
        Assert.Equal("'y' non è dichiarato.", d.Message);
    }

    [Fact]
    public void RiconosceUnErroreCSharpSenzaPrefisso()
    {
        const string output =
            "/percorso/Program.cs(10,5): error CS0103: The name 'foo' does not exist [/percorso/App.csproj]";

        var d = Assert.Single(MsBuildBuildService.ParseDiagnostics(output));
        Assert.Equal("CS0103", d.Id);
        Assert.Equal(10, d.Line);
        Assert.Equal("/percorso/Program.cs", d.FilePath);
    }

    [Fact]
    public void DistingueGliAvvisiDagliErrori()
    {
        const string output =
            "/percorso/Program.cs(3,9): warning CS0219: The variable 'x' is assigned but never used [/p/App.csproj]";

        var d = Assert.Single(MsBuildBuildService.ParseDiagnostics(output));
        Assert.Equal(DiagnosticSeverity.Warning, d.Severity);
    }

    [Fact]
    public void DeduplicaLaStessaDiagnosticaRipetutaDaPiuTarget()
    {
        // MSBuild re-emits the same line for every target that propagates it.
        const string output = """
            /percorso/Program.vb(4,27): error BC30451: 'y' non è dichiarato. [/percorso/VbProbe.vbproj]
             1>/percorso/Program.vb(4,27): error BC30451: 'y' non è dichiarato. [/percorso/VbProbe.vbproj]
            """;

        Assert.Single(MsBuildBuildService.ParseDiagnostics(output));
    }

    [Fact]
    public void RiconosceDiagnosticheSenzaPosizioneComeGliErroriNuGet()
    {
        const string output =
            "/percorso/App.csproj : error NU1605: Rilevato downgrade del pacchetto [/percorso/App.sln]";

        var d = Assert.Single(MsBuildBuildService.ParseDiagnostics(output));
        Assert.Equal("NU1605", d.Id);
        Assert.Null(d.FilePath);
    }

    [Fact]
    public void IgnoraLaRigaDiComandoDelCompilatoreCheContieneLaParolaError()
    {
        // vbc/csc are invoked with /errorreport:prompt: that is not a diagnostic.
        const string output =
            "         /usr/local/share/dotnet/sdk/10.0.100/Roslyn/bincore/vbc /noconfig /errorreport:prompt /optionstrict:custom";

        Assert.Empty(MsBuildBuildService.ParseDiagnostics(output));
    }
}
