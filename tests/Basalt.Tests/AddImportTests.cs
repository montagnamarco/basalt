using Basalt.Workspace;
using Basalt.Workspace.Web;

namespace Basalt.Tests;

/// <summary>
/// Adding the @Imports that makes a name in a view resolve.
///
/// The part a template author cannot do without going to look: which
/// namespace declares the type. The IDE knows, because it has the solution.
/// </summary>
public sealed class AddImportTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-addimport-").FullName;

    private readonly RoslynLanguageService _service = new();

    private string ViewPath => Path.Combine(_root, "Views", "Index.vbhtml");

    public async ValueTask InitializeAsync()
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

        await File.WriteAllTextAsync(Path.Combine(_root, "Model.vb"), """
            Namespace Shop.Models
                Public Class Person
                    Public Property Name As String
                End Class
            End Namespace
            """);

        Directory.CreateDirectory(Path.GetDirectoryName(ViewPath)!);

        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    [Fact]
    public async Task FindsTheNamespaceThatDeclaresTheType()
    {
        await File.WriteAllTextAsync(ViewPath, "<p>@New Person().Name</p>");

        var preview = await _service.PreviewAddImportAsync(ViewPath, "Person");

        Assert.Null(preview.Problem);
        Assert.Contains("Shop.Models", preview.Title, StringComparison.Ordinal);

        var change = Assert.Single(preview.Changes);

        Assert.StartsWith("@Imports Shop.Models", change.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsItInOrderWithTheImportsAlreadyThere()
    {
        await File.WriteAllTextAsync(ViewPath,
            "@Imports System\n@Imports Zebra\n<p>@New Person().Name</p>");

        var preview = await _service.PreviewAddImportAsync(ViewPath, "Person");

        var change = Assert.Single(preview.Changes);

        // System first, then the two others alphabetically.
        Assert.StartsWith(
            "@Imports System\n@Imports Shop.Models\n@Imports Zebra\n",
            change.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoWhenNothingDeclaresTheName()
    {
        await File.WriteAllTextAsync(ViewPath, "<p>@New Nonexistent().X</p>");

        var preview = await _service.PreviewAddImportAsync(ViewPath, "Nonexistent");

        Assert.NotNull(preview.Problem);
        Assert.Contains("Nonexistent", preview.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SaysSoWhenItIsAlreadyImported()
    {
        await File.WriteAllTextAsync(ViewPath,
            "@Imports Shop.Models\n<p>@New Person().Name</p>");

        var preview = await _service.PreviewAddImportAsync(ViewPath, "Person");

        Assert.NotNull(preview.Problem);
        Assert.Contains("already", preview.Problem, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LeavesTheMarkupAlone()
    {
        await File.WriteAllTextAsync(ViewPath, "<p>@New Person().Name</p>\n<div>x</div>");

        var preview = await _service.PreviewAddImportAsync(ViewPath, "Person");

        var change = Assert.Single(preview.Changes);

        Assert.Contains("<p>@New Person().Name</p>\n<div>x</div>",
            change.NewText, StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }
}
