namespace Basalt.Workspace.Snippets;

/// <summary>A place in an expanded snippet the user is meant to fill in.</summary>
public readonly record struct SnippetStop(int Start, int Length, string Placeholder);

/// <summary>A snippet after expansion, with where the caret should go.</summary>
public sealed record ExpandedSnippet(string Text, IReadOnlyList<SnippetStop> Stops, int Caret);

/// <summary>
/// A code snippet, written the way Visual Basic users know them.
///
/// The body uses "$name$" for the places to fill in and "$end$" for where the
/// caret finishes, which is the same notation Visual Studio's snippets use.
/// </summary>
public sealed record CodeSnippet(string Shortcut, string Title, string Body)
{
    public string? Description { get; init; }
}

/// <summary>
/// Expands snippets, keeping the indentation of the line they are written on.
///
/// The indentation matters: a snippet expanded at column eight whose later
/// lines start at column zero produces code the user has to reindent by hand,
/// which is most of the work the snippet was meant to save.
/// </summary>
public static class SnippetExpander
{
    /// <summary>
    /// Expands a snippet for insertion at a position.
    ///
    /// <paramref name="indent"/> is prepended to every line but the first,
    /// which is already positioned by the text it is being inserted into.
    /// </summary>
    public static ExpandedSnippet Expand(CodeSnippet snippet, string indent = "")
    {
        var body = snippet.Body.Replace("\r\n", "\n");

        if (indent.Length > 0) body = body.Replace("\n", "\n" + indent);

        var text = new System.Text.StringBuilder(body.Length);
        var stops = new List<SnippetStop>();
        var caret = -1;

        var index = 0;

        while (index < body.Length)
        {
            if (body[index] != '$')
            {
                text.Append(body[index]);
                index++;
                continue;
            }

            var close = body.IndexOf('$', index + 1);

            if (close < 0)
            {
                // An unpaired marker is a literal dollar sign.
                text.Append(body[index]);
                index++;
                continue;
            }

            var name = body[(index + 1)..close];

            if (name == "end")
            {
                caret = text.Length;
                index = close + 1;
                continue;
            }

            if (name.Length == 0)
            {
                // "$$" is an escaped dollar sign.
                text.Append('$');
                index = close + 1;
                continue;
            }

            stops.Add(new SnippetStop(text.Length, name.Length, name));
            text.Append(name);

            index = close + 1;
        }

        // Without an explicit end, the caret goes to the first thing to fill
        // in, or to the end when there is nothing.
        if (caret < 0) caret = stops.Count > 0 ? stops[0].Start : text.Length;

        return new ExpandedSnippet(text.ToString(), stops, caret);
    }

    /// <summary>The whitespace a line begins with.</summary>
    public static string IndentOfLineAt(string text, int position)
    {
        if (text.Length == 0) return "";

        var caret = Math.Clamp(position, 0, text.Length);
        var lineStart = text.LastIndexOf('\n', Math.Max(0, caret - 1)) + 1;

        var end = lineStart;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t')) end++;

        return text[lineStart..end];
    }
}
