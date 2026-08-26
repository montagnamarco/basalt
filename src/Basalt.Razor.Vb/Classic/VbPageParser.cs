using System.Text;

namespace Basalt.Razor.Vb.Classic;

/// <summary>
/// Reads a <c>.vbpage</c> page: HTML with Visual Basic between <c>&lt;% %&gt;</c>.
/// </summary>
/// <remarks>
/// The shape of Classic ASP and of PHP, which is a different idea from Razor
/// rather than a worse one. A Razor view is a class with a model, compiled
/// into an application that must already exist. A page here is a file: drop it
/// in a folder, it answers on its own path, and it can be read top to bottom.
///
/// That is what made ASP and PHP easy to start with, and it is the thing
/// ASP.NET Core has no answer for. What it should not carry forward is the
/// rest: no Option Strict Off by default, no output that skips encoding, and
/// no globals — the modern part of "Classic ASP, modernised".
/// </remarks>
public static class VbPageParser
{
    /// <summary>What a page is made of, in the order it was written.</summary>
    public sealed record Page(IReadOnlyList<PagePart> Parts)
    {
        /// <summary>Problems found while reading it.</summary>
        public IReadOnlyList<VbHtmlDiagnostic> Diagnostics { get; init; } = [];

        /// <summary>Declarations from a <c>&lt;%! %&gt;</c> block.</summary>
        public IReadOnlyList<string> Members { get; init; } = [];

        /// <summary>Namespaces imported with <c>&lt;%@ Import %&gt;</c>.</summary>
        public IReadOnlyList<string> Imports { get; init; } = [];
    }

    /// <summary>
    /// One run of the page.
    /// </summary>
    /// <param name="Line">The line it starts on, counted from one.</param>
    /// <param name="Position">
    /// Where it starts in the source. Needed by anything that has to point at
    /// the text itself rather than describe it — colouring, above all.
    /// </param>
    public abstract record PagePart(int Line, int Position = 0);

    /// <summary>Markup, written out as it stands.</summary>
    public sealed record Markup(string Text, int Line, int Position = 0)
        : PagePart(Line, Position);

    /// <summary>Statements, run for their effect.</summary>
    public sealed record Code(string Text, int Line, int Position = 0)
        : PagePart(Line, Position);

    /// <summary>An expression whose value is written, HTML-encoded.</summary>
    public sealed record Expression(string Text, int Line, bool Raw = false, int Position = 0)
        : PagePart(Line, Position);

    /// <summary>Reads a page.</summary>
    public static Page Parse(string text)
    {
        var parts = new List<PagePart>();
        var diagnostics = new List<VbHtmlDiagnostic>();
        var members = new List<string>();
        var imports = new List<string>();

        var literal = new StringBuilder();
        var index = 0;
        var line = 1;
        var literalLine = 1;
        var literalStart = 0;

        void FlushMarkup()
        {
            if (literal.Length == 0) return;

            parts.Add(new Markup(literal.ToString(), literalLine, literalStart));
            literal.Clear();
        }

        while (index < text.Length)
        {
            if (text[index] != '<' || !Opens(text, index))
            {
                if (text[index] == '\n') line++;

                literal.Append(text[index]);
                index++;
                continue;
            }

            FlushMarkup();

            var openedAt = line;
            var kind = KindAt(text, index);
            var bodyStart = index + OpenLength(kind);
            var close = text.IndexOf("%>", bodyStart, StringComparison.Ordinal);

            if (close < 0)
            {
                diagnostics.Add(new VbHtmlDiagnostic(
                    "VBP001", "A <% block is not closed by %>.", openedAt, 1));

                // Everything after an unclosed block is code, and reading it
                // as markup would put the source on the page.
                break;
            }

            var raw = text.Substring(bodyStart, close - bodyStart);
            var body = raw.Trim();

            // Where the trimmed text really begins, so a colour lands on the
            // code and not on the spaces the delimiter left in front of it.
            var bodyAt = bodyStart + (raw.Length - raw.TrimStart().Length);

            switch (kind)
            {
                case BlockKind.Expression:
                    parts.Add(new Expression(body, openedAt, Raw: false, Position: bodyAt));
                    break;

                case BlockKind.RawExpression:
                    parts.Add(new Expression(body, openedAt, Raw: true, Position: bodyAt));
                    break;

                case BlockKind.Members:
                    members.Add(body);
                    break;

                case BlockKind.Directive:
                    ReadDirective(body, imports, diagnostics, openedAt);
                    break;

                default:
                    parts.Add(new Code(body, openedAt, bodyAt));
                    break;
            }

            line += CountLines(text, index, close + 2);
            index = close + 2;
            literalLine = line;
            literalStart = index;
        }

        FlushMarkup();

        return new Page(parts)
        {
            Diagnostics = diagnostics,
            Members = members,
            Imports = imports,
        };
    }

    private enum BlockKind
    {
        /// <summary><c>&lt;% %&gt;</c>: statements.</summary>
        Code,

        /// <summary><c>&lt;%= %&gt;</c>: a value, HTML-encoded.</summary>
        Expression,

        /// <summary><c>&lt;%== %&gt;</c>: a value written as it stands.</summary>
        RawExpression,

        /// <summary><c>&lt;%! %&gt;</c>: methods and fields of the page.</summary>
        Members,

        /// <summary><c>&lt;%@ %&gt;</c>: a directive.</summary>
        Directive,
    }

    private static bool Opens(string text, int at) =>
        at + 1 < text.Length && text[at + 1] == '%';

    private static BlockKind KindAt(string text, int at)
    {
        // Longest first: "<%==" also starts with "<%=".
        if (Follows(text, at, "<%==")) return BlockKind.RawExpression;
        if (Follows(text, at, "<%=")) return BlockKind.Expression;
        if (Follows(text, at, "<%!")) return BlockKind.Members;
        if (Follows(text, at, "<%@")) return BlockKind.Directive;

        return BlockKind.Code;
    }

    private static int OpenLength(BlockKind kind) => kind switch
    {
        BlockKind.RawExpression => 4,
        BlockKind.Expression or BlockKind.Members or BlockKind.Directive => 3,
        _ => 2,
    };

    private static bool Follows(string text, int at, string opening) =>
        at + opening.Length <= text.Length &&
        string.CompareOrdinal(text, at, opening, 0, opening.Length) == 0;

    /// <summary>
    /// Reads a directive: <c>&lt;%@ Import Namespace="System.Linq" %&gt;</c>.
    /// </summary>
    private static void ReadDirective(
        string body, List<string> imports, List<VbHtmlDiagnostic> diagnostics, int line)
    {
        var space = body.IndexOf(' ');
        var name = space < 0 ? body : body.Substring(0, space);

        if (!name.Equals("Import", StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(new VbHtmlDiagnostic(
                "VBP002", $"<%@ {name} %> is not a directive this page understands.", line, 1));

            return;
        }

        var value = space < 0 ? "" : body.Substring(space + 1).Trim();

        // Both spellings: Classic ASP wrote the bare namespace, and ASP.NET
        // wrote Namespace="…". Someone porting a page should not have to
        // rewrite the line to find out which one is wanted.
        const string prefix = "Namespace";

        if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            value = value.Substring(prefix.Length).TrimStart().TrimStart('=').Trim();

        value = value.Trim('"');

        if (value.Length == 0)
        {
            diagnostics.Add(new VbHtmlDiagnostic(
                "VBP003", "<%@ Import %> needs a namespace.", line, 1));

            return;
        }

        imports.Add(value);
    }

    private static int CountLines(string text, int from, int to)
    {
        var lines = 0;

        for (var i = from; i < to && i < text.Length; i++)
            if (text[i] == '\n') lines++;

        return lines;
    }
}
