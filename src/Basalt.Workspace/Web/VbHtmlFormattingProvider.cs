using System.Text;
using Basalt.Extensibility;
using Basalt.Razor.Vb;

namespace Basalt.Workspace.Web;

/// <summary>
/// Tidying a .vbhtml.
///
/// Deliberately narrow: it indents the Visual Basic inside @Code blocks and
/// lines up the block keywords, and it leaves the markup exactly as written.
///
/// Reformatting HTML is a different job with different opinions — where a tag
/// breaks, how attributes wrap — and a formatter that rearranged a template's
/// markup would be one people turn off. What drifts in practice is the code
/// half, because nothing was indenting it at all.
/// </summary>
public sealed class VbHtmlFormattingProvider : IFormattingProvider
{
    public Task<FormattingResult> FormatDocumentAsync(
        LanguageDocument document, CancellationToken ct = default) =>
        Task.FromResult(Format(document.Text, caret: 0));

    public Task<FormattingResult> FormatRangeAsync(
        LanguageDocument document, int start, int length, CancellationToken ct = default) =>
        // The whole document: a code block is the unit that gets indented,
        // and half of one cannot be tidied sensibly.
        Task.FromResult(Format(document.Text, caret: start));

    public Task<FormattingResult> FormatLineAsync(
        LanguageDocument document, int caret, CancellationToken ct = default) =>
        Task.FromResult(FormattingResult.Unchanged(document.Text, caret));

    public bool TriggersFormatting(char character) => false;

    /// <summary>
    /// Puts the @Imports directives in order, alphabetically, with System
    /// first.
    ///
    /// Separate from formatting rather than part of it: reordering someone's
    /// lines is a change they should ask for, not something that happens
    /// because they pressed the format key.
    ///
    /// Only a run of directives at the very top is touched. Imports written
    /// further down, after markup, are left where they are: moving them
    /// would change which lines they apply to in a reader's mind, and the
    /// generator emits them all at the top anyway.
    /// </summary>
    internal static FormattingResult SortImports(string text, int caret)
    {
        var lines = SplitKeepingBreaks(text);

        var first = -1;
        var last = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Text.Trim();

            if (trimmed.Length == 0)
            {
                // A blank line inside the run is fine; one after it ends it.
                if (first >= 0 && last >= 0) continue;

                continue;
            }

            if (IsImport(trimmed))
            {
                if (first < 0) first = i;

                last = i;
                continue;
            }

            // Anything else ends the run at the top.
            break;
        }

        if (first < 0 || last <= first) return FormattingResult.Unchanged(text, caret);

        var run = lines
            .GetRange(first, last - first + 1)
            .Where(l => l.Text.Trim().Length > 0)
            .Select(l => l.Text.Trim())
            .ToList();

        // System first, then the rest alphabetically. Duplicates go: two
        // identical imports are one import written twice.
        var sorted = run
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(line => SystemFirst(line) ? 0 : 1)
            .ThenBy(NamespaceOf, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (sorted.SequenceEqual(run, StringComparer.Ordinal))
            return FormattingResult.Unchanged(text, caret);

        var builder = new StringBuilder();

        for (var i = 0; i < first; i++) builder.Append(lines[i].Text).Append(lines[i].Break);

        for (var i = 0; i < sorted.Count; i++)
        {
            builder.Append(sorted[i]);
            builder.Append(lines[Math.Min(first + i, last)].Break);
        }

        for (var i = last + 1; i < lines.Count; i++)
            builder.Append(lines[i].Text).Append(lines[i].Break);

        var result = builder.ToString();

        return new FormattingResult(result, Math.Min(caret, result.Length), true);
    }

    /// <summary>
    /// The template with one more @Imports, in the right place and in order.
    ///
    /// Placed with the others when there are others, and at the very top when
    /// there are none: an @Imports after markup applies to the whole view
    /// anyway, but reads as though it did not.
    /// </summary>
    internal static string WithImport(string text, string namespaceName)
    {
        var line = $"@Imports {namespaceName}";

        var lines = SplitKeepingBreaks(text);

        var last = -1;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Text.Trim();

            if (trimmed.Length == 0) continue;

            if (IsImport(trimmed)) { last = i; continue; }

            break;
        }

        // The line break the file already uses, so a Windows template stays
        // one. A file with a single line and no break has none to copy.
        var lineBreak = lines
            .Select(l => l.Break)
            .FirstOrDefault(b => !string.IsNullOrEmpty(b))
            ?? Environment.NewLine;

        if (last < 0)
        {
            // Nothing to join: it goes first, with a blank line after it so
            // the markup does not run straight on.
            return line + lineBreak + text;
        }

        var builder = new StringBuilder();

        for (var i = 0; i <= last; i++) builder.Append(lines[i].Text).Append(lines[i].Break);

        builder.Append(line).Append(lineBreak);

        for (var i = last + 1; i < lines.Count; i++)
            builder.Append(lines[i].Text).Append(lines[i].Break);

        // Added at the end of the run, then sorted, so it lands where it
        // belongs rather than merely last. SortImports returns the text it was
        // given when there is nothing to reorder.
        return SortImports(builder.ToString(), caret: 0).Text;
    }

    /// <summary>Whether a line is an @Imports directive.</summary>
    private static bool IsImport(string line) =>
        line.StartsWith("@Imports ", StringComparison.OrdinalIgnoreCase);

    /// <summary>The namespace an @Imports line names.</summary>
    private static string NamespaceOf(string line) =>
        line.Substring("@Imports ".Length).Trim();

    /// <summary>
    /// Whether a namespace sorts into the first group.
    ///
    /// System before everything else, which is the order Visual Studio and
    /// Roslyn's own organiser use.
    /// </summary>
    private static bool SystemFirst(string line)
    {
        var name = NamespaceOf(line);

        return name.Equals("System", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("System.", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The lines of a text, each with the break that followed it.</summary>
    private static List<(string Text, string Break)> SplitKeepingBreaks(string text)
    {
        var lines = new List<(string, string)>();
        var start = 0;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '\n' && text[at] != '\r') continue;

            var end = at;
            var lineBreak = text[at] == '\r' && at + 1 < text.Length && text[at + 1] == '\n'
                ? "\r\n"
                : text[at].ToString();

            lines.Add((text.Substring(start, end - start), lineBreak));

            at += lineBreak.Length - 1;
            start = at + 1;
        }

        if (start <= text.Length - 1 || lines.Count == 0)
            lines.Add((text.Substring(start), ""));

        return lines;
    }

    /// <summary>
    /// Indents the code inside every @Code block.
    ///
    /// The markup is copied through untouched, so the only lines that can
    /// move are the ones between @Code and End Code.
    /// </summary>
    internal static FormattingResult Format(string text, int caret)
    {
        var parsed = VbHtmlParser.Parse(text);

        // A template that does not parse is left alone: reformatting
        // something half-understood is how a formatter eats someone's work.
        if (parsed.Diagnostics.Count > 0) return FormattingResult.Unchanged(text, caret);

        var blocks = parsed.Nodes.OfType<StatementNode>().ToList();

        if (blocks.Count == 0) return FormattingResult.Unchanged(text, caret);

        var written = new StringBuilder(text);
        var moved = 0;

        // Later blocks first, so an edit does not shift the ones after it.
        foreach (var block in blocks.OrderByDescending(b => b.Position))
        {
            var at = text.IndexOf(block.Code, block.Position, StringComparison.Ordinal);

            if (at < 0) continue;

            // Back up over the whitespace already in front of the first line:
            // replacing from the trimmed position would leave that in place
            // and add the new indentation on top of it.
            var from = at;

            while (from > 0 && (text[from - 1] == ' ' || text[from - 1] == '\t')) from--;

            var existing = text.Substring(from, at - from + block.Code.Length);
            var indented = Indent(block.Code);

            if (string.Equals(indented, existing, StringComparison.Ordinal)) continue;

            written.Remove(from, existing.Length);
            written.Insert(from, indented);

            if (from < caret) moved += indented.Length - existing.Length;
        }

        var result = written.ToString();

        return string.Equals(result, text, StringComparison.Ordinal)
            ? FormattingResult.Unchanged(text, caret)
            : new FormattingResult(result, caret + moved, true);
    }

    /// <summary>
    /// Lays out a run of Visual Basic statements.
    ///
    /// One level in after a block opens, one back out before it closes, which
    /// is what makes a nested If readable.
    /// </summary>
    private static string Indent(string code)
    {
        var lines = code.Replace("\r\n", "\n").Split('\n');
        var written = new List<string>();

        var depth = 1;

        foreach (var raw in lines)
        {
            var line = raw.Trim();

            if (line.Length == 0)
            {
                written.Add("");
                continue;
            }

            // A closing keyword lines up with what it closes, so it is
            // dedented before being written rather than after.
            if (Closes(line)) depth = Math.Max(1, depth - 1);

            written.Add(new string(' ', depth * 4) + line);

            if (Opens(line)) depth++;
        }

        return string.Join("\n", written);
    }

    /// <summary>Whether a line opens a block.</summary>
    private static bool Opens(string line) =>
        StartsWithWord(line, "If") && line.EndsWith("Then", StringComparison.OrdinalIgnoreCase)
        || StartsWithWord(line, "For") || StartsWithWord(line, "While")
        || StartsWithWord(line, "Do") || StartsWithWord(line, "Using")
        || StartsWithWord(line, "With") || StartsWithWord(line, "Select")
        || StartsWithWord(line, "Try") || StartsWithWord(line, "Sub")
        || StartsWithWord(line, "Function");

    /// <summary>Whether a line closes one.</summary>
    private static bool Closes(string line) =>
        StartsWithWord(line, "End") || StartsWithWord(line, "Next")
        || StartsWithWord(line, "Loop") || StartsWithWord(line, "Wend")
        || StartsWithWord(line, "Else") || StartsWithWord(line, "ElseIf")
        || StartsWithWord(line, "Catch") || StartsWithWord(line, "Finally")
        || StartsWithWord(line, "Case");

    /// <summary>
    /// Whether a line begins with a whole word.
    ///
    /// A word boundary matters: "Iffy = 1" does not open a block.
    /// </summary>
    private static bool StartsWithWord(string line, string word)
    {
        if (!line.StartsWith(word, StringComparison.OrdinalIgnoreCase)) return false;

        if (line.Length == word.Length) return true;

        var next = line[word.Length];

        return !char.IsLetterOrDigit(next) && next != '_';
    }
}
