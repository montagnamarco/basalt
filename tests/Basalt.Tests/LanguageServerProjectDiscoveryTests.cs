using Basalt.Razor.Vb.LanguageServer;

namespace Basalt.Tests;

public sealed class LanguageServerProjectDiscoveryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-lsp-discovery-" + Guid.NewGuid().ToString("N"));

    public LanguageServerProjectDiscoveryTests() => Directory.CreateDirectory(_root);

    public void Dispose() => ScratchFolder.Delete(_root);

    private string Write(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "");
        return path;
    }

    [Theory]
    [InlineData("Workspace.slnx")]
    [InlineData("Workspace.sln")]
    public void NestedSolutionTakesPrecedenceOverAnyProject(string solutionName)
    {
        Write("First.vbproj");
        Write(Path.Combine("a", "Nested.vbproj"));
        var expected = Write(Path.Combine("z", "src", solutionName));

        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(_root));
    }

    [Fact]
    public void SlnxTakesPrecedenceOverSlnRegardlessOfDepth()
    {
        Write("Top.sln");
        var expected = Write(Path.Combine("nested", "Workspace.SLNX"));

        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(_root));
    }

    [Theory]
    [InlineData("slnx")]
    [InlineData("sln")]
    [InlineData("vbproj")]
    public void MultipleCandidatesHaveStablePathOrder(string extension)
    {
        Write("Zebra." + extension);
        Write("Middle." + extension);
        var expected = Write("Alpha." + extension);

        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(_root));
        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(_root));
    }

    [Theory]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData(".git")]
    [InlineData(".hg")]
    [InlineData(".svn")]
    [InlineData(".vs")]
    [InlineData(".codex")]
    [InlineData(".agents")]
    [InlineData(".claude")]
    [InlineData("node_modules")]
    public void BuildAndRepositoryDirectoriesDoNotSupplyCandidates(string ignoredDirectory)
    {
        Write(Path.Combine(ignoredDirectory, "Generated.slnx"));
        Write(Path.Combine(ignoredDirectory, "Generated.vbproj"));
        var expected = Write(Path.Combine("src", "Real.vbproj"));

        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(_root));
    }

    [Theory]
    [InlineData("slnx")]
    [InlineData("sln")]
    [InlineData("vbproj")]
    public void ExplicitFileIsNotReplacedByAnotherCandidate(string extension)
    {
        Write("Preferred.slnx");
        var expected = Write(Path.Combine("nested", "Chosen." + extension));

        Assert.Equal(expected, ProjectCompilation.FindSolutionOrProject(expected));
    }

    [Fact]
    public void EmptyMissingAndUnsupportedLocationsReturnNoMatch()
    {
        Assert.Null(ProjectCompilation.FindSolutionOrProject(_root));
        Assert.Null(ProjectCompilation.FindSolutionOrProject(Path.Combine(_root, "missing")));
        Assert.Null(ProjectCompilation.FindSolutionOrProject(Write("Notes.txt")));
    }
}
