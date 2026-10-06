using System.Text;

namespace Basalt.Workspace.Ai;

/// <summary>
/// A unified diff between two texts, written the way git writes one so the
/// IDE's diff view can show it.
///
/// Computed here because the change under review is not in git yet: it is an
/// assistant's proposal that exists only in memory.
/// </summary>
public static class TextDiff
{
    private const int Context = 3;

    /// <summary>Above this many table cells the changed middle is shown as replaced wholesale.</summary>
    private const long MaxCells = 4_000_000;

    public static string Unified(string path, string before, string after)
    {
        var oldLines = Split(before);
        var newLines = Split(after);

        var ops = Diff(oldLines, newLines);

        if (ops.All(o => o.Kind == ' ')) return "";

        var text = new StringBuilder();
        text.Append("--- a/").Append(path).Append('\n');
        text.Append("+++ b/").Append(path).Append('\n');

        // Hunks: runs of changes with up to Context unchanged lines around
        // them, merged when their contexts touch.
        var i = 0;

        while (i < ops.Count)
        {
            while (i < ops.Count && ops[i].Kind == ' ') i++;
            if (i >= ops.Count) break;

            var start = Math.Max(0, i - Context);
            var end = i;
            var lastChange = i;

            while (end < ops.Count)
            {
                if (ops[end].Kind != ' ') lastChange = end;
                if (end - lastChange > Context * 2) break;
                end++;
            }

            end = Math.Min(ops.Count, lastChange + Context + 1);

            var oldStart = ops[start].OldIndex;
            var newStart = ops[start].NewIndex;
            var oldCount = 0;
            var newCount = 0;
            var body = new StringBuilder();

            for (var k = start; k < end; k++)
            {
                body.Append(ops[k].Kind).Append(ops[k].Text).Append('\n');
                if (ops[k].Kind != '+') oldCount++;
                if (ops[k].Kind != '-') newCount++;
            }

            text.Append($"@@ -{oldStart + (oldCount == 0 ? 0 : 1)},{oldCount} +{newStart + (newCount == 0 ? 0 : 1)},{newCount} @@\n");
            text.Append(body);

            i = end;
        }

        return text.ToString();
    }

    private readonly record struct Op(char Kind, string Text, int OldIndex, int NewIndex);

    private static string[] Split(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static List<Op> Diff(string[] a, string[] b)
    {
        var ops = new List<Op>();

        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix
               && a[a.Length - 1 - suffix] == b[b.Length - 1 - suffix]) suffix++;

        for (var k = 0; k < prefix; k++) ops.Add(new Op(' ', a[k], k, k));

        var n = a.Length - prefix - suffix;
        var m = b.Length - prefix - suffix;

        if ((long)(n + 1) * (m + 1) > MaxCells)
        {
            for (var k = 0; k < n; k++) ops.Add(new Op('-', a[prefix + k], prefix + k, prefix));
            for (var k = 0; k < m; k++) ops.Add(new Op('+', b[prefix + k], prefix + n, prefix + k));
        }
        else
        {
            // Longest common subsequence of the differing middle.
            var table = new int[n + 1, m + 1];

            for (var x = n - 1; x >= 0; x--)
                for (var y = m - 1; y >= 0; y--)
                    table[x, y] = a[prefix + x] == b[prefix + y]
                        ? table[x + 1, y + 1] + 1
                        : Math.Max(table[x + 1, y], table[x, y + 1]);

            int ox = 0, oy = 0;

            while (ox < n && oy < m)
            {
                if (a[prefix + ox] == b[prefix + oy])
                {
                    ops.Add(new Op(' ', a[prefix + ox], prefix + ox, prefix + oy));
                    ox++; oy++;
                }
                else if (table[ox + 1, oy] >= table[ox, oy + 1])
                {
                    ops.Add(new Op('-', a[prefix + ox], prefix + ox, prefix + oy));
                    ox++;
                }
                else
                {
                    ops.Add(new Op('+', b[prefix + oy], prefix + ox, prefix + oy));
                    oy++;
                }
            }

            for (; ox < n; ox++) ops.Add(new Op('-', a[prefix + ox], prefix + ox, prefix + oy));
            for (; oy < m; oy++) ops.Add(new Op('+', b[prefix + oy], prefix + ox, prefix + oy));
        }

        for (var k = 0; k < suffix; k++)
            ops.Add(new Op(' ', a[a.Length - suffix + k], a.Length - suffix + k, b.Length - suffix + k));

        return ops;
    }
}
