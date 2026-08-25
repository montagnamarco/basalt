using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>Marking the lines that differ from the last commit.</summary>
public class GitChangeMarginTests
{
    private static IReadOnlyDictionary<int, LineChange> FromDiff(string diff) =>
        GitChangeMargin.FromDiff(diff);

    [AvaloniaFact]
    public void MarksAnAddedLine()
    {
        var changes = FromDiff("@@ -1,2 +1,3 @@\n one\n+two\n three");

        Assert.Equal(LineChange.Added, changes[2]);
    }

    [AvaloniaFact]
    public void MarksAReplacedLineAsModified()
    {
        // A removal followed by an addition is one change to a reader, not two
        // events, and should be marked as such.
        var changes = FromDiff("@@ -1,2 +1,2 @@\n one\n-two\n+deux");

        Assert.Equal(LineChange.Modified, changes[2]);
    }

    [AvaloniaFact]
    public void MarksWhereALineWasRemoved()
    {
        // A removed line has no line of its own left to mark, so the marker
        // goes on the line that took its place.
        var changes = FromDiff("@@ -1,3 +1,2 @@\n one\n-two\n three");

        Assert.Equal(LineChange.Removed, changes[2]);
    }

    [AvaloniaFact]
    public void NumbersLinesFromTheHunkHeader()
    {
        // Counting from the top of the file would misplace every hunk but the
        // first, since a diff omits what did not change.
        var changes = FromDiff("@@ -40,2 +40,3 @@\n forty\n+forty one");

        Assert.True(changes.ContainsKey(41));
        Assert.False(changes.ContainsKey(2));
    }

    [AvaloniaFact]
    public void ReadsSeveralHunks()
    {
        var changes = FromDiff(
            "@@ -1,2 +1,3 @@\n one\n+added\n@@ -20,2 +21,3 @@\n twenty\n+also added");

        Assert.Equal(LineChange.Added, changes[2]);
        Assert.Equal(LineChange.Added, changes[22]);
    }

    [AvaloniaFact]
    public void IgnoresGitsPreamble()
    {
        var changes = FromDiff(
            "diff --git a/A.vb b/A.vb\nindex 1..2 100644\n--- a/A.vb\n+++ b/A.vb\n" +
            "@@ -1,1 +1,2 @@\n one\n+two");

        // The "+++ b/A.vb" line starts with a plus but is not an added line.
        Assert.Single(changes);
        Assert.Equal(LineChange.Added, changes[2]);
    }

    [AvaloniaFact]
    public void ReadsAnEmptyDiffAsNoChanges()
    {
        Assert.Empty(FromDiff(""));
    }

    [AvaloniaFact]
    public void ShowsAndClearsWhatItWasGiven()
    {
        var margin = new GitChangeMargin();

        margin.Show(FromDiff("@@ -1,1 +1,2 @@\n one\n+two"));
        Assert.NotEmpty(margin.Changes);

        margin.Clear();
        Assert.Empty(margin.Changes);
    }
}
