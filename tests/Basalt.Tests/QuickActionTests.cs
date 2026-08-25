using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// The fixes offered for problems in the code.
///
/// Run against real compilations: the point of using Roslyn's own providers is
/// that they behave as they do in Visual Studio, and only a real workspace
/// shows whether they were found and applied at all.
/// </summary>
public sealed class QuickActionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-quickfix", Guid.NewGuid().ToString("N"));

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

    private async Task<RoslynLanguageService> OpenAsync(SourceLanguage language)
    {
        var service = new RoslynLanguageService();

        await service.OpenSolutionAsync(Path.Combine(
            _root, language == SourceLanguage.VisualBasic ? "Probe.vbproj" : "Probe.csproj"));

        return service;
    }

    [Fact]
    public async Task FindsRoslynsOwnFixProviders()
    {
        // If the Features assemblies do not load, every later test would fail
        // for that one reason; this says so directly.
        // Visual Basic imports System.Collections.Generic project-wide, so a
        // List needs no fix; System.Text is not imported by default.
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim builder As New StringBuilder()
                End Sub
            End Class
            """;

        CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var path = Path.Combine(_root, "Program.vb");

        var actions = await service.GetQuickActionsAsync(
            path, code.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2);

        // The type is unknown without its namespace, so something should be
        // offered — importing it, at least.
        Assert.NotEmpty(actions);
    }

    [Fact]
    public async Task OffersToImportAMissingNamespaceInVisualBasic()
    {
        // Visual Basic imports System.Collections.Generic project-wide, so a
        // List needs no fix; System.Text is not imported by default.
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim builder As New StringBuilder()
                End Sub
            End Class
            """;

        CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var actions = await service.GetQuickActionsAsync(
            Path.Combine(_root, "Program.vb"),
            code.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2);

        Assert.Contains(actions, a =>
            a.Title.Contains("System.Text", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AppliesTheImportItOffered()
    {
        // Visual Basic imports System.Collections.Generic project-wide, so a
        // List needs no fix; System.Text is not imported by default.
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim builder As New StringBuilder()
                End Sub
            End Class
            """;

        CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var path = Path.Combine(_root, "Program.vb");

        var actions = await service.GetQuickActionsAsync(
            path, code.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2);

        var import = actions.First(a =>
            a.Title.Contains("System.Text", StringComparison.Ordinal));

        var updated = await service.ApplyQuickActionAsync(path, import);

        Assert.NotNull(updated);
        Assert.Contains("Imports System.Text", updated!);
    }

    [Fact]
    public async Task OffersNothingWhereThereIsNoProblem()
    {
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim x As Integer = 1
                End Sub
            End Class
            """;

        CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var actions = await service.GetQuickActionsAsync(
            Path.Combine(_root, "Program.vb"),
            code.IndexOf("Dim x", StringComparison.Ordinal) + 4);

        Assert.Empty(actions);
    }

    [Fact]
    public async Task OffersEachFixOnlyOnce()
    {
        // Visual Basic imports System.Collections.Generic project-wide, so a
        // List needs no fix; System.Text is not imported by default.
        const string code = """
            Public Class Probe
                Public Sub M()
                    Dim builder As New StringBuilder()
                End Sub
            End Class
            """;

        CreateProject(SourceLanguage.VisualBasic, code);
        using var service = await OpenAsync(SourceLanguage.VisualBasic);

        var actions = await service.GetQuickActionsAsync(
            Path.Combine(_root, "Program.vb"),
            code.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2);

        var titles = actions.Select(a => a.Title).ToList();

        Assert.Equal(titles.Count, titles.Distinct().Count());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
