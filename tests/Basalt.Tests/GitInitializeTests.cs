using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// Starting a repository from inside the IDE.
///
/// A new solution had no way to become one without leaving for a terminal.
/// Run against real git, in a real folder: a mock would prove only that the
/// arguments were assembled.
/// </summary>
public sealed class GitInitializeTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-gitinit-").FullName;

    public GitInitializeTests()
    {
        File.WriteAllText(Path.Combine(_root, "Program.vb"), "Module M\nEnd Module\n");

        Directory.CreateDirectory(Path.Combine(_root, "obj"));
        File.WriteAllText(Path.Combine(_root, "obj", "build.tmp"), "output");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public async Task ItMakesARepository()
    {
        Assert.True(await GitSourceControlService.InitializeAsync(_root));

        var git = new GitSourceControlService(_root);

        Assert.True(await git.IsRepositoryAsync(_root));
    }

    [Fact]
    public async Task ItLeavesAFirstCommitBehind()
    {
        // Without one there is no HEAD, and half a git client has nothing to
        // work from.
        await GitSourceControlService.InitializeAsync(_root, "Start");

        var git = new GitSourceControlService(_root);

        var log = await git.GetLogAsync(10);

        var only = Assert.Single(log);

        Assert.Equal("Start", only.Message);
    }

    [Fact]
    public async Task TheBranchIsCalledMain()
    {
        await GitSourceControlService.InitializeAsync(_root);

        var git = new GitSourceControlService(_root);

        Assert.Equal("main", await git.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task ItWritesAnIgnoreFile()
    {
        await GitSourceControlService.InitializeAsync(_root);

        var ignore = Path.Combine(_root, ".gitignore");

        Assert.True(File.Exists(ignore));

        var text = await File.ReadAllTextAsync(ignore);

        Assert.Contains("obj/", text, StringComparison.Ordinal);
        Assert.Contains("bin/", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBuildOutputStaysOutOfTheFirstCommit()
    {
        // The point of writing the ignore file before committing.
        await GitSourceControlService.InitializeAsync(_root);

        var git = new GitSourceControlService(_root);

        var changes = await git.GetStatusAsync();

        Assert.DoesNotContain(changes, c => c.Path.Contains("obj", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnIgnoreFileAlreadyThereIsLeftAlone()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, ".gitignore"), "# mine\n*.log\n");

        await GitSourceControlService.InitializeAsync(_root);

        var text = await File.ReadAllTextAsync(Path.Combine(_root, ".gitignore"));

        Assert.Equal("# mine\n*.log\n", text);
    }

    [Fact]
    public async Task ItRefusesInsideARepositoryThatExists()
    {
        // A repository nested in a repository surprises everyone who meets one.
        await GitSourceControlService.InitializeAsync(_root);

        Assert.False(await GitSourceControlService.InitializeAsync(_root));
    }

    [Fact]
    public async Task ItRefusesForAFolderThatIsNotThere()
    {
        Assert.False(await GitSourceControlService.InitializeAsync(
            Path.Combine(_root, "nowhere")));
    }
}
