using Basalt.Workspace;
using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

/// <summary>What a name may be.</summary>
public class RenameNameTests
{
    [Theory]
    [InlineData("total", true)]
    [InlineData("_total", true)]
    [InlineData("total2", true)]
    [InlineData("name$", true)]
    [InlineData("count%", true)]
    [InlineData("", false)]
    [InlineData("2total", false)]
    [InlineData("has space", false)]
    [InlineData("has-dash", false)]
    public void AcceptsOnlyNamesThatWouldCompile(string name, bool valid)
    {
        // Checked here rather than left to Roslyn, which accepts anything and
        // produces code that will not build.
        Assert.Equal(valid, RenameRefactoring.IsValidName(name));
    }
}

/// <summary>Describing a change before making it.</summary>
public class ChangePreviewTests
{
    [Fact]
    public void ReportsTheLinesThatDiffer()
    {
        var preview = new FileChangePreview(
            "/a/Program.vb",
            "Module A\n    Dim total = 1\nEnd Module",
            "Module A\n    Dim sum = 1\nEnd Module");

        var changed = preview.ChangedLines;

        Assert.Single(changed);
        Assert.Equal(2, changed[0].Line);
        Assert.Contains("total", changed[0].Before);
        Assert.Contains("sum", changed[0].After);
    }

    [Fact]
    public void ReportsNothingWhenTheFileIsUnchanged()
    {
        var preview = new FileChangePreview("/a/A.vb", "same", "same");

        Assert.Empty(preview.ChangedLines);
    }

    [Fact]
    public void ReportsLinesAddedAtTheEnd()
    {
        var preview = new FileChangePreview("/a/A.vb", "one", "one\ntwo");

        Assert.Single(preview.ChangedLines);
    }

    [Fact]
    public void SaysWhyARefusedRefactoringCannotBeDone()
    {
        // A refactoring that quietly does nothing is worse than one that says
        // why it will not.
        var refused = RefactoringPreview.Refused("Rename", "There is nothing to rename here.");

        Assert.False(refused.CanApply);
        Assert.Contains("nothing to rename", refused.Problem);
    }
}

/// <summary>
/// Renaming across a real solution.
///
/// Run against a compiled workspace: what makes rename worth having is that it
/// knows which "Count" is the one being renamed, and only a real semantic
/// model knows that.
/// </summary>
public sealed class RenameAcrossSolutionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-rename", Guid.NewGuid().ToString("N"));

    private string ProjectPath => Path.Combine(_root, "Probe.vbproj");

    public RenameAcrossSolutionTests() => Directory.CreateDirectory(_root);

    private async Task<RoslynLanguageService> OpenAsync(params (string Name, string Code)[] files)
    {
        await File.WriteAllTextAsync(ProjectPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Library</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <RootNamespace></RootNamespace>
              </PropertyGroup>
            </Project>
            """);

        foreach (var (name, code) in files)
            await File.WriteAllTextAsync(Path.Combine(_root, name), code);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(ProjectPath);

        return service;
    }

    [Fact]
    public async Task RenamesEveryUseOfASymbol()
    {
        const string code = """
            Public Class Calculator
                Private total As Integer

                Public Sub Add(value As Integer)
                    total = total + value
                End Sub

                Public Function Result() As Integer
                    Return total
                End Function
            End Class
            """;

        using var service = await OpenAsync(("Calculator.vb", code));

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Calculator.vb"),
            code.IndexOf("total As Integer", StringComparison.Ordinal) + 2,
            "sum");

        Assert.True(preview.CanApply, preview.Problem);

        var changed = preview.Changes.Single();

        Assert.DoesNotContain("total", changed.NewText);
        Assert.Contains("sum = sum + value", changed.NewText);
    }

    [Fact]
    public async Task ReachesEveryFileThatUsesTheSymbol()
    {
        // The point of renaming rather than replacing: it follows the symbol
        // wherever it is used.
        const string declaration = """
            Public Class Customer
                Public Property Name As String
            End Class
            """;

        const string usage = """
            Public Module Report
                Public Function Describe(c As Customer) As String
                    Return c.Name
                End Function
            End Module
            """;

        using var service = await OpenAsync(
            ("Customer.vb", declaration), ("Report.vb", usage));

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Customer.vb"),
            declaration.IndexOf("Name As String", StringComparison.Ordinal) + 2,
            "FullName");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Equal(2, preview.Changes.Count);

        Assert.Contains(preview.Changes,
            c => c.FileName == "Report.vb" && c.NewText.Contains("c.FullName"));
    }

    [Fact]
    public async Task LeavesAlonAnUnrelatedNameThatMatches()
    {
        // A search and replace would rename both; this must not.
        const string code = """
            Public Class Basket
                Private count As Integer

                Public Sub Add(items As System.Collections.Generic.List(Of String))
                    count = items.Count
                End Sub
            End Class
            """;

        using var service = await OpenAsync(("Basket.vb", code));

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Basket.vb"),
            code.IndexOf("count As Integer", StringComparison.Ordinal) + 2,
            "total");

        Assert.True(preview.CanApply, preview.Problem);

        // The list's own Count is untouched.
        Assert.Contains("items.Count", preview.Changes.Single().NewText);
        Assert.Contains("total = items.Count", preview.Changes.Single().NewText);
    }

    [Fact]
    public async Task RefusesWhenThereIsNothingToRename()
    {
        const string code = """
            Public Class Empty
            End Class
            """;

        using var service = await OpenAsync(("Empty.vb", code));

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Empty.vb"), 0, "Other");

        Assert.False(preview.CanApply);
        Assert.NotNull(preview.Problem);
    }

    [Fact]
    public async Task RefusesAnInvalidName()
    {
        const string code = """
            Public Class Thing
                Private value As Integer
            End Class
            """;

        using var service = await OpenAsync(("Thing.vb", code));

        var preview = await service.PreviewRenameAsync(
            Path.Combine(_root, "Thing.vb"),
            code.IndexOf("value", StringComparison.Ordinal) + 2,
            "2bad");

        Assert.False(preview.CanApply);
        Assert.Contains("not a valid name", preview.Problem);
    }

    [Fact]
    public async Task WritesTheChangesOnlyWhenApplied()
    {
        const string code = """
            Public Class Thing
                Private value As Integer

                Public Sub Use()
                    value = 1
                End Sub
            End Class
            """;

        var path = Path.Combine(_root, "Thing.vb");

        using var service = await OpenAsync(("Thing.vb", code));

        var preview = await service.PreviewRenameAsync(
            path, code.IndexOf("value As Integer", StringComparison.Ordinal) + 2, "amount");

        // Looking is not doing: the file is untouched until applied.
        Assert.Contains("value", await File.ReadAllTextAsync(path));

        Assert.True(await RoslynLanguageService.ApplyAsync(preview));

        var written = await File.ReadAllTextAsync(path);

        Assert.Contains("amount = 1", written);
        Assert.DoesNotContain("value = 1", written);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>
/// Quick actions, previewed before they are applied.
///
/// Roslyn's own fixes do the work; what these check is that the change is
/// described the same way a rename is, so the user reviews both alike.
/// </summary>
public sealed class QuickActionPreviewTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-quickpreview", Guid.NewGuid().ToString("N"));

    public QuickActionPreviewTests() => Directory.CreateDirectory(_root);

    private async Task<RoslynLanguageService> OpenAsync(string code)
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

        await File.WriteAllTextAsync(Path.Combine(_root, "Program.vb"), code);

        var service = new RoslynLanguageService();
        await service.OpenSolutionAsync(Path.Combine(_root, "Probe.vbproj"));

        return service;
    }

    /// <summary>StringBuilder needs an import; a List does not, in Visual Basic.</summary>
    private const string NeedsImport = """
        Public Class Probe
            Public Sub M()
                Dim builder As New StringBuilder()
            End Sub
        End Class
        """;

    [Fact]
    public async Task DescribesWhatAFixWouldChange()
    {
        using var service = await OpenAsync(NeedsImport);

        var path = Path.Combine(_root, "Program.vb");
        var position = NeedsImport.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2;

        var actions = await service.GetQuickActionsAsync(path, position);

        var import = actions.First(a => a.Title.Contains("System.Text", StringComparison.Ordinal));

        var preview = await service.PreviewQuickActionAsync(path, import);

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("Imports System.Text", preview.Changes.Single().NewText);
    }

    [Fact]
    public async Task LeavesTheFileAloneUntilApplied()
    {
        // Looking is not doing, for a fix as much as for a rename.
        using var service = await OpenAsync(NeedsImport);

        var path = Path.Combine(_root, "Program.vb");
        var position = NeedsImport.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2;

        var actions = await service.GetQuickActionsAsync(path, position);
        var import = actions.First(a => a.Title.Contains("System.Text", StringComparison.Ordinal));

        await service.PreviewQuickActionAsync(path, import);

        Assert.DoesNotContain("Imports System.Text", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task WritesTheFixWhenApplied()
    {
        using var service = await OpenAsync(NeedsImport);

        var path = Path.Combine(_root, "Program.vb");
        var position = NeedsImport.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2;

        var actions = await service.GetQuickActionsAsync(path, position);
        var import = actions.First(a => a.Title.Contains("System.Text", StringComparison.Ordinal));

        var preview = await service.PreviewQuickActionAsync(path, import);

        Assert.True(await RoslynLanguageService.ApplyAsync(preview));
        Assert.Contains("Imports System.Text", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ReportsTheLineThatWouldBeAdded()
    {
        // The preview shows lines, not whole files: an import is one line, and
        // that is what the user should see.
        using var service = await OpenAsync(NeedsImport);

        var path = Path.Combine(_root, "Program.vb");
        var position = NeedsImport.IndexOf("StringBuilder(", StringComparison.Ordinal) + 2;

        var actions = await service.GetQuickActionsAsync(path, position);
        var import = actions.First(a => a.Title.Contains("System.Text", StringComparison.Ordinal));

        var preview = await service.PreviewQuickActionAsync(path, import);

        Assert.Contains(
            preview.Changes.Single().ChangedLines,
            line => line.After.Contains("Imports System.Text"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
