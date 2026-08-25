using Basalt.Workspace;

namespace Basalt.Tests;

/// <summary>
/// What a commit changed.
///
/// Picking one in the history used to do nothing: the panel raised an event
/// and nobody was listening, and there was no way to ask git anyway.
/// </summary>
public sealed class CommitDiffTests : IAsyncLifetime
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-commitdiff-").FullName;

    private GitSourceControlService _git = null!;

    public async ValueTask InitializeAsync()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "Program.vb"), "Module M\nEnd Module\n");

        await GitSourceControlService.InitializeAsync(_root, "First");

        _git = new GitSourceControlService(_root);

        // A second commit, so there is something to compare against.
        await File.WriteAllTextAsync(
            Path.Combine(_root, "Program.vb"), "Module M\n    Dim x = 1\nEnd Module\n");

        await _git.StageAsync(["Program.vb"]);
        await _git.CommitAsync("Second");
    }

    public ValueTask DisposeAsync()
    {
        try { Directory.Delete(_root, true); } catch { }

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ACommitShowsWhatItChanged()
    {
        var log = await _git.GetLogAsync(10);

        var second = log.First(c => c.Message == "Second");

        var diff = await _git.GetCommitDiffAsync(second.Sha);

        Assert.Contains("Dim x = 1", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFirstCommitWorksToo()
    {
        // It has no parent, which is where a naive "HEAD~1" would fail.
        var log = await _git.GetLogAsync(10);

        var first = log.First(c => c.Message == "First");

        var diff = await _git.GetCommitDiffAsync(first.Sha);

        Assert.Contains("Module M", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TwoCommitsShowWhatLiesBetween()
    {
        var log = await _git.GetLogAsync(10);

        var first = log.First(c => c.Message == "First");
        var second = log.First(c => c.Message == "Second");

        var diff = await _git.GetCommitDiffAsync(second.Sha, against: first.Sha);

        Assert.Contains("Dim x = 1", diff, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ItListsTheFilesACommitTouched()
    {
        var log = await _git.GetLogAsync(10);

        var second = log.First(c => c.Message == "Second");

        var files = await _git.GetCommitFilesAsync(second.Sha);

        Assert.Contains("Program.vb", files);
    }

    [Fact]
    public async Task AnUnknownCommitGivesNothingRatherThanThrowing()
    {
        Assert.Equal("", await _git.GetCommitDiffAsync("nosuchcommit"));
        Assert.Empty(await _git.GetCommitFilesAsync("nosuchcommit"));
    }

    [Fact]
    public async Task AnEmptyHashIsIgnored()
    {
        Assert.Equal("", await _git.GetCommitDiffAsync(""));
    }
}
