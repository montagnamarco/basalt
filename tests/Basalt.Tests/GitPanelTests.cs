using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>The panel that shows what would go into the next commit.</summary>
public class GitChangesPanelTests
{
    private static FileChange Change(string path, bool staged, FileChangeKind kind = FileChangeKind.Modified) =>
        new(path, kind, staged);

    [AvaloniaFact]
    public void SeparatesStagedChangesFromTheRest()
    {
        var panel = new GitChangesPanel();

        panel.Show("main", [
            Change("A.vb", staged: true),
            Change("B.vb", staged: false),
            Change("C.vb", staged: false)
        ]);

        Assert.Single(panel.Staged);
        Assert.Equal(2, panel.Unstaged.Count);
    }

    [AvaloniaFact]
    public void MarksEachChangeWithTheLetterGitUses()
    {
        var panel = new GitChangesPanel();

        panel.Show("main", [
            Change("A.vb", false, FileChangeKind.Added),
            Change("B.vb", false, FileChangeKind.Deleted),
            Change("C.vb", false, FileChangeKind.Untracked)
        ]);

        Assert.Equal(["A", "D", "?"], panel.Unstaged.Select(r => r.Marker));
    }

    [AvaloniaFact]
    public void AsksToCommitWithTheMessageTyped()
    {
        var panel = new GitChangesPanel();
        panel.Show("main", [Change("A.vb", staged: true)]);

        CommitRequest? asked = null;
        panel.CommitRequested += (_, request) => asked = request;

        panel.CommitMessage = "a change";
        panel.RequestCommitForTests();

        Assert.Equal("a change", asked!.Message);
        Assert.False(asked.Amend);
    }

    [AvaloniaFact]
    public void SaysWhenTheCommitShouldReplaceThePreviousOne()
    {
        var panel = new GitChangesPanel();

        CommitRequest? asked = null;
        panel.CommitRequested += (_, request) => asked = request;

        panel.CommitMessage = "a better message";
        panel.IsAmending = true;
        panel.RequestCommitForTests();

        Assert.True(asked!.Amend);
    }

    [AvaloniaFact]
    public void AsksForThePreviousMessageWhenAmendIsTicked()
    {
        // Amending is usually about correcting that message, so retyping it
        // would be busywork.
        var panel = new GitChangesPanel();

        var asked = 0;
        panel.AmendRequested += (_, _) => asked++;

        panel.IsAmending = true;

        Assert.Equal(1, asked);
    }

    [AvaloniaFact]
    public void StopsAmendingOnceTheCommitIsMade()
    {
        // Left ticked, the next commit would silently replace the one just made.
        var panel = new GitChangesPanel();
        panel.IsAmending = true;

        panel.ClearMessage();

        Assert.False(panel.IsAmending);
    }

    [AvaloniaFact]
    public void RefusesToCommitWithoutAMessage()
    {
        // An empty message would be rejected by git anyway, with a worse error.
        var panel = new GitChangesPanel();

        var asked = 0;
        panel.CommitRequested += (_, _) => asked++;

        panel.CommitMessage = "   ";
        panel.RequestCommitForTests();

        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public void EmptiesTheMessageOnceCommitted()
    {
        var panel = new GitChangesPanel();
        panel.CommitMessage = "done";

        panel.ClearMessage();

        Assert.Equal("", panel.CommitMessage);
    }

    [AvaloniaFact]
    public void AsksToStageAndUnstageAFile()
    {
        var panel = new GitChangesPanel();
        panel.Show("main", [Change("A.vb", staged: false), Change("B.vb", staged: true)]);

        FileChange? staged = null;
        FileChange? unstaged = null;

        panel.StageRequested += (_, change) => staged = change;
        panel.UnstageRequested += (_, change) => unstaged = change;

        panel.StageForTests(panel.Unstaged[0]);
        panel.UnstageForTests(panel.Staged[0]);

        Assert.Equal("A.vb", staged!.Path);
        Assert.Equal("B.vb", unstaged!.Path);
    }

    [AvaloniaFact]
    public void CopesWithADetachedHead()
    {
        var panel = new GitChangesPanel();

        panel.Show(null, []);

        Assert.Empty(panel.Staged);
        Assert.Empty(panel.Unstaged);
    }
}

/// <summary>The history panel, and the lanes it draws.</summary>
public class GitHistoryPanelTests
{
    private static CommitInfo Commit(string sha, params string[] parents) =>
        new(sha, "Test", $"work on {sha}", DateTimeOffset.UnixEpoch) { Parents = parents };

    [AvaloniaFact]
    public void LaysOutEveryCommitItIsGiven()
    {
        var panel = new GitHistoryPanel();

        panel.Show([Commit("c", "b"), Commit("b", "a"), Commit("a")]);

        Assert.Equal(3, panel.Rows.Count);
    }

    [AvaloniaFact]
    public void GivesEachLaneItsOwnColour()
    {
        Assert.NotEqual(GitHistoryPanel.ColourForLane(0), GitHistoryPanel.ColourForLane(1));
    }

    [AvaloniaFact]
    public void ReusesColoursOnceTheyRunOut()
    {
        // A history wide enough to exhaust the palette should still draw.
        Assert.Equal(GitHistoryPanel.ColourForLane(0), GitHistoryPanel.ColourForLane(6));
    }

    [AvaloniaFact]
    public void EmptiesWhenAskedTo()
    {
        var panel = new GitHistoryPanel();
        panel.Show([Commit("a")]);

        panel.Clear();

        Assert.Empty(panel.Rows);
    }

    [AvaloniaFact]
    public void ShowsAnEmptyHistoryWithoutComplaining()
    {
        var panel = new GitHistoryPanel();

        panel.Show([]);

        Assert.Empty(panel.Rows);
    }
}
