using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Moving a type into a file named after it.
///
/// Two files change, and the preview has to show both: a refactoring that
/// writes a file the user did not know about is one they cannot undo.
/// </summary>
public sealed class MoveTypeToFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-movetype", Guid.NewGuid().ToString("N"));

    public MoveTypeToFileTests() => Directory.CreateDirectory(_root);

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

    private static int Caret(string code, string fragment)
    {
        var at = code.IndexOf(fragment, StringComparison.Ordinal);

        Assert.True(at >= 0, $"'{fragment}' is not in the source.");

        return at;
    }

    private const string TwoTypes = """
        Imports System.Text

        Public Class First
            Public Sub M()
            End Sub
        End Class

        Public Class Second
            Public Sub N()
            End Sub
        End Class
        """;

    [Fact]
    public async Task ChangesBothTheOldFileAndTheNewOne()
    {
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Equal(2, preview.Changes.Count);
    }

    [Fact]
    public async Task NamesTheNewFileAfterTheType()
    {
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        Assert.Contains(preview.Changes,
            c => Path.GetFileName(c.FilePath) == "Second.vb");
    }

    [Fact]
    public async Task PutsTheTypeInTheNewFileAndTakesItOutOfTheOld()
    {
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        var moved = preview.Changes.Single(c => Path.GetFileName(c.FilePath) == "Second.vb");
        var left = preview.Changes.Single(c => Path.GetFileName(c.FilePath) == "Program.vb");

        Assert.Contains("Public Class Second", moved.NewText, StringComparison.Ordinal);
        Assert.DoesNotContain("Public Class Second", left.NewText, StringComparison.Ordinal);

        // And the one that stayed is untouched.
        Assert.Contains("Public Class First", left.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakesTheImportsAlong()
    {
        // The type may need them, and an unused import in the new file is a
        // smaller problem than a missing one.
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        var moved = preview.Changes.Single(c => Path.GetFileName(c.FilePath) == "Second.vb");

        Assert.Contains("Imports System.Text", moved.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task KeepsTheTypeInItsNamespace()
    {
        // A type moved out of its namespace has a different full name, and
        // every use of it would stop finding it.
        const string code = """
            Namespace Things
                Public Class First
                End Class

                Public Class Second
                End Class
            End Namespace
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(code, "Public Class Second"));

        Assert.True(preview.CanApply, preview.Problem);

        var moved = preview.Changes.Single(c => Path.GetFileName(c.FilePath) == "Second.vb");

        Assert.Contains("Namespace Things", moved.NewText, StringComparison.Ordinal);
        Assert.Contains("End Namespace", moved.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesWhenTheTypeIsAlreadyAloneInItsFile()
    {
        const string code = """
            Public Class Only
            End Class
            """;

        var (service, path) = await OpenAsync(code);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(path, Caret(code, "Only"));

        Assert.False(preview.CanApply);
        Assert.Contains("only type", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesWhenTheFileWouldOverwriteAnother()
    {
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        await File.WriteAllTextAsync(Path.Combine(_root, "Second.vb"), "' in the way");

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        Assert.False(preview.CanApply);
        Assert.Contains("already exists", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaysSoWhenTheCaretIsNotInAType()
    {
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Imports System.Text"));

        Assert.False(preview.CanApply);
        Assert.Contains("Put the caret in a type", preview.Problem ?? "",
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task WritesBothFilesWhenApplied()
    {
        // The new file has to be created, not only described.
        var (service, path) = await OpenAsync(TwoTypes);
        using var _ = service;

        var preview = await service.PreviewMoveTypeToFileAsync(
            path, Caret(TwoTypes, "Public Class Second"));

        Assert.True(await RoslynLanguageService.ApplyAsync(preview));

        var created = Path.Combine(_root, "Second.vb");

        Assert.True(File.Exists(created));
        Assert.Contains("Public Class Second", await File.ReadAllTextAsync(created),
            StringComparison.Ordinal);
        Assert.DoesNotContain("Public Class Second", await File.ReadAllTextAsync(path),
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
