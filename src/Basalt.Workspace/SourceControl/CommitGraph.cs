using Basalt.Core.Services;

namespace Basalt.Workspace.SourceControl;

/// <summary>A line drawn from one row to the next, in lane terms.</summary>
public sealed record GraphEdge(int FromLane, int ToLane);

/// <summary>
/// One commit placed in the graph.
///
/// <paramref name="Lane"/> is the column the dot sits in; <paramref name="Edges"/>
/// are the lines running down to the row below.
/// </summary>
public sealed record GraphRow(
    CommitInfo Commit,
    int Lane,
    IReadOnlyList<GraphEdge> Edges,
    int LaneCount)
{
    /// <summary>
    /// Whether some row above left a line waiting for this one, in this same
    /// lane.
    ///
    /// False for a branch's first commit in the list: nothing above has
    /// claimed this lane yet, so a line drawn above the dot would dangle from
    /// nothing. True for every row after it in the same line of descent,
    /// where the row above really did leave a line to meet.
    /// </summary>
    public bool Continues { get; init; }
}

/// <summary>
/// Arranges commits into lanes so the history can be drawn.
///
/// The commits are walked newest first, keeping a list of lanes where each
/// lane holds the commit expected next in that line of descent. A commit takes
/// the lane that was waiting for it; a merge sends its extra parents into
/// further lanes; a lane whose commit has been drawn is freed for reuse, which
/// is what keeps a long history from drifting endlessly to the right.
/// </summary>
public static class CommitGraph
{
    public static IReadOnlyList<GraphRow> Build(IReadOnlyList<CommitInfo> commits)
    {
        var rows = new List<GraphRow>(commits.Count);

        // Lane slots: the sha each lane is currently waiting to draw, or null.
        var lanes = new List<string?>();

        foreach (var commit in commits)
        {
            // What every lane was waiting for coming into this row: both
            // whether this commit's own lane continues one from above, and
            // which other lanes need a line drawn through this row even
            // though it belongs to none of them.
            var before = lanes.ToList();

            var lane = lanes.IndexOf(commit.Sha);
            var continues = lane >= 0;

            if (lane < 0) lane = Claim(lanes, commit.Sha);

            // Lanes waiting for this same commit are merging back in here, and
            // must not keep waiting for a commit that has now been drawn.
            for (var i = 0; i < lanes.Count; i++)
                if (i != lane && lanes[i] == commit.Sha)
                    lanes[i] = null;

            // The lane continues along the first parent; other parents branch off.
            lanes[lane] = commit.Parents.Count > 0 ? commit.Parents[0] : null;

            var edges = new List<GraphEdge>();

            foreach (var parent in commit.Parents.Skip(1))
            {
                var existing = lanes.IndexOf(parent);
                var target = existing >= 0 ? existing : Claim(lanes, parent);

                edges.Add(new GraphEdge(lane, target));
            }

            if (commit.Parents.Count > 0) edges.Add(new GraphEdge(lane, lane));

            // Every other lane that was already carrying a line and still is
            // after this commit - unrelated to it - needs that line drawn
            // straight through this row too. Without this, a branch's line
            // breaks every time another branch's commit falls between two of
            // its own in the newest-first order.
            for (var i = 0; i < before.Count; i++)
            {
                if (i == lane || before[i] is null) continue;
                if (i < lanes.Count && lanes[i] is not null)
                    edges.Add(new GraphEdge(i, i));
            }

            Trim(lanes);

            rows.Add(new GraphRow(commit, lane, edges, Math.Max(lanes.Count, lane + 1))
            {
                Continues = continues
            });
        }

        return rows;
    }

    /// <summary>Puts a commit in the leftmost free lane, adding one if needed.</summary>
    private static int Claim(List<string?> lanes, string sha)
    {
        var free = lanes.IndexOf(null);

        if (free >= 0)
        {
            lanes[free] = sha;
            return free;
        }

        lanes.Add(sha);
        return lanes.Count - 1;
    }

    /// <summary>Drops empty lanes on the right so the graph does not grow without end.</summary>
    private static void Trim(List<string?> lanes)
    {
        while (lanes.Count > 0 && lanes[^1] is null) lanes.RemoveAt(lanes.Count - 1);
    }
}
