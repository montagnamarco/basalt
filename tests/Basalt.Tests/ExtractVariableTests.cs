using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

public sealed class ExtractVariableTests
{
    private const string Source = """
        Module Program
            Sub Main()
                Console.WriteLine(2 + 3)
            End Sub
        End Module
        """;

    /// <summary>A solution holding <see cref="Source"/> as one VB file.</summary>
    private static (Solution Solution, string Path) Build(string text = Source)
    {
        var workspace = new AdhocWorkspace();

        var project = workspace.AddProject("P", LanguageNames.VisualBasic);
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.vb");

        var info = DocumentInfo.Create(
            DocumentId.CreateNewId(project.Id),
            "Program.vb",
            loader: TextLoader.From(
                TextAndVersion.Create(SourceText.From(text), VersionStamp.Create())),
            filePath: path);

        return (workspace.AddDocument(info).Project.Solution, path);
    }

    /// <summary>The offset of <paramref name="fragment"/> in the source.</summary>
    private static (int Start, int Length) Find(string fragment, string text = Source) =>
        (text.IndexOf(fragment, StringComparison.Ordinal), fragment.Length);

    [Fact]
    public async Task WritesADimAboveTheStatementAndUsesTheNameInPlace()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 + 3");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "sum");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("        Dim sum = 2 + 3", updated, StringComparison.Ordinal);
        Assert.Contains("Console.WriteLine(sum)", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteLine(2 + 3)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsTheDimBeforeTheStatementNotAfterIt()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 + 3");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "sum");

        var updated = preview.Changes.Single().NewText;

        Assert.True(
            updated.IndexOf("Dim sum", StringComparison.Ordinal)
                < updated.IndexOf("WriteLine(sum)", StringComparison.Ordinal),
            $"The declaration landed after its use:\n{updated}");
    }

    [Fact]
    public async Task KeepsTheIndentationOfTheStatementItCameFrom()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 + 3");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "sum");

        var line = preview.Changes.Single().NewText
            .Split('\n')
            .First(l => l.Contains("Dim sum", StringComparison.Ordinal));

        Assert.StartsWith("        Dim", line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesHalfAnExpression()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 +");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "sum");

        Assert.False(preview.CanApply);
        Assert.Contains("single expression", preview.Problem ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesANameThatIsNotAnIdentifier()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 + 3");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "2sum");

        Assert.False(preview.CanApply);
    }

    [Fact]
    public async Task LeavesTheRestOfTheFileByteForByteAlone()
    {
        var (solution, path) = Build();
        var (start, length) = Find("2 + 3");

        var preview = await new ExtractVariableRefactoring(solution)
            .PreviewAsync(path, start, length, "sum");

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Module Program", updated, StringComparison.Ordinal);
        Assert.Contains("End Module", updated, StringComparison.Ordinal);
        Assert.Contains("    Sub Main()", updated, StringComparison.Ordinal);
    }
}
