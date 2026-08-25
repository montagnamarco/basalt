using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace Basalt.Shell.Controls;

/// <summary>
/// Where the foldable blocks are in a Visual Basic file.
///
/// AvaloniaEdit ships a strategy for XML and nothing else, so this reads the
/// text itself. By line and by keyword rather than through Roslyn: folding
/// has to keep working while the file is half-typed, which is exactly when
/// the parse tree is at its least useful.
/// </summary>
public static class VbFoldingStrategy
{
    /// <summary>
    /// The words that open a block, each with the word that closes it.
    ///
    /// Longest first, so "End Sub" is not read as "End" and "Property" is not
    /// mistaken for the start of a property block when it is a line of one.
    /// </summary>
    private static readonly (string Open, string Close)[] Blocks =
    [
        ("Namespace", "End Namespace"),
        ("Interface", "End Interface"),
        ("Structure", "End Structure"),
        ("Property", "End Property"),
        ("Function", "End Function"),
        ("Operator", "End Operator"),
        ("Module", "End Module"),
        ("Class", "End Class"),
        ("Enum", "End Enum"),
        ("Sub", "End Sub")
    ];

    /// <summary>
    /// The foldings a document holds, in the order the editor wants them:
    /// by where they start.
    /// </summary>
    public static IReadOnlyList<NewFolding> Foldings(TextDocument document)
    {
        var found = new List<NewFolding>();
        var open = new Stack<(string Close, int Offset, string Name)>();

        // A region is a fold of its own, and it can hold blocks.
        var regions = new Stack<(int Offset, string Name)>();

        foreach (var line in document.Lines)
        {
            var text = document.GetText(line.Offset, line.Length);
            var trimmed = text.Trim();

            if (trimmed.Length == 0) continue;

            // A directive, not a comment: "#Region" starts with a hash and
            // the comment check never sees it.
            if (StartsRegion(trimmed))
            {
                regions.Push((line.Offset, RegionName(trimmed)));
                continue;
            }

            if (EndsRegion(trimmed))
            {
                if (regions.Count > 0)
                {
                    var region = regions.Pop();
                    Add(found, region.Offset, line.EndOffset, region.Name);
                }

                continue;
            }

            if (IsComment(trimmed)) continue;

            if (open.Count > 0 && Closes(trimmed, open.Peek().Close))
            {
                var block = open.Pop();
                Add(found, block.Offset, line.EndOffset, block.Name);
                continue;
            }

            if (Opens(trimmed) is { } opened)
                open.Push((opened.Close, line.Offset, opened.Name));
        }

        return [.. found.OrderBy(f => f.StartOffset)];
    }

    /// <summary>Puts the editor's foldings back in step with the text.</summary>
    public static void Update(FoldingManager manager, TextDocument document) =>
        manager.UpdateFoldings(Foldings(document), firstErrorOffset: -1);

    private static void Add(List<NewFolding> found, int start, int end, string name)
    {
        // A block that fits on one line has nothing to hide.
        if (end <= start) return;

        found.Add(new NewFolding(start, end) { Name = name });
    }

    /// <summary>The block a line opens, if it opens one.</summary>
    private static (string Close, string Name)? Opens(string line)
    {
        foreach (var (open, close) in Blocks)
        {
            if (!HasWord(line, open)) continue;

            // "End Sub" holds the word Sub and opens nothing.
            if (line.StartsWith("End ", StringComparison.OrdinalIgnoreCase)) return null;

            // A declaration ending in a line continuation, or one written on
            // a single line, has no body to fold.
            if (IsSingleLine(line, open)) return null;

            return (close, Summarise(line));
        }

        return null;
    }

    private static bool Closes(string line, string close) =>
        line.StartsWith(close, StringComparison.OrdinalIgnoreCase)
        && (line.Length == close.Length || char.IsWhiteSpace(line[close.Length]));

    /// <summary>
    /// Whether a declaration says everything on its own line.
    ///
    /// "Public Property Name As String" is a property with no body; the same
    /// words followed by nothing open a block that runs to End Property.
    /// </summary>
    private static bool IsSingleLine(string line, string keyword)
    {
        if (!keyword.Equals("Property", StringComparison.OrdinalIgnoreCase)) return false;

        // "Public Property Name As String" is an auto-property and has no
        // body. "Public Property Name() As String" declares one, and its Get
        // and Set follow on the lines below.
        var at = line.IndexOf("Property", StringComparison.OrdinalIgnoreCase);

        return !line[(at + "Property".Length)..].Contains('(');
    }

    /// <summary>Whether a word appears on a line as a word of its own.</summary>
    private static bool HasWord(string line, string word)
    {
        var at = line.IndexOf(word, StringComparison.OrdinalIgnoreCase);

        while (at >= 0)
        {
            var before = at == 0 || !IsWordCharacter(line[at - 1]);
            var afterAt = at + word.Length;
            var after = afterAt >= line.Length || !IsWordCharacter(line[afterAt]);

            if (before && after) return true;

            at = line.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool IsComment(string line) =>
        line.StartsWith('\'') || line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase);

    private static bool StartsRegion(string line) =>
        !EndsRegion(line)
        && line.TrimStart('\'').StartsWith("#Region", StringComparison.OrdinalIgnoreCase);

    private static bool EndsRegion(string line) =>
        line.TrimStart('\'').StartsWith("#End Region", StringComparison.OrdinalIgnoreCase);

    /// <summary>What a folded region is labelled with.</summary>
    private static string RegionName(string line)
    {
        var quote = line.IndexOf('"');

        if (quote >= 0)
        {
            var close = line.IndexOf('"', quote + 1);

            if (close > quote) return line.Substring(quote + 1, close - quote - 1);
        }

        return "Region";
    }

    /// <summary>
    /// What a folded block is labelled with: the line itself, shortened.
    ///
    /// The whole line rather than just the name, because the modifiers say
    /// what it is — Private Sub reads differently from Public Overrides Sub.
    /// </summary>
    private static string Summarise(string line)
    {
        var text = line.Length <= 60 ? line : line.Substring(0, 57) + "…";

        return text + " …";
    }
}
