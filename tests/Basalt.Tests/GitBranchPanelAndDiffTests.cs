using Avalonia.Headless.XUnit;
using Basalt.Core.Services;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>The branch list and the commands on it.</summary>
public class BranchPanelTests
{
    private static BranchInfo Branch(
        string name, bool current = false, bool remote = false, int ahead = 0, int behind = 0) =>
        new(name, current, remote, Ahead: ahead, Behind: behind);

    [AvaloniaFact]
    public void KeepsLocalAndRemoteBranchesApart()
    {
        // Only a local branch can be checked out; mixing them invites the user
        // to try something that will fail.
        var panel = new BranchPanel();

        panel.Show([
            Branch("main", current: true),
            Branch("feature"),
            Branch("origin/main", remote: true)
        ]);

        Assert.Equal(2, panel.Local.Count);
        Assert.Single(panel.Remote);
    }

    [AvaloniaFact]
    public void MarksTheCurrentBranch()
    {
        var panel = new BranchPanel();
        panel.Show([Branch("main", current: true), Branch("other")]);

        Assert.StartsWith("●", panel.Local.Single(r => r.Branch.Name == "main").Display);
    }

    [AvaloniaFact]
    public void ShowsHowFarABranchHasDrifted()
    {
        var panel = new BranchPanel();
        panel.Show([Branch("main", ahead: 2, behind: 3)]);

        var display = panel.Local[0].Display;

        Assert.Contains("↑2", display);
        Assert.Contains("↓3", display);
    }

    [AvaloniaFact]
    public void SaysNothingAboutDriftWhenThereIsNone()
    {
        var panel = new BranchPanel();
        panel.Show([Branch("main")]);

        Assert.DoesNotContain("↑", panel.Local[0].Display);
        Assert.DoesNotContain("↓", panel.Local[0].Display);
    }

    [AvaloniaFact]
    public void AsksToCreateABranchWithTheNameTyped()
    {
        var panel = new BranchPanel();

        string? asked = null;
        panel.CreateRequested += (_, name) => asked = name;

        panel.NewBranchName = "feature/login";
        panel.RequestCreate();

        Assert.Equal("feature/login", asked);
    }

    [AvaloniaFact]
    public void RefusesToCreateABranchWithoutAName()
    {
        var panel = new BranchPanel();

        var asked = 0;
        panel.CreateRequested += (_, _) => asked++;

        panel.NewBranchName = "   ";
        panel.RequestCreate();

        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public void EmptiesTheNameBoxOnceTheBranchIsAskedFor()
    {
        var panel = new BranchPanel();
        panel.NewBranchName = "feature";

        panel.RequestCreate();

        Assert.Equal("", panel.NewBranchName);
    }

    [AvaloniaFact]
    public void AsksToCheckOutMergeAndDelete()
    {
        var panel = new BranchPanel();
        panel.Show([Branch("feature")]);

        BranchInfo? checkedOut = null, merged = null, deleted = null;

        panel.CheckoutRequested += (_, b) => checkedOut = b;
        panel.MergeRequested += (_, b) => merged = b;
        panel.DeleteRequested += (_, b) => deleted = b;

        var row = panel.Local[0];
        panel.CheckoutForTests(row);
        panel.MergeForTests(row);
        panel.DeleteForTests(row);

        Assert.Equal("feature", checkedOut!.Name);
        Assert.Equal("feature", merged!.Name);
        Assert.Equal("feature", deleted!.Name);
    }
}

/// <summary>Reading a unified diff.</summary>
public class DiffViewTests
{
    private const string Sample = """
        diff --git a/A.vb b/A.vb
        index 1234567..89abcde 100644
        --- a/A.vb
        +++ b/A.vb
        @@ -3,7 +3,8 @@ Module A
             Sub Main()
                 Dim total As Integer = 0
        -        total = 1
        +        total = 2
        +        Console.WriteLine(total)
             End Sub
        """;

    [AvaloniaFact]
    public void ReadsAddedAndRemovedLines()
    {
        var lines = DiffView.Parse(Sample);

        Assert.Equal(2, lines.Count(l => l.Kind == DiffLineKind.Added));
        Assert.Single(lines, l => l.Kind == DiffLineKind.Removed);
    }

    [AvaloniaFact]
    public void TreatsGitsPreambleAsHeaders()
    {
        var lines = DiffView.Parse(Sample);

        Assert.Contains(lines, l => l.Kind == DiffLineKind.Header && l.Text.StartsWith("diff --git"));
        Assert.Contains(lines, l => l.Kind == DiffLineKind.Header && l.Text.StartsWith("@@"));
    }

    [AvaloniaFact]
    public void NumbersLinesFromTheHunkHeader()
    {
        // A diff omits unchanged regions, so counting from the top of the file
        // would misplace every line after the first hunk.
        var lines = DiffView.Parse(Sample);

        var firstContext = lines.First(l => l.Kind == DiffLineKind.Context);

        Assert.Equal(3, firstContext.OldLine);
        Assert.Equal(3, firstContext.SoNewLine);
    }

    [AvaloniaFact]
    public void GivesAnAddedLineOnlyANewNumber()
    {
        var added = DiffView.Parse(Sample).First(l => l.Kind == DiffLineKind.Added);

        Assert.Null(added.OldLine);
        Assert.NotNull(added.SoNewLine);
    }

    [AvaloniaFact]
    public void GivesARemovedLineOnlyAnOldNumber()
    {
        var removed = DiffView.Parse(Sample).First(l => l.Kind == DiffLineKind.Removed);

        Assert.NotNull(removed.OldLine);
        Assert.Null(removed.SoNewLine);
    }

    [AvaloniaFact]
    public void ReadsAnEmptyDiffAsNothing()
    {
        Assert.Empty(DiffView.Parse(""));
    }

    [AvaloniaFact]
    public void ShowsAndClearsWhatItWasGiven()
    {
        var view = new DiffView();

        view.Show(Sample);
        Assert.NotEmpty(view.Lines);

        view.Clear();
        Assert.Empty(view.Lines);
    }

    [AvaloniaFact]
    public void CopesWithTheNoNewlineMarker()
    {
        var lines = DiffView.Parse("@@ -1,1 +1,1 @@\n-a\n+b\n\\ No newline at end of file");

        Assert.Contains(lines, l => l.Text.StartsWith("\\ No newline"));
    }
}
