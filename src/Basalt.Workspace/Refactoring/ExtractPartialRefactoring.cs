using Basalt.Razor.Vb;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Moving a run of markup into a partial view of its own.
///
/// The repetitive part of a template is usually a block of markup that wants
/// a name. Doing it by hand means creating the file, moving the lines, and
/// remembering the exact call — three steps where the middle one silently
/// leaves the original behind.
/// </summary>
public sealed class ExtractPartialRefactoring
{
    private const string Title = "Extract to Partial View";

    /// <summary>
    /// What extracting the selected markup would change: a new partial, and
    /// the view with a call in place of what moved.
    /// </summary>
    public RefactoringPreview Preview(
        string filePath, string text, int start, int length, string name)
    {
        if (!IsUsableName(name))
            return RefactoringPreview.Refused(Title, $"'{name}' is not a usable view name.");

        if (start < 0 || length <= 0 || start + length > text.Length)
            return RefactoringPreview.Refused(Title, "Nothing is selected.");

        var selected = text.Substring(start, length);

        if (selected.Trim().Length == 0)
            return RefactoringPreview.Refused(Title, "The selection is only whitespace.");

        // Code cannot travel on its own: a partial is a separate view, and a
        // variable declared in the page is not in scope inside it.
        if (MentionsLocalCode(text, start, length))
        {
            return RefactoringPreview.Refused(
                Title,
                "The selection uses code from this view, which a partial cannot see.");
        }

        var partialName = name.StartsWith('_') ? name : "_" + name;

        var folder = Path.GetDirectoryName(filePath) ?? "";
        var partialPath = Path.Combine(folder, partialName + ".vbhtml");

        if (File.Exists(partialPath))
            return RefactoringPreview.Refused(Title, $"'{partialName}.vbhtml' already exists.");

        var call = $"@Html.Partial(\"{partialName}\")";

        var updated = text.Substring(0, start) + call + text.Substring(start + length);

        return new RefactoringPreview($"Extract to '{partialName}.vbhtml'",
        [
            // The new file first: it is what the call will reach for.
            new FileChangePreview(partialPath, "", Trim(selected)),
            new FileChangePreview(filePath, text, updated)
        ]);
    }

    /// <summary>
    /// Whether the selection reads something declared in the page's own code.
    ///
    /// Conservative on purpose: any expression naming something a Code block
    /// above declares stops the extraction, because the partial would not
    /// compile and the author would find out at build time.
    /// </summary>
    private static bool MentionsLocalCode(string text, int start, int length)
    {
        var document = VbHtmlParser.Parse(text);

        var declared = new List<string>();

        foreach (var node in document.Nodes)
        {
            if (node is not StatementNode statement) continue;

            foreach (var line in statement.Code.Split('\n'))
            {
                var trimmed = line.Trim();

                if (!trimmed.StartsWith("Dim ", StringComparison.OrdinalIgnoreCase)) continue;

                var name = trimmed.Substring(4).Trim();
                var nameEnd = name.IndexOfAny([' ', '\t', '=', '(']);

                if (nameEnd > 0) name = name.Substring(0, nameEnd);

                if (name.Length > 0) declared.Add(name);
            }
        }

        var end = start + length;

        // A block or a statement inside the selection is code moving with it,
        // whether or not it names anything declared above.
        if (document.Nodes.Any(n =>
                n is StatementNode or BlockNode && n.Position >= start && n.Position < end))
            return true;

        if (declared.Count == 0) return false;

        // An expression reading a page variable would not compile in a file
        // that cannot see it.
        return document.Nodes
            .OfType<ExpressionNode>()
            .Where(e => e.Position >= start && e.Position < end)
            .Any(e => declared.Any(name => NamesIt(e.Expression, name)));
    }

    /// <summary>Whether an expression uses a name, as a whole word.</summary>
    private static bool NamesIt(string expression, string name)
    {
        var at = expression.IndexOf(name, StringComparison.OrdinalIgnoreCase);

        while (at >= 0)
        {
            var before = at == 0 || !IsNamePart(expression[at - 1]);
            var afterAt = at + name.Length;
            var after = afterAt >= expression.Length || !IsNamePart(expression[afterAt]);

            if (before && after) return true;

            at = expression.IndexOf(name, at + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsNamePart(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Whether a name can become a view file and a class.</summary>
    private static bool IsUsableName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        var bare = name.TrimStart('_');

        return bare.Length > 0
            && (char.IsLetter(bare[0]) || bare[0] == '_')
            && bare.All(c => char.IsLetterOrDigit(c) || c == '_');
    }

    /// <summary>
    /// The extracted markup, without the indentation it had in the page.
    ///
    /// It is a file of its own now, and keeping the old indentation would
    /// leave every line pushed across for no reason.
    /// </summary>
    private static string Trim(string markup)
    {
        var lines = markup.Replace("\r\n", "\n").Split('\n');

        // Measured from the second line onwards: a selection starts at the
        // first visible character, so the first line has already lost its
        // indentation and would make the common indent zero.
        var indent = lines
            .Skip(1)
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        // The first line keeps what it has: the selection began at its first
        // visible character, so there is no indentation on it to remove and
        // cutting blindly would eat the markup itself.
        var trimmed = lines.Select((line, index) =>
            index == 0 || line.Length < indent
                ? line
                : line.Substring(indent));

        return string.Join(Environment.NewLine, trimmed).Trim()
             + Environment.NewLine;
    }
}
