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

    [Fact]
    public async Task LeavesTheWordInMarkupAndAnotherSymbolOnTheSameLineAlone()
    {
        // The use was found by line, and every "Name" on the line renamed:
        // the label's text and the Name of a different class with it.
        await File.WriteAllTextAsync(Path.Combine(_root, "Pet.vb"), """
            Public Class Pet
                Public Property Name As String
            End Class
            """);
        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
                Dim pet As New Pet()
            End Code
            <label>Name</label> <p>@p.Name owns @pet.Name</p>
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("<label>Name</label> <p>@p.FullName owns @pet.Name</p>", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamesAUseWrittenInAnotherCase()
    {
        // Visual Basic reads p.name as p.Name; left as it was, it would name
        // a property that no longer exists.
        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
                Dim shown = p.name
            End Code
            <p>@shown</p>
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("Dim shown = p.FullName", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamesOnlyTheRightNameInABlocksOpening()
    {
        // "@If" is written as "If" at the writer's indent, and the mapping
        // for it drifts by that indent: it landed exactly on pet.Name, a
        // dozen characters on, and renamed the wrong property. The string
        // and the case of "p.name" are Visual Basic's own business.
        await File.WriteAllTextAsync(Path.Combine(_root, "Pet.vb"), """
            Public Class Pet
                Public Property Name As String
            End Class
            """);
        await _service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
                Dim pet As New Pet()
            End Code
            @If p.name <> pet.Name AndAlso p.Name <> "Name" Then
                <p>different</p>
            End If
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("@If p.FullName <> pet.Name AndAlso p.FullName <> \"Name\" Then", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RenamesAUseInAnElseIf()
    {
        await File.WriteAllTextAsync(ViewPath, """
            @Code
                Dim p As New Person()
            End Code
            @If p.UserName = "" Then
                <p>none</p>
            @ElseIf p.Name <> "" Then
                <p>named</p>
            @End If
            """);

        var preview = await _service.PreviewRenameAsync(
            ModelPath, await CaretOnNameAsync(), "FullName");

        var view = Assert.Single(preview.Changes, c => c.FilePath == ViewPath);

        Assert.Contains("@ElseIf p.FullName <> \"\" Then", view.NewText, StringComparison.Ordinal);
        Assert.Contains("@If p.UserName = \"\" Then", view.NewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ASpanMappingThatDriftsIsRefusedRatherThanTrusted()
    {
        // The block's mapping starts at the writer's indent on one side and
        // after the "@" on the other: its offsets land a dozen characters on.
        const string template = "@If p.Name <> pet.Name Then\n    <p>x</p>\nEnd If\n";
        var generated = Basalt.Razor.Vb.VbHtmlCodeWriter.WriteWithMap(
            Basalt.Razor.Vb.VbHtmlParser.Parse(template), "Drift", "Site", "Drift.vbhtml");

        var use = generated.Code.IndexOf("If p.Name", StringComparison.Ordinal) + "If p.".Length;
        var both = new Basalt.Workspace.Web.TemplateGeneration.Generated(generated.Code, generated.Map);

        Assert.NotEqual(template.IndexOf("p.Name", StringComparison.Ordinal) + 2, generated.Map.ToOriginal(use));
        Assert.Null(Basalt.Workspace.Web.TemplateGeneration.VerifiedSpanOriginal(template, both, use));
    }

    public ValueTask DisposeAsync()
    {
        _service.Dispose();

        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }
}
