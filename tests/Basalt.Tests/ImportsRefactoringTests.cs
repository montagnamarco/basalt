using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Tidying the Imports of a file.
///
/// Against a real project rather than an ad-hoc one: whether an import is used
/// is decided by what the names bind to, which needs the framework references
/// a real project brings.
/// </summary>
public sealed class ImportsRefactoringTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-imports", Guid.NewGuid().ToString("N"));

    public ImportsRefactoringTests() => Directory.CreateDirectory(_root);

    private async Task<(RoslynLanguageService Service, string Path)> OpenAsync(string code)
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Probe.vbproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        var path = Path.Combine(_root, "Program.vb");
        await File.WriteAllTextAsync(path, code);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        return (service, path);
    }

    [Fact]
    public async Task RemovesAnImportNothingUses()
    {
        var (service, path) = await OpenAsync("""
            Imports System.Text

            Public Class Probe
                Public Sub M()
                End Sub
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);

        Assert.True(preview.CanApply, preview.Problem);
        Assert.DoesNotContain("System.Text", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsAnImportThatIsUsed()
    {
        var (service, path) = await OpenAsync("""
            Imports System.Text

            Public Class Probe
                Public Sub M()
                    Dim b As New StringBuilder()
                End Sub
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);

        // Nothing to remove and nothing out of order, so there is no change.
        Assert.False(preview.CanApply);
        Assert.Contains("already tidy", preview.Problem ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task SortsTheImportsItKeeps()
    {
        var (service, path) = await OpenAsync("""
            Imports System.Text
            Imports System.Collections.Generic

            Public Class Probe
                Public Sub M()
                    Dim b As New StringBuilder()
                    Dim l As New List(Of Integer)
                End Sub
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.True(
            updated.IndexOf("System.Collections.Generic", StringComparison.Ordinal)
                < updated.IndexOf("System.Text", StringComparison.Ordinal),
            $"The imports came out unsorted:\n{updated}");
    }

    [Fact]
    public async Task KeepsAnAliasEvenWhenItLooksUnused()
    {
        // What an alias stands for is not a namespace this can check, so
        // dropping one would risk changing what the code means.
        var (service, path) = await OpenAsync("""
            Imports SB = System.Text.StringBuilder

            Public Class Probe
                Public Sub M()
                End Sub
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);

        if (preview.CanApply)
        {
            Assert.Contains("SB", preview.Changes.Single().NewText, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task LeavesTheCodeBelowTheImportsAlone()
    {
        var (service, path) = await OpenAsync("""
            Imports System.Text

            Public Class Probe
                Public Sub M()
                End Sub
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);
        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Public Class Probe", updated, StringComparison.Ordinal);
        Assert.Contains("Public Sub M()", updated, StringComparison.Ordinal);
        Assert.Contains("End Class", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoWhenThereAreNoImports()
    {
        var (service, path) = await OpenAsync("""
            Public Class Probe
            End Class
            """);

        using var _ = service;

        var preview = await service.PreviewTidyImportsAsync(path);

        Assert.False(preview.CanApply);
        Assert.Contains("no imports", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
