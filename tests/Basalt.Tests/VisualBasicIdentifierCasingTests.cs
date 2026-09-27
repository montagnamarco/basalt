using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Identifier casing against a real project: "console.readline" becomes
/// "Console.ReadLine".
///
/// This needs the semantic model, because only the resolved symbol knows how
/// its own name is spelled. Keyword casing works on a file in isolation;
/// identifiers do not.
/// </summary>
public sealed class VisualBasicIdentifierCasingTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-idcase", Guid.NewGuid().ToString("N"));

    private RoslynLanguageService? _service;

    public VisualBasicIdentifierCasingTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageService Service, string File)> OpenProjectAsync(string code)
    {
        var project = Path.Combine(_root, "Probe.vbproj");
        var source = Path.Combine(_root, "Program.vb");

        await File.WriteAllTextAsync(project, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(source, code);

        _service = new RoslynLanguageService();
        await _service.OpenSolutionAsync(project);
        return (_service, source);
    }

    [Fact]
    public async Task CorrectsFrameworkTypeAndMethodNames()
    {
        const string code = """
            Imports System

            Public Class Probe
                Public Sub M()
                    console.writeline("x")
                End Sub
            End Class
            """;

        var (service, file) = await OpenProjectAsync(code);

        // The line holding the call, zero-based.
        var line = code.Split('\n').ToList().FindIndex(l => l.Contains("console"));
        var changes = await service.GetIdentifierCasingChangesAsync(file, code, line);

        var corrected = Microsoft.CodeAnalysis.Text.SourceText.From(code)
            .WithChanges(changes).ToString();

        Assert.Contains("Console.WriteLine", corrected);
    }

    [Fact]
    public async Task CorrectsTypeNamesInDeclarations()
    {
        const string code = """
            Imports System

            Public Class Probe
                Public Sub M()
                    Dim e As exception = Nothing
                End Sub
            End Class
            """;

        var (service, file) = await OpenProjectAsync(code);

        var line = code.Split('\n').ToList().FindIndex(l => l.Contains("exception"));
        var changes = await service.GetIdentifierCasingChangesAsync(file, code, line);

        var corrected = Microsoft.CodeAnalysis.Text.SourceText.From(code)
            .WithChanges(changes).ToString();

        Assert.Contains("As Exception", corrected);
    }

    [Fact]
    public async Task LeavesUserIdentifiersSpelledAsDeclared()
    {
        // A local the user named themselves keeps their spelling.
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim myValue As Integer = 1
                    myValue = 2
                End Sub
            End Class
            """;

        var (service, file) = await OpenProjectAsync(code);

        var line = code.Split('\n').ToList().FindIndex(l => l.Trim() == "myValue = 2");
        var changes = await service.GetIdentifierCasingChangesAsync(file, code, line);

        Assert.Empty(changes);
    }

    [Fact]
    public async Task ReturnsNothingForAFileOutsideTheProject()
    {
        var (service, _) = await OpenProjectAsync("Public Class Probe\nEnd Class");

        var changes = await service.GetIdentifierCasingChangesAsync(
            Path.Combine(_root, "NotInProject.vb"), "Public Class X\nEnd Class", 0);

        Assert.Empty(changes);
    }

    public void Dispose()
    {
        _service?.Dispose();
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }
}
