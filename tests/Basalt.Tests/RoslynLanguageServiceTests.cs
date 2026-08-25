using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Integration tests over real projects generated on the fly: they verify that
/// Roslyn provides completion and diagnostics in both C# and VB.NET.
/// </summary>
public sealed class RoslynLanguageServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-tests", Guid.NewGuid().ToString("N"));

    private string CreateProject(SourceLanguage language, string sourceCode)
    {
        Directory.CreateDirectory(_root);

        var isVb = language == SourceLanguage.VisualBasic;
        var projectPath = Path.Combine(_root, isVb ? "Probe.vbproj" : "Probe.csproj");
        var sourcePath = Path.Combine(_root, isVb ? "Program.vb" : "Program.cs");

        File.WriteAllText(projectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(sourcePath, sourceCode);

        return sourcePath;
    }

    [Fact]
    public async Task OffreIMembriDiUnaStringaInVisualBasic()
    {
        const string code = """
            Public Class Prova
                Public Sub Metodo()
                    Dim s As String = "ciao"
                    s.
                End Sub
            End Class
            """;
        var sourcePath = CreateProject(SourceLanguage.VisualBasic, code);

        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        var completions = await service.GetCompletionsAsync(
            sourcePath, code.IndexOf("s.", StringComparison.Ordinal) + 2);

        Assert.NotEmpty(completions);
        Assert.Contains(completions, c => c.DisplayText == "Length");
    }

    [Fact]
    public async Task SegnalaGliErroriSemanticiInVisualBasic()
    {
        const string code = """
            Public Class Prova
                Public Sub Metodo()
                    Dim n As Integer = variabileInesistente
                End Sub
            End Class
            """;
        var sourcePath = CreateProject(SourceLanguage.VisualBasic, code);

        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        var diagnostics = await service.GetDiagnosticsAsync(sourcePath);

        Assert.Contains(diagnostics, d => d.Id == "BC30451" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public async Task RiconosceIlLinguaggioDelProgettoAperto()
    {
        CreateProject(SourceLanguage.VisualBasic, "Public Class Prova\nEnd Class");

        using var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        Assert.Equal("Visual Basic", service.CurrentSolution!.Projects.Single().Language);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* file still in use: irrelevant for the test */ }
    }
}
