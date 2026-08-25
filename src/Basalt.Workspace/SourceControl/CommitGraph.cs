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
    int LaneCount);

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
            var lane = lanes.IndexOf(commit.Sha);

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

            Trim(lanes);

            rows.Add(new GraphRow(commit, lane, edges, Math.Max(lanes.Count, lane + 1)));
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
