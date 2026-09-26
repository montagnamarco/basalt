using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Basalt.Workspace.Refactoring;

namespace Basalt.Tests;

/// <summary>
/// Lifting statements into a method.
///
/// The signature is what these check: which locals become parameters and
/// which become the return value is decided by data flow, not by guessing.
/// </summary>
public sealed class ExtractMethodTests
{
    private static (Solution Solution, string Path) Build(string text)
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

    /// <summary>The preview for the span covering <paramref name="fragment"/>.</summary>
    private static Task<RefactoringPreview> ExtractAsync(
        string source, string fragment, string name = "Helper")
    {
        // The fragments are written with "\n"; a raw string literal takes the
        // line breaks of the checkout, which are "\r\n" on Windows.
        source = source.ReplaceLineEndings("\n");

        var (solution, path) = Build(source);
        var start = source.IndexOf(fragment, StringComparison.Ordinal);

        Assert.True(start >= 0, $"The fragment is not in the source:\n{fragment}");

        return new ExtractMethodRefactoring(solution)
            .PreviewAsync(path, start, fragment.Length, name);
    }

    private const string WithLocals = """
        Module Program
            Sub Main()
                Dim a = 1
                Dim b = 2
                Console.WriteLine(a + b)
            End Sub
        End Module
        """;

    [Fact]
    public async Task MakesASubWhenNothingHasToComeBack()
    {
        var preview = await ExtractAsync(WithLocals, "Console.WriteLine(a + b)");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Private Sub Helper(", updated, StringComparison.Ordinal);
        Assert.Contains("End Sub", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("Private Function Helper", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PassesInTheLocalsTheStatementsRead()
    {
        var preview = await ExtractAsync(WithLocals, "Console.WriteLine(a + b)");

        var updated = preview.Changes.Single().NewText;

        // Both are read inside and declared outside, so both come in.
        Assert.Contains("a As Integer", updated, StringComparison.Ordinal);
        Assert.Contains("b As Integer", updated, StringComparison.Ordinal);
        Assert.Contains("Helper(a, b)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MakesAFunctionWhenAValueIsStillNeededAfterwards()
    {
        const string source = """
            Module Program
                Sub Main()
                    Dim a = 1
                    Dim total = a + 1
                    Console.WriteLine(total)
                End Sub
            End Module
            """;

        var preview = await ExtractAsync(source, "Dim total = a + 1");

        Assert.True(preview.CanApply, preview.Problem);

        var updated = preview.Changes.Single().NewText;

        Assert.Contains("Private Function Helper(a As Integer) As Integer",
            updated, StringComparison.Ordinal);
        Assert.Contains("Return total", updated, StringComparison.Ordinal);

        // The declaration moved into the method, so the call site declares it.
        Assert.Contains("Dim total = Helper(a)", updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TakesTheEnclosingMethodsParametersAsParametersToo()
    {
        const string source = """
            Module Program
                Sub Show(count As Integer)
                    Console.WriteLine(count)
                End Sub
            End Module
            """;

        var preview = await ExtractAsync(source, "Console.WriteLine(count)");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("count As Integer", preview.Changes.Single().NewText,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefusesASelectionThatReturnsFromTheMethod()
    {
        const string source = """
            Module Program
                Function Check() As Integer
                    Return 1
                End Function
            End Module
            """;

        var preview = await ExtractAsync(source, "Return 1");

        Assert.False(preview.CanApply);
        Assert.Contains("returns from the method", preview.Problem ?? "",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefusesHalfAStatement()
    {
        var preview = await ExtractAsync(WithLocals, "Console.WriteLine(a +");

        Assert.False(preview.CanApply);
    }

    [Fact]
    public async Task RefusesANameThatIsNotAnIdentifier()
    {
        var preview = await ExtractAsync(WithLocals, "Console.WriteLine(a + b)", "2Helper");

        Assert.False(preview.CanApply);
    }

    [Fact]
    public async Task ExtractsSeveralStatementsAtOnce()
    {
        const string source = """
            Module Program
                Sub Main()
                    Dim a = 1
                    Console.WriteLine(a)
                    Console.WriteLine(a + 1)
                End Sub
            End Module
            """;

        var preview = await ExtractAsync(
            source, "Console.WriteLine(a)\n        Console.WriteLine(a + 1)");

        Assert.True(preview.CanApply, preview.Problem);
        Assert.Contains("2 statements", preview.Title, StringComparison.Ordinal);

        var updated = preview.Changes.Single().NewText;

        // Both moved into the method, and one call replaced them.
        Assert.Contains("Helper(a)", updated, StringComparison.Ordinal);
        Assert.Equal(2, updated.Split("Console.WriteLine").Length - 1);
    }

    [Fact]
    public async Task RefusesWhenTwoValuesWouldHaveToComeBack()
    {
        // Rather than quietly turning them into ByRef parameters, which
        // changes the shape of the call in a way the user did not ask for.
        var preview = await ExtractAsync(WithLocals, "Dim a = 1\n        Dim b = 2");

        Assert.False(preview.CanApply);
        Assert.Contains("more than one value", preview.Problem ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task PutsTheNewMethodAfterTheOneItCameFrom()
    {
        var preview = await ExtractAsync(WithLocals, "Console.WriteLine(a + b)");

        var updated = preview.Changes.Single().NewText;

        var endOfMain = updated.IndexOf("End Sub", StringComparison.Ordinal);
        var helper = updated.IndexOf("Private Sub Helper", StringComparison.Ordinal);

        Assert.True(helper > endOfMain,
            $"The method landed inside the one it came from:\n{updated}");
    }
}
