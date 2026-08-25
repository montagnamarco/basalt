using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Renaming a symbol that Razor views use.
///
/// Roslyn cannot reach a .vbhtml: it is not a solution document, so the
/// renamer neither sees nor edits it. Before this, renaming a model property
/// left every view naming the old one and the break appeared at build time,
/// in generated code.
/// </summary>
public sealed class ViewRenameTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-viewrename-").FullName;

    private readonly RoslynLanguageService _service = new();

    private string ModelPath => Path.Combine(_root, "Model.vb");
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

        await File.WriteAllTextAsync(ModelPath, """
            Public Class Person
                Public Property Name As String
                Public Property UserName As String
            End Class
            """);

        Directory.CreateDirectory(Path.GetDirectoryName(ViewPath)!);

        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.Name</p>
            """);

        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));
    }

    /// <summary>The caret on the Name property's declaration.</summary>
    private async Task<int> CaretOnNameAsync()
    {
        var model = await File.ReadAllTextAsync(ModelPath);

        return model.IndexOf("Name As String", StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamingAModelPropertyRewritesTheViewThatUsesIt()
    {
        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        Assert.Null(preview.Problem);

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("@p.FullName", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesAnUnrelatedNameThatMerelyContainsItAlone()
    {
        // UserName contains Name. A textual rename would corrupt it.
        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.UserName</p>
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        // The view uses only UserName, so nothing in it should change at all.
        Assert.DoesNotContain(preview.Changes, c => c.FilePath == ViewPath);
    }

    [Fact]
    public async Task DoesNotListAViewThatDoesNotUseTheSymbol()
    {
        await File.WriteAllTextAsync(ViewPath, "<p>nothing here</p>");

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        Assert.DoesNotContain(preview.Changes, c => c.FilePath == ViewPath);
    }

    [Fact]
    public async Task RenamesEveryUseOnTheSameLine()
    {
        // The map gives a line, not a column, so both uses on it must be
        // found by searching the line rather than by arithmetic.
        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
            End Code
            <p>@p.Name and again @p.Name</p>
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Equal(2, view.NewText.Split("FullName").Length - 1);
        Assert.DoesNotContain("p.Name", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LeavesTheWordInsideMarkupAlone()
    {
        // "Name" appears as literal text and as an attribute. Only the code
        // use is a reference to the property.
        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
            End Code
            <label title="Name">Name</label>
            <p>@p.Name</p>
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("@p.FullName", view.NewText, StringComparison.Ordinal);
        Assert.Contains("title=\"Name\">Name</label>", view.NewText, StringComparison.Ordinal);
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }
}
