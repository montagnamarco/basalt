using System.Diagnostics;
using Basalt.Core.Services;
using Basalt.Workspace;
using Basalt.Workspace.SourceControl;

namespace Basalt.Tests;

/// <summary>
/// Branches, and the history graph drawn from them.
///
/// The repository is built by running git, so the parsing is checked against
/// git's real output rather than against a sample that could drift from it.
/// </summary>
public sealed class GitBranchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-git-branch", Guid.NewGuid().ToString("N"));

    private readonly GitSourceControlService _git;

    public GitBranchTests()
    {
        Directory.CreateDirectory(_root);

        Run("init", "-b", "main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Test");

        _git = new GitSourceControlService(_root);
    }

    private void Run(params string[] arguments)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        Process.Start(info)!.WaitForExit();
    }

    private void Commit(string fileName, string content, string message)
    {
        File.WriteAllText(Path.Combine(_root, fileName), content);
        Run("add", "-A");
        Run("commit", "-m", message);
    }

    [Fact]
    public async Task ListsTheBranchAndMarksTheCurrentOne()
    {
        Commit("A.vb", "Module A", "first");
        Run("branch", "feature");

        var branches = await _git.GetBranchesAsync();

        Assert.Equal(2, branches.Count);
        Assert.True(branches.Single(b => b.Name == "main").IsCurrent);
        Assert.False(branches.Single(b => b.Name == "feature").IsCurrent);
    }

    [Fact]
    public async Task DoesNotMistakeASlashInANameForARemote()
    {
        // "feature/login" is local; only a name whose first segment is a
        // configured remote is a remote branch.
        Commit("A.vb", "Module A", "first");
        Run("branch", "feature/login");

        var branches = await _git.GetBranchesAsync();

        Assert.False(branches.Single(b => b.Name == "feature/login").IsRemote);
    }

    [Fact]
    public async Task CreatesABranchAndSwitchesToIt()
    {
        Commit("A.vb", "Module A", "first");

        await _git.CreateBranchAsync("work");

        Assert.Equal("work", await _git.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task CreatesABranchWithoutLeavingTheCurrentOne()
    {
        Commit("A.vb", "Module A", "first");

        await _git.CreateBranchAsync("later", checkout: false);

        Assert.Equal("main", await _git.GetCurrentBranchAsync());
        Assert.Contains(await _git.GetBranchesAsync(), b => b.Name == "later");
    }

    [Fact]
    public async Task SwitchesBetweenBranches()
    {
        Commit("A.vb", "Module A", "first");
        Run("branch", "other");

        await _git.CheckoutAsync("other");

        Assert.Equal("other", await _git.GetCurrentBranchAsync());
    }

    [Fact]
    public async Task DeletesABranch()
    {
        Commit("A.vb", "Module A", "first");
        Run("branch", "spare");

        await _git.DeleteBranchAsync("spare");

        Assert.DoesNotContain(await _git.GetBranchesAsync(), b => b.Name == "spare");
    }

    [Fact]
    public async Task ExplainsWhyABranchCouldNotBeDeleted()
    {
        Commit("A.vb", "Module A", "first");

        // Deleting the branch you are on is refused, and the reason should
        // reach the user rather than being swallowed.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _git.DeleteBranchAsync("main"));

        Assert.NotEmpty(error.Message);
        Assert.DoesNotContain("The git command failed", error.Message);
    }

    [Fact]
    public async Task MergesABranchBackIn()
    {
        Commit("A.vb", "Module A", "first");

        await _git.CreateBranchAsync("side");
        Commit("B.vb", "Module B", "on the side");

        await _git.CheckoutAsync("main");
        await _git.MergeAsync("side");

        Assert.True(File.Exists(Path.Combine(_root, "B.vb")));
    }

    [Fact]
    public async Task ThrowsAwayLocalChangesToAFile()
    {
        Commit("A.vb", "original", "first");

        await File.WriteAllTextAsync(Path.Combine(_root, "A.vb"), "changed");

        await _git.DiscardChangesAsync(["A.vb"]);

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "A.vb")));
    }

    [Fact]
    public async Task ThrowsAwayAChangeThatWasAlreadyStaged()
    {
        Commit("A.vb", "original", "first");

        await File.WriteAllTextAsync(Path.Combine(_root, "A.vb"), "changed");
        Run("add", "A.vb");

        await _git.DiscardChangesAsync(["A.vb"]);

        Assert.Equal("original", await File.ReadAllTextAsync(Path.Combine(_root, "A.vb")));
        Assert.Empty(await _git.GetStatusAsync());
    }

    [Fact]
    public async Task ReadsTheParentsOfEachCommit()
    {
        Commit("A.vb", "one", "first");
        Commit("A.vb", "two", "second");

        var log = await _git.GetLogAsync(10);

        Assert.Empty(log.Single(c => c.Message == "first").Parents);
        Assert.Single(log.Single(c => c.Message == "second").Parents);
    }

    [Fact]
    public async Task RecognisesAMergeCommit()
    {
        Commit("A.vb", "one", "first");

        await _git.CreateBranchAsync("side");
        Commit("B.vb", "side work", "on the side");

        await _git.CheckoutAsync("main");
        Commit("C.vb", "main work", "on main");

        // Divergent histories force a real merge commit rather than a fast-forward.
        Run("merge", "side", "-m", "merged");

        var log = await _git.GetLogAsync(10);
        var merge = log.Single(c => c.Message == "merged");

        Assert.True(merge.IsMerge);
        Assert.Equal(2, merge.Parents.Count);
    }

    [Fact]
    public async Task ReportsWhichBranchesPointAtACommit()
    {
        Commit("A.vb", "one", "first");

        var log = await _git.GetLogAsync(10);

        Assert.Contains("main", log[0].Refs);
    }

    [Fact]
    public async Task IncludesCommitsFromOtherBranches()
    {
        // A graph of the current branch alone would hide the branching it
        // exists to show.
        Commit("A.vb", "one", "first");

        await _git.CreateBranchAsync("side");
        Commit("B.vb", "side work", "only on the side");

        await _git.CheckoutAsync("main");

        Assert.Contains(await _git.GetLogAsync(10), c => c.Message == "only on the side");
    }

    [Fact]
    public async Task SeparatesTheSubjectFromTheRestOfTheMessage()
    {
        Commit("A.vb", "one", "a subject");

        var log = await _git.GetLogAsync(10);

        Assert.Equal("a subject", log[0].Subject);
    }

    [Fact]
    public async Task ReplacesTheLastCommitWhenAmending()
    {
        Commit("A.vb", "one", "frist");

        await _git.CommitAsync("first", amend: true);

        var log = await _git.GetLogAsync(10);

        // Amending replaces rather than adds: the typo should be gone, not
        // sitting in the history above the correction.
        Assert.Single(log);
        Assert.Equal("first", log[0].Subject);
    }

    [Fact]
    public async Task IncludesAForgottenFileWhenAmending()
    {
        Commit("A.vb", "one", "first");

        await File.WriteAllTextAsync(Path.Combine(_root, "B.vb"), "two");
        Run("add", "B.vb");

        await _git.CommitAsync("first", amend: true);

        Assert.Single(await _git.GetLogAsync(10));
        Assert.Empty(await _git.GetStatusAsync());
    }

    [Fact]
    public async Task ReadsTheMessageOfTheLastCommit()
    {
        Commit("A.vb", "one", "a message");

        Assert.Equal("a message", await _git.GetLastCommitMessageAsync());
    }

    [Fact]
    public async Task HasNoLastMessageInAnEmptyRepository()
    {
        // Nothing to amend before the first commit.
        Assert.Null(await _git.GetLastCommitMessageAsync());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Laying commits out into lanes for drawing.</summary>
public class CommitGraphTests
{
    private static CommitInfo Commit(string sha, params string[] parents) =>
        new(sha, "Test", $"commit {sha}", DateTimeOffset.UnixEpoch)
        {
            Parents = parents
        };

    [Fact]
    public void PlacesAStraightHistoryInOneLane()
    {
        var rows = CommitGraph.Build([
            Commit("c", "b"),
            Commit("b", "a"),
            Commit("a")
        ]);

        Assert.All(rows, row => Assert.Equal(0, row.Lane));
        Assert.All(rows, row => Assert.Equal(1, row.LaneCount));
    }

    [Fact]
    public void GivesADivergentBranchItsOwnLane()
    {
        // Two children of "a", neither descended from the other.
        var rows = CommitGraph.Build([
            Commit("c", "a"),
            Commit("b", "a"),
            Commit("a")
        ]);

        Assert.Equal(0, rows[0].Lane);
        Assert.Equal(1, rows[1].Lane);

        // The count is per row: the second lane only exists from the row that
        // claims it onwards, which is what keeps the graph as narrow as the
        // history actually is at each point.
        Assert.Equal(2, rows[1].LaneCount);
    }

    [Fact]
    public void BringsAMergesSecondParentIntoItsOwnLane()
    {
        var rows = CommitGraph.Build([
            Commit("m", "a", "b"),
            Commit("b", "base"),
            Commit("a", "base"),
            Commit("base")
        ]);

        var merge = rows[0];

        // One edge continues down the merge's own lane, another reaches across
        // to where the second parent will be drawn.
        Assert.Contains(merge.Edges, e => e.FromLane == 0 && e.ToLane == 0);
        Assert.Contains(merge.Edges, e => e.ToLane != 0);
    }

    [Fact]
    public void ReusesALaneOnceItsBranchHasBeenDrawn()
    {
        // After the side branch merges back, the history is linear again and
        // should not keep drifting rightwards.
        var rows = CommitGraph.Build([
            Commit("later", "m"),
            Commit("m", "a", "b"),
            Commit("b", "base"),
            Commit("a", "base"),
            Commit("base"),
            Commit("older")
        ]);

        Assert.Equal(1, rows[^1].LaneCount);
    }

    [Fact]
    public void EndsTheLaneOfACommitWithNoParents()
    {
        var rows = CommitGraph.Build([Commit("only")]);

        Assert.Empty(rows[0].Edges);
    }

    [Fact]
    public void HandlesAnEmptyHistory()
    {
        Assert.Empty(CommitGraph.Build([]));
    }

    [Fact]
    public void KeepsEveryCommitItWasGiven()
    {
        var commits = new[]
        {
            Commit("d", "c"), Commit("c", "a", "b"), Commit("b", "a"), Commit("a")
        };

        var rows = CommitGraph.Build(commits);

        Assert.Equal(commits.Length, rows.Count);
        Assert.Equal(commits.Select(c => c.Sha), rows.Select(r => r.Commit.Sha));
    }
}
