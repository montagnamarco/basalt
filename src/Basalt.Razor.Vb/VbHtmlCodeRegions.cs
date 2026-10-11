namespace Basalt.Razor.Vb;

/// <summary>
/// Which half of a template a position is in.
///
/// Shared rather than duplicated: the IDE and the standalone language server
/// both have to answer this, and two implementations that could disagree
/// would show up as a feature working in one editor and not the other.
/// </summary>
public static class VbHtmlCodeRegions
{
    /// <summary>
    /// Whether a position sits in the Visual Basic half of the template.
    ///
    /// Decided from the parse tree rather than by counting at signs: a nested
    /// block, a string containing an at sign, and a comment all confuse a
    /// scan, and the tree already knows.
    /// </summary>
    public static bool IsInCode(string text, int position)
    {
        var parsed = VbHtmlParser.Parse(text);

        return Covers(parsed.Nodes, position, text);
    }

    /// <summary>
    /// Whether a position sits in the whitespace after a statement block's
    /// body, before its "End Code".
    /// </summary>
    /// <remarks>
    /// The body is kept trimmed, so <see cref="IsInCode"/> ends at its last
    /// character: right for hover and navigation, which have nothing to say
    /// about blank space, but not for completion, where the caret after
    /// "= New " on the block's last line is exactly where a space has just
    /// asked for the types.
    /// </remarks>
    public static bool IsAfterStatementBody(string text, int position)
    {
        var parsed = VbHtmlParser.Parse(text);

        return AfterBody(parsed.Nodes, position, text);
    }

    private static bool AfterBody(IReadOnlyList<VbHtmlNode> nodes, int position, string text)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case StatementNode statement:
                    var bodyAt = text.IndexOf(statement.Code, statement.Position, StringComparison.Ordinal);
                    if (bodyAt < 0) break;

                    if (InWhitespaceAfter(text, bodyAt + statement.Code.Length, position)) return true;
                    break;

                case FunctionsNode functions:
                    if (InWhitespaceAfter(text, functions.BodyPosition + functions.Code.Length, position))
                        return true;
                    break;

                case BlockNode block:
                    if (AfterBody(block.Body, position, text)) return true;

                    foreach (var clause in block.Clauses)
                        if (AfterBody(clause.Body, position, text)) return true;

                    break;

                case SectionNode section:
                    if (AfterBody(section.Body, position, text)) return true;
                    break;
            }
        }

        return false;
    }

    /// <summary>Whether a position is in the whitespace that follows an offset.</summary>
    private static bool InWhitespaceAfter(string text, int bodyEnd, int position)
    {
        var whitespaceEnd = bodyEnd;

        while (whitespaceEnd < text.Length && text[whitespaceEnd] is ' ' or '\t' or '\r' or '\n')
            whitespaceEnd++;

        return position > bodyEnd && position <= whitespaceEnd;
    }

    /// <summary>
    /// How far a member chain runs from an offset.
    ///
    /// "@Model." stops parsing at "Model", but the dot and whatever follows
    /// belong to the same expression as far as the caret is concerned.
    /// </summary>
    private static int EndOfChain(string text, int from)
    {
        var at = from;

        while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] is '.' or '_'))
            at++;

        return at;
    }

    /// <summary>
    /// The extents of the Visual Basic half of a template: expressions, block
    /// clauses, statement bodies and member blocks, as offsets into the text.
    /// </summary>
    /// <remarks>
    /// What a markup check must not read as markup: inside a code block
    /// <c>&lt;Parameter&gt;</c> is an attribute and <c>a &lt; b</c> a comparison,
    /// and scanned as HTML they were reported as tags never closed. Taken from
    /// the same parse tree <see cref="IsInCode"/> walks. The extents differ on purpose: here
    /// an expression runs from "@" to the end of its own text (Html.Raw and Await wrappers
    /// included) and clause lines to their end, which is what a markup scan must skip.
    /// </remarks>
    public static IReadOnlyList<(int Start, int Length)> CodeSpans(string text)
    {
        var spans = new List<(int Start, int Length)>();
        Collect(VbHtmlParser.Parse(text).Nodes, text, spans);
        return spans;
    }

    private static void Collect(
        IReadOnlyList<VbHtmlNode> nodes, string text, List<(int Start, int Length)> spans)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ExpressionNode expression:
                    // From the "@" to the end of the expression's own text: for
                    // @Html.Raw(…) and @Await the text starts after the wrapper,
                    // so ExpressionPosition, not Position, is where it is measured
                    // from; the member chain typed after it counts too.
                    var end = EndOfChain(text, expression.ExpressionPosition + expression.Expression.Length);
                    spans.Add((expression.Position, Math.Max(0, end - expression.Position)));
                    break;

                case StatementNode statement:
                    var bodyAt = text.IndexOf(statement.Code, statement.Position, StringComparison.Ordinal);
                    if (bodyAt >= 0) spans.Add((bodyAt, statement.Code.Length));
                    break;

                case BlockNode block:
                    // The opening clause and every later one (ElseIf, Case Is < 5)
                    // are Visual Basic to the end of their line.
                    spans.Add(ToEndOfLine(text, block.Position));
                    Collect(block.Body, text, spans);
                    foreach (var clause in block.Clauses)
                    {
                        if (clause.Position >= 0) spans.Add(ToEndOfLine(text, clause.Position));
                        Collect(clause.Body, text, spans);
                    }
                    break;

                case DirectiveNode directive:
                    // @Attribute <Authorize>, @Imports, @ModelType: one line each.
                    spans.Add(ToEndOfLine(text, directive.Position));
                    break;

                case SectionNode section:
                    Collect(section.Body, text, spans);
                    break;

                case FunctionsNode functions:
                    spans.Add((functions.BodyPosition, functions.Code.Length));
                    break;
            }
        }
    }

    private static (int Start, int Length) ToEndOfLine(string text, int start)
    {
        var end = text.IndexOf('\n', Math.Max(0, start));
        if (end < 0) end = text.Length;
        return (start, Math.Max(0, end - start));
    }

    private static bool Covers(
        IReadOnlyList<VbHtmlNode> nodes, int position, string text)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case ExpressionNode expression:
                    // A node's text is not its source extent: "@Model." parses
                    // as the expression "Model", so the caret after the dot
                    // sits past the node while still being in code. The run of
                    // member-chain characters after it counts as the same
                    // expression, which is exactly where completion is asked
                    // for.
                    if (position > expression.Position
                        && position <= EndOfChain(
                            text, expression.Position + expression.Expression.Length + 1))
                        return true;
                    break;

                case StatementNode statement:
                    // Position is where "@Code" began; Code is the body
                    // without the keyword, so the end is found by looking for
                    // the body rather than by adding lengths.
                    var bodyAt = text.IndexOf(statement.Code, statement.Position,
                        StringComparison.Ordinal);

                    if (bodyAt >= 0 && position > bodyAt
                        && position <= bodyAt + statement.Code.Length)
                        return true;
                    break;

                case BlockNode block:
                    // The opening clause is code; the body is decided by
                    // whatever is inside it.
                    if (position > block.Position
                        && position <= block.Position + block.Opening.Length + 1)
                        return true;

                    if (Covers(block.Body, position, text)) return true;

                    foreach (var clause in block.Clauses)
                        if (Covers(clause.Body, position, text)) return true;

                    break;

                case SectionNode section:
                    if (Covers(section.Body, position, text)) return true;
                    break;

                case FunctionsNode functions:
                    // Members declared for the page: Visual Basic as much as a
                    // code block's body, and the list asked of HTML there.
                    if (position > functions.BodyPosition
                        && position <= functions.BodyPosition + functions.Code.Length)
                        return true;
                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Where a template caret lands in the generated Visual Basic, accurate
    /// to the character.
    ///
    /// The span arithmetic in <see cref="SourceMap.ToGenerated"/> cannot be:
    /// a mapped region and the code generated from it are different lengths —
    /// <c>Write(p.Greet("hi", 2))</c> against <c>@p.Greet("hi", 2)</c> — so an
    /// offset measured from the region start means a different place at the
    /// other end. Roslyn forgives that for completion, which looks around the
    /// caret; signature help does not, and answered nothing at all.
    ///
    /// Measuring from the start of the line instead works because
    /// <c>#ExternalSource</c> maps lines: the same text sits on both, with a
    /// different prefix in front of it.
    /// </summary>
    public static int? CaretInGenerated(
        string template, string generated, SourceMap map, int position)
    {
        if (position < 0 || position > template.Length) return null;

        var templateLineStart = StartOfLine(template, position);
        var templateLine = CountLines(template, templateLineStart);

        if (map.ToGeneratedLine(templateLine) is not { } generatedLine) return null;

        var generatedLineStart = StartOfLineNumber(generated, generatedLine);

        if (generatedLineStart < 0) return null;

        // What the caret sits after on its own line. An expression's "@" is
        // not carried into the code; a statement's leading whitespace is not
        // either, because the writer indents to its own depth.
        var text = template.Substring(templateLineStart, position - templateLineStart);
        var afterAt = text.LastIndexOf('@');

        var inLine = afterAt >= 0
            ? text.Length - afterAt - 1
            : text.Length - LeadingWhitespace(text);

        // Where the same text begins on the generated line, past whatever
        // prefix the writer put in front of it.
        var generatedLineEnd = EndOfLine(generated, generatedLineStart);
        var body = generated.Substring(
            generatedLineStart, generatedLineEnd - generatedLineStart);

        var start = PrefixLength(body);

        var at = generatedLineStart + start + inLine;

        return at > generatedLineEnd ? generatedLineEnd : at;
    }

    /// <summary>
    /// How much of a generated line comes before the template's own text.
    ///
    /// The writer indents and wraps an expression in Write(...), and neither
    /// belongs to what the author typed.
    /// </summary>
    private static int PrefixLength(string line)
    {
        var at = 0;

        while (at < line.Length && (line[at] == ' ' || line[at] == '\t')) at++;

        // A component writes an expression into a local of its own,
        // "Dim __v3 = ", before handing it to AddContent. Without skipping it
        // a caret after "Model." landed inside "__v3", and completion was
        // asked about the local instead of the model.
        if (string.CompareOrdinal(line, at, ComponentLocal, 0, ComponentLocal.Length) == 0)
        {
            var equals = line.IndexOf(" = ", at, StringComparison.Ordinal);

            if (equals >= 0) return equals + " = ".Length;
        }

        foreach (var call in Wrappers)
        {
            if (at + call.Length <= line.Length &&
                string.CompareOrdinal(line, at, call, 0, call.Length) == 0)
                return at + call.Length;
        }

        return at;
    }

    /// <summary>
    /// What the writer puts in front of an expression. Longest first, so that
    /// "WriteRaw(" is not read as "Write" followed by something else.
    /// </summary>
    private static readonly string[] Wrappers = ["WriteRaw(", "Write("];

    /// <summary>How the component writer's expression locals begin.</summary>
    private const string ComponentLocal = "Dim __v";

    /// <summary>How much whitespace a line starts with.</summary>
    private static int LeadingWhitespace(string text)
    {
        var at = 0;

        while (at < text.Length && (text[at] == ' ' || text[at] == '\t')) at++;

        return at;
    }

    private static int StartOfLine(string text, int position)
    {
        var at = Math.Min(position, text.Length);

        while (at > 0 && text[at - 1] != '\n') at--;

        return at;
    }

    private static int EndOfLine(string text, int from)
    {
        var at = from;

        while (at < text.Length && text[at] != '\r' && text[at] != '\n') at++;

        return at;
    }

    /// <summary>The 1-based line number a position is on.</summary>
    private static int CountLines(string text, int position)
    {
        var line = 1;

        for (var at = 0; at < position && at < text.Length; at++)
            if (text[at] == '\n') line++;

        return line;
    }

    /// <summary>Where a 1-based line begins, or -1 when there is no such line.</summary>
    private static int StartOfLineNumber(string text, int line)
    {
        if (line <= 1) return 0;

        var seen = 1;

        for (var at = 0; at < text.Length; at++)
        {
            if (text[at] != '\n') continue;

            seen++;

            if (seen == line) return at + 1;
        }

        return -1;
    }
}
