using System.Text;
using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Classic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.VisualBasic;

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
        Task.FromResult(FormatLine(document.Text, caret));

    public bool TriggersFormatting(char character) => false;

    /// <summary>
    /// The line that closes the block opened on this one, if it opens one.
    /// </summary>
    /// <remarks>
    /// Through the same Roslyn-backed completer the IDE uses for a .vb file,
    /// asked about the code alone: pressing Enter after "If x Then" puts
    /// "End If" below, the way Visual Basic has always done it. A template
    /// used to answer null here, so blocks inside @Code had to be closed by
    /// hand while the very same block in a .vb file closed itself.
    ///
    /// The whole template cannot be handed to the completer, because the
    /// markup around the code is not Visual Basic and stops it parsing. What
    /// is passed is the code of the block the line sits in, with the line
    /// index rebased onto it.
    /// </remarks>
    public async Task<string?> GetBlockClosingAsync(
        LanguageDocument document, int lineIndex, CancellationToken ct = default)
    {
        var text = document.Text;
        var lineStart = StartOfLine(text, lineIndex);

        if (lineStart < 0) return null;

        var block = CodeAround(text, lineStart);

        if (block is null) return null;

        var (code, from) = block.Value;

        // Which line of the block the caret is on. Anchored to the start of
        // the line the code begins on, not to the code itself: the parser
        // hands over the block trimmed of its leading whitespace, so on an
        // indented line the two sit on the same line but at different offsets
        // — and counting from the code put the caret one line out, which
        // silently answered null for every indented block.
        var firstLine = LineOf(text, from);
        var codeStartsAt = StartOfLine(text, firstLine);

        if (codeStartsAt >= 0 && codeStartsAt < from)
        {
            // The block's own first line keeps whatever indentation it had, so
            // the line numbers line up with the template's.
            code = text[codeStartsAt..(from + code.Length)];
        }

        // Wrapped in a module and a method, because Roslyn will not read a
        // bare "If x Then" as a block at all: on its own it parses as an
        // IfStatement with no MultiLineIfBlock around it, and the completer
        // looks for the block. Measured — without the wrapper every template
        // answered null, whatever the line said.
        const string opening = "Module __M\nSub __S()\n";

        var closing = await VisualBasicBlockCompleter
            .GetClosingFor(opening + code, lineIndex - firstLine + 2, ct)
            .ConfigureAwait(false);

        // End Sub and End Module are the wrapper's, not the author's.
        return closing is "End Sub" or "End Module" ? null : closing;
    }

    /// <summary>
    /// How far a new line at this position should be indented.
    /// </summary>
    /// <remarks>
    /// The depth of the enclosing blocks, counted the same way the document
    /// formatter counts it, so a line typed and a line laid out afterwards
    /// agree. Answering zero — which the default did — dropped every new line
    /// inside a block back to the left margin.
    /// </remarks>
    public Task<int> GetIndentationAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var text = document.Text;
        var block = CodeAround(text, Math.Clamp(position, 0, text.Length));

        if (block is null) return Task.FromResult(0);

        var (code, from) = block.Value;
        var upTo = Math.Clamp(position - from, 0, code.Length);

        var depth = 1;

        foreach (var raw in code[..upTo].Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();

            if (line.Length == 0) continue;
            if (Closes(line)) depth = Math.Max(1, depth - 1);
            if (Opens(line)) depth++;
        }

        return Task.FromResult(depth * 4);
    }

    /// <summary>
    /// The code block this position sits in, and where it starts.
    /// </summary>
    private static (string Code, int From)? CodeAround(string text, int position)
    {
        // A .vb file is Visual Basic from the first character: there is no
        // markup to stay out of and no delimiters to find. Looking for them
        // meant a plain file got nothing — no block closed itself and no line
        // was tidied, which is most of what Rider does not do for VB.NET.
        if (IsPlainVisualBasic(text)) return (text, 0);

        if (LooksLikeAPage(text))
        {
            var open = text.LastIndexOf("<%", position, StringComparison.Ordinal);

            if (open < 0) return null;

            var close = text.IndexOf("%>", open, StringComparison.Ordinal);

            if (close < 0 || close < position) return null;

            return (text[(open + 2)..close], open + 2);
        }

        var parsed = VbHtmlParser.Parse(text);

        foreach (var statement in parsed.Nodes.OfType<StatementNode>())
        {
            var at = text.IndexOf(statement.Code, statement.Position, StringComparison.Ordinal);

            if (at < 0) continue;

            // Back over the indentation in front of the first line: the parser
            // hands the block over trimmed, so a caret at the start of an
            // indented line sits before the code and the block was not found
            // at all — every indented block silently closed nothing.
            var start = at;

            while (start > 0 && (text[start - 1] == ' ' || text[start - 1] == '\t')) start--;

            if (position < start || position > at + statement.Code.Length) continue;

            return (text[start..(at + statement.Code.Length)], start);
        }

        return null;
    }

    /// <summary>The offset a line begins at, or -1 when there is no such line.</summary>
    private static int StartOfLine(string text, int lineIndex)
    {
        if (lineIndex < 0) return -1;

        var at = 0;

        for (var line = 0; line < lineIndex; line++)
        {
            var next = text.IndexOf('\n', at);

            if (next < 0) return -1;

            at = next + 1;
        }

        return at;
    }

    /// <summary>Which line an offset falls on.</summary>
    private static int LineOf(string text, int offset)
    {
        var line = 0;

        for (var i = 0; i < offset && i < text.Length; i++)
            if (text[i] == '\n') line++;

        return line;
    }

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
    /// <summary>
    /// Tidies the one line the caret just left.
    /// </summary>
    /// <remarks>
    /// This is what the editor asks for on Enter, and it used to answer
    /// "unchanged" every time: the method was a stub, so a view was laid out
    /// only when someone ran Reformat Code by hand. Typing behaved like a
    /// plain text editor, which is the opposite of what made Visual Basic
    /// Visual Basic.
    ///
    /// One line rather than the document: reformatting everything on every
    /// Enter moves text far from the caret, and an author who indented
    /// something deliberately watches it get undone as they type.
    /// </remarks>
    internal static FormattingResult FormatLine(string text, int caret)
    {
        var at = Math.Clamp(caret, 0, text.Length);

        // The line the caret is on. The editor reports the position where
        // Enter was pressed — the end of the line just finished — rather than
        // the start of the new one, so stepping back a line as well corrected
        // the line above and left the one just typed alone.
        var lineStart = text.LastIndexOf('\n', Math.Max(0, at - 1)) + 1;
        var lineEnd = text.IndexOf('\n', lineStart);

        if (lineEnd < 0) lineEnd = text.Length;

        var line = text[lineStart..lineEnd];

        if (line.Trim().Length == 0) return FormattingResult.Unchanged(text, caret);

        // Only inside code. A line of markup is the author's to lay out, and
        // rewriting HTML as it is typed is how a formatter gets switched off.
        if (!IsCode(text, lineStart, lineEnd)) return FormattingResult.Unchanged(text, caret);

        // A page writes its code between delimiters on the same line, so what
        // is canonicalised is what sits inside them rather than the whole
        // line — otherwise the <% and %> are handed to the parser as if they
        // were Visual Basic.
        var inner = Delimited(line);
        var canonical = Canonicalise(inner.Trim());

        if (canonical.Trim().Length == 0) return FormattingResult.Unchanged(text, caret);

        // The indentation the line already carries is kept: working out the
        // right depth needs the whole block, which is what Format does when
        // the document is laid out as a whole.
        var indent = line[..(line.Length - line.TrimStart().Length)];

        // The block's own padding is kept: replacing the trimmed code would
        // close the delimiters up against it, turning "<% If x = 1 Then %>"
        // into "<%If x = 1 Then%>" the first time a line was typed.
        var replacement = ReferenceEquals(inner, line)
            ? indent + canonical.Trim()
            : line.Replace(inner, Padded(inner, canonical.Trim()));

        if (string.Equals(replacement, line, StringComparison.Ordinal))
            return FormattingResult.Unchanged(text, caret);

        var updated = text[..lineStart] + replacement + text[lineEnd..];

        // The caret moves by however much the line grew or shrank before it.
        return new FormattingResult(updated, caret + (replacement.Length - line.Length), Changed: true);
    }

    /// <summary>
    /// Whether this position sits in Visual Basic rather than in markup.
    /// </summary>
    private static bool IsCode(string text, int lineStart, int lineEnd)
    {
        if (IsPlainVisualBasic(text)) return true;

        // A page's blocks are delimited and usually sit within one line, so
        // the line itself answers the question. Looking only at what precedes
        // the line said no for a block that opens on it, which left every
        // single-line <% %> unformatted.
        if (LooksLikeAPage(text))
        {
            var line = text[lineStart..lineEnd];

            if (line.Contains("<%", StringComparison.Ordinal)) return true;

            var open = text.LastIndexOf("<%", lineStart, StringComparison.Ordinal);
            var close = text.LastIndexOf("%>", lineStart, StringComparison.Ordinal);

            return open > close;
        }

        var parsed = VbHtmlParser.Parse(text);

        return parsed.Nodes
            .OfType<StatementNode>()
            .Any(block => lineStart >= block.Position
                       && lineStart <= block.Position + block.Code.Length);
    }

    /// <summary>
    /// Puts the replacement back with the spaces the original carried.
    /// </summary>
    private static string Padded(string original, string replacement)
    {
        var before = original.StartsWith(' ') || original.StartsWith('\t') ? " " : "";
        var after = original.EndsWith(' ') || original.EndsWith('\t') ? " " : "";

        return before + replacement + after;
    }

    /// <summary>
    /// The Visual Basic inside a page's delimiters, or the line itself.
    /// </summary>
    private static string Delimited(string line)
    {
        var open = line.IndexOf("<%", StringComparison.Ordinal);

        if (open < 0) return line;

        // Past the marker that follows <% on a directive or an expression:
        // those are not statements and are left alone.
        var from = open + 2;

        if (from < line.Length && (line[from] == '@' || line[from] == '=' || line[from] == '!'))
            return line;

        var close = line.IndexOf("%>", from, StringComparison.Ordinal);

        return close < 0 ? line : line[from..close];
    }

    internal static FormattingResult Format(string text, int caret)
    {
        // A .vbpage page is written with <% %> rather than @, so the Razor parser
        // sees one long run of markup and finds nothing to lay out. The pages
        // were left exactly as typed while views were being tidied, which is
        // the sort of gap nobody reports as a bug — it just quietly feels
        // unfinished.
        if (LooksLikeAPage(text)) return FormatPage(text, caret);

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

            // Casing before indentation. Roslyn is asked what each keyword is
            // properly spelled, which is what makes "end if" become "End If"
            // and is the half of the Visual Basic experience that indentation
            // alone does not give: a view used to be indented tidily and
            // stayed lower case, which reads as a formatter that half works.
            var indented = Indent(Canonicalise(block.Code));

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
    /// <summary>
    /// Spells each Visual Basic keyword the way the language does.
    /// </summary>
    /// <remarks>
    /// Through Roslyn rather than a list of keywords kept here: the list is
    /// long, contextual keywords are not keywords everywhere, and a second
    /// opinion about what a keyword is would disagree with the compiler
    /// sooner or later.
    ///
    /// Spacing comes from Roslyn's own formatter for the same reason: it is
    /// what turns "x=1" into "x = 1", and the rules for which operators take
    /// spaces are the compiler's to state, not ours to copy.
    ///
    /// Synchronously, because the surrounding formatter is: a block is a few
    /// lines of text and parsing it costs less than the machinery to await it
    /// would. Failure is silent by design — a block that does not parse is
    /// left exactly as written rather than half-corrected, since a template
    /// being typed into is unparseable most of the time.
    /// </remarks>
    private static string Canonicalise(string code)
    {
        try
        {
            var cased = VisualBasicCaseCorrector
                .CorrectKeywordsAsync(code, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            return Spaced(cased);
        }
        catch (Exception)
        {
            return code;
        }
    }

    /// <summary>
    /// Puts the spaces where Visual Basic puts them.
    /// </summary>
    /// <remarks>
    /// The block's own lines are formatted inside a scratch method: Roslyn
    /// formats a compilation unit, and a bare sequence of statements is not
    /// one. Wrapping and unwrapping keeps the indentation this class applies
    /// afterwards, which is what lines the code up with the markup around it.
    /// </remarks>
    private static string Spaced(string code)
    {
        // Closed off, so a block that opens a construct and leaves its body
        // in the markup still parses: a page writes "if x=1 then" in one <% %>
        // and "end if" in another, and neither half is a statement on its own.
        // Without this the tree has errors, Spaced gives up, and the page gets
        // its keywords cased but never its spacing.
        var opening = "Module __M\nSub __S()\n";
        var closing = "\n" + ClosingFor(code) + "\nEnd Sub\nEnd Module";

        var wrapped = opening + code.Replace("\r\n", "\n") + closing;
        var tree = VisualBasicSyntaxTree.ParseText(wrapped);

        // A block still being typed does not parse, and formatting a broken
        // tree moves text the author is in the middle of writing.
        if (tree.GetDiagnostics().Any(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)) return code;

        using var workspace = new AdhocWorkspace();
        // Roslyn writes the line breaks it touches as Environment.NewLine, so on
        // Windows the scratch method came back with "\r\n" where it went in with
        // "\n", the markers below were not found and every block was left
        // unspaced. Back to "\n", which is what this class works in throughout.
        var formatted = Formatter.Format(tree.GetRoot(), workspace)
            .ToFullString()
            .Replace("\r\n", "\n");

        var start = formatted.IndexOf(")\n", StringComparison.Ordinal);
        var end = formatted.LastIndexOf("End Sub", StringComparison.Ordinal);

        if (start < 0 || end < 0 || end <= start) return code;

        var inner = formatted[(formatted.IndexOf('\n', start) + 1)..end];

        // The closing keyword added above is ours, not the author's.
        var added = ClosingFor(code);

        if (added.Length > 0)
        {
            var last = inner.LastIndexOf(added, StringComparison.Ordinal);
            if (last >= 0) inner = inner[..last];
        }

        return inner.TrimEnd('\n', ' ');
    }

    /// <summary>
    /// Whether this is a page of &lt;% %&gt; blocks rather than a Razor view.
    /// </summary>
    /// <remarks>
    /// Decided from the text rather than the file name: the formatter is given
    /// a document's contents, and a caller that had to name the dialect first
    /// would have to get that right in every editor separately.
    /// </remarks>
    private static bool LooksLikeAPage(string text) =>
        text.Contains("<%", StringComparison.Ordinal);

    /// <summary>
    /// Whether this is a .vb file rather than a template.
    /// </summary>
    /// <remarks>
    /// Decided from the text, like the page check above: the formatter is
    /// handed a document's contents, and a caller that had to name the dialect
    /// would have to get it right in every editor separately.
    ///
    /// A template always carries one of the two markers somewhere — a Razor
    /// view has an @ directive or expression, a page has its delimiters — so
    /// their absence is what identifies plain Visual Basic. A .vb file that
    /// happens to contain an "@" inside a string is the case this gets wrong,
    /// and it errs towards treating the file as a template, which formats
    /// nothing rather than formatting the wrong thing.
    /// </remarks>
    private static bool IsPlainVisualBasic(string text) =>
        !text.Contains("<%", StringComparison.Ordinal)
        && !text.Contains('@');

    /// <summary>
    /// Lays out the Visual Basic inside a page's blocks.
    /// </summary>
    /// <remarks>
    /// Each block on its own, with the markup between them untouched. A page's
    /// blocks are often a line each — an opening If, some HTML, a closing End
    /// If — so there is no run of statements to indent as a unit the way a
    /// view's @Code block is; what is worth fixing is how each line is spelled.
    /// </remarks>
    private static FormattingResult FormatPage(string text, int caret)
    {
        var page = VbPageParser.Parse(text);

        var blocks = page.Parts
            .OfType<VbPageParser.Code>()
            .Where(block => block.Position > 0 && block.Text.Trim().Length > 0)
            .OrderByDescending(block => block.Position)
            .ToList();

        if (blocks.Count == 0) return FormattingResult.Unchanged(text, caret);

        var written = new StringBuilder(text);
        var changed = false;

        // Later blocks first, so an edit does not shift the ones after it.
        foreach (var block in blocks)
        {
            var at = text.IndexOf(block.Text, block.Position, StringComparison.Ordinal);

            if (at < 0) continue;

            // Trimmed of the wrapper's indentation: Roslyn formats inside a
            // scratch method and hands back lines indented for it, which
            // inside <% %> shows up as a block that drifts right every time
            // the page is formatted.
            //
            // A page's block is also often half a statement — "if x=1 then"
            // with its body in the markup below — so it is canonicalised on
            // its own line and put back on one line.
            var canonical = OneLine(block.Text, Canonicalise(block.Text));

            if (string.Equals(canonical, block.Text, StringComparison.Ordinal)) continue;

            written.Remove(at, block.Text.Length);
            written.Insert(at, canonical);
            changed = true;
        }

        return changed
            ? new FormattingResult(written.ToString(), caret, Changed: true)
            : FormattingResult.Unchanged(text, caret);
    }

    /// <summary>
    /// Puts a formatted block back on the single line it came from.
    /// </summary>
    /// <remarks>
    /// The leading whitespace is the scratch wrapper's, not the author's, and
    /// carrying it into a &lt;% %&gt; block indents the page further on every
    /// pass. Blocks written across several lines keep their line breaks; only
    /// the indentation each line gained is removed.
    /// </remarks>
    /// <summary>
    /// What would close the construct this text opens, if it opens one.
    /// </summary>
    private static string ClosingFor(string code)
    {
        var line = code.Trim();

        if (StartsWithWord(line, "If") && line.EndsWith("Then", StringComparison.OrdinalIgnoreCase))
            return "End If";
        if (StartsWithWord(line, "For")) return "Next";
        if (StartsWithWord(line, "While")) return "End While";
        if (StartsWithWord(line, "Using")) return "End Using";
        if (StartsWithWord(line, "With")) return "End With";
        if (StartsWithWord(line, "Select")) return "End Select";
        if (StartsWithWord(line, "Try")) return "End Try";

        return "";
    }

    private static string OneLine(string original, string formatted)
    {
        // The block's own padding is kept rather than replaced: the parser
        // hands over the text between the delimiters with its spaces included,
        // so adding one of our own on each side widened every block by two
        // characters every time the page was formatted.
        var before = original.StartsWith(' ') || original.StartsWith('\t') ? " " : "";
        var after = original.EndsWith(' ') || original.EndsWith('\t') ? " " : "";

        var lines = formatted.Replace("\r\n", "\n").Split('\n');

        var body = lines.Length == 1
            ? lines[0].Trim()
            : string.Join("\n", lines.Select(line => line.Trim())).Trim();

        return before + body + after;
    }

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
