using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>One spelling for one file, found without freezing the editor.</summary>
public sealed class PathIdentityTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basalt-paths-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
    }

    [Theory]
    [InlineData("c:\\x\\a.vb", "C:\\x\\a.vb")]
    [InlineData("C:\\x\\a.vb", "C:\\x\\a.vb")]
    [InlineData("d:\\", "D:\\")]
    public void BringsAWindowsDriveLetterToOneCase(string given, string expected) =>
        Assert.Equal(expected, PathIdentity.NormalizeRoot(given, windows: true));

    [Fact]
    public void LeavesRootsAloneOffWindows()
    {
        Assert.Equal("/x", PathIdentity.NormalizeRoot("/x", windows: false));
        Assert.Equal("c:\\x", PathIdentity.NormalizeRoot("c:\\x", windows: false));
    }

    [Fact]
    public void ResolvingALinkLoopFallsBackToTheWrittenPathInsteadOfThrowing()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var a = Path.Combine(_directory, "a");
        var b = Path.Combine(_directory, "b");
        Directory.CreateSymbolicLink(a, b);
        Directory.CreateSymbolicLink(b, a);

        Assert.False(PathIdentity.Same(Path.Combine(a, "x.vb"), Path.Combine(_directory, "other.vb")));
    }

    [Fact]
    public void MatchesTheSameFileAcrossManyDocumentsWithoutResolvingEachOne()
    {
        // Resolving a path lists its parent folders on some disks; doing that
        // for every open document at every comparison froze the editor.
        var resolved = new List<string>();
        var matcher = new PathMatcher(path => { resolved.Add(path); return path; });

        var target = Path.Combine(_directory, "Target.vb");
        var documents = Enumerable.Range(0, 300).Select(i => Path.Combine(_directory, $"Doc{i}.vb")).ToList();

        // Another folder, same file name: the one pair that has to be resolved.
        documents.Add(Path.Combine(_directory, "sub", "Target.vb"));

        var matches = documents.Count(d => matcher.Same(d, target));

        Assert.Equal(0, matches);
        Assert.True(resolved.Count <= 4, $"resolved {resolved.Count} paths");

        // The same pair again costs nothing more.
        var before = resolved.Count;
        matcher.Same(documents[^1], target);
        Assert.Equal(before, resolved.Count);

        // Identical spellings need no resolving at all.
        Assert.True(new PathMatcher(_ => throw new InvalidOperationException("resolved")).Same(target, target));
    }

    [Fact]
    public void TreatsTheSameFileThroughALinkAsTheSame()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Creating a link needs privileges on Windows.");

        var link = Path.Combine(Directory.CreateTempSubdirectory("basalt-paths-link-").FullName, "dir");
        Directory.CreateSymbolicLink(link, _directory);

        try
        {
            File.WriteAllText(Path.Combine(_directory, "A.vb"), "x");

            Assert.True(new PathMatcher().Same(Path.Combine(link, "A.vb"), Path.Combine(_directory, "A.vb")));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(link)!, recursive: true);
        }
    }
}
