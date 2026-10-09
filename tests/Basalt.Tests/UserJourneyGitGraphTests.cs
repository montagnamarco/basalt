using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Services;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Basalt.Workspace.SourceControl;

namespace Basalt.Tests;

/// <summary>
/// The commit history graph, against a real repository with a divergence, a
/// merge, and a second branch left open - the shape that makes a graph worth
/// drawing at all - with the drawing itself proved by reading pixels rather
/// than trusting that the right geometry was asked for.
///
/// Topology built (newest first, the order git and the panel both use):
/// <code>
/// MainWork3   (main)
/// SecondWork  (second, still open)
/// MainWork2   (main, after the merge)
/// MergeFeature (merges "feature" into main - two parents)
/// MainWork    (main, before the merge)
/// FeatureWork (feature)
/// Base
/// Root
/// </code>
/// </summary>
public sealed class UserJourneyGitGraphTests : IDisposable
{
    private readonly string _root =
        Directory.CreateTempSubdirectory("basalt-git-graph-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Run(params string[] arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.Environment["GIT_CONFIG_NOSYSTEM"] = "1";
        info.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(_root, ".no-global-gitconfig");
        foreach (var arg in arguments) info.ArgumentList.Add(arg);

        using var process = System.Diagnostics.Process.Start(info)!;
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }

    private void Commit(string fileName, string content, string message)
    {
        File.WriteAllText(Path.Combine(_root, fileName), content);
        Run("add", "-A");
        Run("commit", "-q", "-m", message);
    }

    /// <summary>
    /// Builds the topology documented on the class, with real git commands:
    /// a branch that diverges and is merged back with an explicit merge
    /// commit, plus a second branch forked after the merge and never closed.
    /// </summary>
    private async Task<IReadOnlyList<CommitInfo>> BuildRepositoryAsync()
    {
        Run("init", "-q", "-b", "main");
        Run("config", "user.email", "test@example.com");
        Run("config", "user.name", "Basalt-Test");

        Commit("Root.vb", "root", "Root");
        Commit("Base.vb", "base", "Base");

        Run("branch", "feature");
        Run("checkout", "-q", "feature");
        Commit("Feature.vb", "feature", "FeatureWork");

        Run("checkout", "-q", "main");
        Commit("MainWork.vb", "main work", "MainWork");

        Run("merge", "feature", "-q", "-m", "MergeFeature", "--no-ff");

        Commit("MainWork2.vb", "main work 2", "MainWork2");

        Run("branch", "second");
        Run("checkout", "-q", "second");
        Commit("SecondWork.vb", "second work", "SecondWork");

        Run("checkout", "-q", "main");
        Commit("MainWork3.vb", "main work 3", "MainWork3");

        var git = new GitSourceControlService(_root);
        return await git.GetLogAsync(50);
    }

    // ------------------------------------------------------------------
    // The lane and edge arithmetic, against the real log.
    // ------------------------------------------------------------------
    [Fact]
    public async Task LaysOutTheRealHistoryWithAMergeAPassThroughAndAReusedLane()
    {
        var commits = await BuildRepositoryAsync();
        var bySubject = commits.ToDictionary(c => c.Subject);

        Assert.Equal(8, commits.Count);
        Assert.Equal(
            ["MainWork3", "SecondWork", "MainWork2", "MergeFeature",
             "MainWork", "FeatureWork", "Base", "Root"],
            commits.Select(c => c.Subject));

        var rows = CommitGraph.Build(commits).ToDictionary(r => r.Commit.Subject);

        // The fork: "MainWork3" and "SecondWork" are both children of
        // "MainWork2", so each gets its own lane the moment the second one
        // is seen.
        Assert.Equal(0, rows["MainWork3"].Lane);
        Assert.Equal(1, rows["SecondWork"].Lane);
        Assert.False(rows["MainWork3"].Continues);
        Assert.False(rows["SecondWork"].Continues);

        // Lane 1 (second's) converges back into lane 0 exactly at
        // "MainWork2" - the fork's shared parent - and is freed there: no
        // edge continues it further down.
        Assert.Equal(0, rows["MainWork2"].Lane);
        Assert.DoesNotContain(rows["MainWork2"].Edges, e => e.FromLane == 1 || e.ToLane == 1);
        Assert.Equal(1, rows["MainWork2"].LaneCount);

        // The merge: one edge keeps its own lane going to "MainWork", the
        // other bends across to a newly reused lane 1 - the very slot
        // "second" just freed - for "FeatureWork".
        var merge = rows["MergeFeature"];
        Assert.True(merge.Commit.IsMerge);
        Assert.Equal(0, merge.Lane);
        Assert.Contains(merge.Edges, e => e.FromLane == 0 && e.ToLane == 0);
        Assert.Contains(merge.Edges, e => e.FromLane == 0 && e.ToLane == 1);

        // "MainWork" is the row between the merge and "FeatureWork": it
        // belongs to lane 0, but lane 1 (feature, still waiting for "Base")
        // must still be drawn straight through this row, or the feature
        // branch's own line would break here.
        Assert.Equal(0, rows["MainWork"].Lane);
        Assert.Contains(rows["MainWork"].Edges, e => e.FromLane == 1 && e.ToLane == 1);

        // "FeatureWork" itself sits in lane 1, continuing what "MergeFeature"
        // left waiting there.
        Assert.Equal(1, rows["FeatureWork"].Lane);
        Assert.True(rows["FeatureWork"].Continues);

        // Both branches converge on "Base", which frees lane 1 for good:
        // nothing in this history reuses it afterwards.
        Assert.Equal(1, rows["Base"].LaneCount);
    }

    // ------------------------------------------------------------------
    // The same history, drawn - proved with pixels rather than trusting the
    // geometry that was asked for.
    // ------------------------------------------------------------------
    [AvaloniaFact]
    public async Task DrawsAContinuousLineACurvedMergeAndStableColoursPerLane()
    {
        var commits = await BuildRepositoryAsync();

        var panel = new GitHistoryPanel();
        panel.Show(commits);

        var window = new Window { Width = 500, Height = 400, Content = panel };
        window.Show();
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
        }

        try
        {
            var cells = panel.GetVisualDescendants()
                .OfType<Control>()
                .Where(c => c.GetType().Name == "GraphCell")
                .ToList();

            Assert.Equal(commits.Count, cells.Count);

            // Each row's GraphCell, in the same top-to-bottom order the list
            // shows them, with its absolute position in the panel so pixels
            // can be sampled by lane rather than by guesswork.
            var bySubject = new Dictionary<string, (Control Cell, Point Origin)>();

            foreach (var cell in cells)
            {
                var origin = cell.TranslatePoint(new Point(0, 0), panel) ?? default;

                // The row this cell belongs to is whichever one its vertical
                // position falls at: rows are listed top to bottom in order,
                // and matching by position is what ties a realized cell back
                // to the commit it was built for.
                bySubject[FindSubjectAt(panel, origin)] = (cell, origin);
            }

            const double LaneWidth = 14;
            static double X(Point origin, int lane) => origin.X + (lane + 0.5) * LaneWidth;

            var raster = Raster.Of(panel);

            // Lane 0's colour and lane 1's colour, read from two rows where
            // each is unambiguously that lane's own dot.
            var (mainCell, mainOrigin) = bySubject["MainWork3"];
            var (secondCell, secondOrigin) = bySubject["SecondWork"];

            var lane0Colour = raster.At((int)X(mainOrigin, 0), (int)(mainOrigin.Y + mainCell.Bounds.Height / 2));
            var lane1Colour = raster.At((int)X(secondOrigin, 1), (int)(secondOrigin.Y + secondCell.Bounds.Height / 2));

            Assert.NotEqual(lane0Colour, lane1Colour);

            // Lane 0's colour is the same two rows apart, on "MainWork2" -
            // colours are stable per lane, not per row.
            var (mainWork2Cell, mainWork2Origin) = bySubject["MainWork2"];
            var lane0Again = raster.At(
                (int)X(mainWork2Origin, 0), (int)(mainWork2Origin.Y + mainWork2Cell.Bounds.Height / 2));
            Assert.Equal(lane0Colour, lane0Again);

            // "MainWork" is not where lane 1 lives - its own dot is lane 0 -
            // yet lane 1's line (feature, still open at this row) must still
            // show as ink running the full height of this row's cell, or the
            // branch's own line would look broken here.
            var (mainWorkCell, mainWorkOrigin) = bySubject["MainWork"];
            var background = raster.At(2, 2); // a corner with nothing drawn on it

            var topOfLane1 = raster.At((int)X(mainWorkOrigin, 1), (int)mainWorkOrigin.Y + 1);
            var bottomOfLane1 = raster.At(
                (int)X(mainWorkOrigin, 1), (int)(mainWorkOrigin.Y + mainWorkCell.Bounds.Height) - 1);

            Assert.NotEqual(background, topOfLane1);
            Assert.NotEqual(background, bottomOfLane1);

            // The merge row: a curved edge bends from lane 0 towards lane 1.
            // Somewhere strictly between the two lanes' centres the bend
            // must have put ink down - a straight pair of vertical lines at
            // lane 0 and lane 1 alone would leave that middle column bare.
            var (mergeCell, mergeOrigin) = bySubject["MergeFeature"];
            var betweenX = (int)((X(mergeOrigin, 0) + X(mergeOrigin, 1)) / 2);

            var sawInkBetweenLanes = false;
            for (var y = (int)mergeOrigin.Y; y < mergeOrigin.Y + mergeCell.Bounds.Height; y++)
            {
                if (!Equals(raster.At(betweenX, y), background)) { sawInkBetweenLanes = true; break; }
            }

            Assert.True(sawInkBetweenLanes, "expected ink between the two lanes on the merge row");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Which commit a cell at this vertical position belongs to, found by
    /// matching the panel's row order against the realized cells' order
    /// rather than any identity the cell itself carries.
    /// </summary>
    private static string FindSubjectAt(GitHistoryPanel panel, Point origin)
    {
        // Rows are the same height and stacked top to bottom in list order,
        // after whatever margin the list's own first item carries: the row
        // whose accumulated position is closest to this cell's own top is
        // the one it was built for.
        var cells = panel.GetVisualDescendants()
            .OfType<Control>()
            .Where(c => c.GetType().Name == "GraphCell")
            .ToList();

        var ordered = cells
            .Select(c => c.TranslatePoint(new Point(0, 0), panel)?.Y ?? 0)
            .OrderBy(y => y)
            .ToList();

        var index = ordered.FindIndex(y => Math.Abs(y - origin.Y) < 1.0);
        if (index < 0) throw new InvalidOperationException($"no row found at y={origin.Y}");

        return panel.Rows[index].Commit.Subject;
    }
}
