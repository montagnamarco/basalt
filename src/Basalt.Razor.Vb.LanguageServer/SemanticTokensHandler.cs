using Basalt.Razor.Vb;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Colouring for the parts of a view that are not markup.
///
/// A TextMate grammar can colour the HTML and the "@" transitions, but only
/// the parser knows which text is a directive, which is an expression and
/// which is Visual Basic. Those are what this reports; the grammar keeps the
/// rest.
/// </summary>
public sealed class VbHtmlSemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlSemanticTokensHandler(DocumentStore documents) => _documents = documents;

    protected override Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
        CancellationToken ct)
    {
        var document = _documents.Get(identifier.TextDocument.Uri.ToString());

        if (document is null) return Task.CompletedTask;

        // A range request asks about the lines on screen. Colouring a large
        // file is otherwise paid for in full on every edit, when the editor
        // only ever shows a screenful.
        var range = (identifier as SemanticTokensRangeParams)?.Range;

        foreach (var (line, character, length, type) in Tokens(document))
        {
            ct.ThrowIfCancellationRequested();

            if (range is not null && !Within(range, line)) continue;

            // No modifiers: nothing here is static, readonly or deprecated.
            builder.Push(line, character, length, type, Array.Empty<SemanticTokenModifier>());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether a line falls in the range asked about.
    ///
    /// By line rather than by character: a token is pushed whole, and one
    /// that starts before the range but reaches into it still has to be
    /// coloured or the first line on screen loses its highlighting.
    /// </summary>
    internal static bool Within(
        OmniSharp.Extensions.LanguageServer.Protocol.Models.Range range, int line) =>
        line >= range.Start.Line && line <= range.End.Line;

    /// <summary>
    /// How far a construct actually runs in the source.
    ///
    /// A node records the text it holds, which is not always what was
    /// written: parentheses and prefixes are stripped while parsing. The
    /// colouring has to follow the source, or it stops short of what the
    /// reader sees.
    /// </summary>
    private static int SourceLength(string text, int from, int atLeast)
    {
        var at = Math.Min(from + 1, text.Length);

        // An explicit expression runs to its closing bracket.
        if (at < text.Length && text[at] == '(')
        {
            var depth = 0;

            for (var i = at; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) return i - from + 1;
            }
        }

        // Otherwise the run of member-chain characters after the at sign,
        // which covers "Html.Raw(x)" as well as a plain name.
        var end = at;

        while (end < text.Length
               && (char.IsLetterOrDigit(text[end]) || text[end] is '.' or '_'))
            end++;

        if (end < text.Length && text[end] == '(')
        {
            var depth = 0;

            for (var i = end; i < text.Length; i++)
            {
                if (text[i] == '(') depth++;
                else if (text[i] == ')' && --depth == 0) { end = i + 1; break; }
            }
        }

        return Math.Max(atLeast, end - from);
    }

    /// <summary>How far a block runs, from its keyword to the end of its body.</summary>
    private static int BlockLength(string text, int from, string body)
    {
        var at = text.IndexOf(body, Math.Min(from, text.Length), StringComparison.Ordinal);

        return at < 0 ? body.Length : at - from + body.Length;
    }

    /// <summary>
    /// The tokens of a view, as line, column, length and kind.
    ///
    /// Separated from the protocol so it can be checked without a server.
    /// </summary>
    /// <summary>
    /// The part of a block's opening that follows its keyword.
    /// </summary>
    /// <remarks>
    /// "@If ok Then" gives back " ok Then": the condition and the trailing
    /// keyword, which are coloured like any other Visual Basic rather than
    /// swept into the opening keyword.
    /// </remarks>
    private static string ClauseAfterKeyword(string text, BlockNode block)
    {
        var start = block.Position + KeywordLength(text, block.Position);
        var length = block.Opening.Length + 1 - KeywordLength(text, block.Position);

        if (length <= 0 || start >= text.Length) return "";

        return text.Substring(start, Math.Min(length, text.Length - start));
    }

    private static bool IsWordStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsWordCharacter(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>How long the keyword introducing a block is.</summary>
    private static int KeywordLength(string text, int position)
    {
        var at = position;

        // Past the at sign that introduced it.
        if (at < text.Length && text[at] == '@') at++;

        var start = at;

        while (at < text.Length && IsWordCharacter(text[at])) at++;

        return at - start + (start - position);
    }

    /// <summary>
    /// The Visual Basic keywords worth colouring inside a view.
    /// </summary>
    /// <remarks>
    /// Not the whole language: a view holds control flow, declarations and
    /// the odd operator, and listing every keyword Visual Basic has would
    /// colour words that never appear here while adding nothing a reader
    /// uses.
    /// </remarks>
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dim", "As", "New", "Nothing", "True", "False", "If", "Then", "Else",
        "ElseIf", "End", "For", "Each", "In", "To", "Step", "Next", "While",
        "Do", "Loop", "Until", "Select", "Case", "Try", "Catch", "Finally",
        "Using", "With", "Function", "Sub", "Return", "And", "Or", "Not",
        "AndAlso", "OrElse", "Is", "IsNot", "Await", "Async", "Public",
        "Private", "Protected", "Friend", "Shared", "ReadOnly", "Const",
        "Property", "Get", "Set", "Imports", "Me", "MyBase", "String",
        "Integer", "Boolean", "Double", "Decimal", "Date", "Object", "Long",
    };

    /// <summary>Where the last thing inside a block sits in the source.</summary>
    private static int LastPositionIn(BlockNode block)
    {
        var last = block.Position + block.Opening.Length;

        void Scan(IEnumerable<VbHtmlNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Position > last) last = node.Position;

                if (node is BlockNode inner)
                {
                    Scan(inner.Body);

                    foreach (var clause in inner.Clauses) Scan(clause.Body);
                }
            }
        }

        Scan(block.Body);

        foreach (var clause in block.Clauses) Scan(clause.Body);

        return last;
    }

    internal static IReadOnlyList<(int Line, int Character, int Length, SemanticTokenType Type)>
        Tokens(OpenDocument document)
    {
        var tokens = new List<(int, int, int, SemanticTokenType)>();

        void Walk(IEnumerable<VbHtmlNode> nodes)
        {
            foreach (var node in nodes)
            {
                switch (node)
                {
                    case DirectiveNode directive:
                        Add(directive.Position, directive.Name.Length + 1, SemanticTokenType.Macro);
                        break;

                    case ExpressionNode expression:
                        // Measured against the source, not the node's text:
                        // "@(a * b)" is stored paren-stripped, so adding the
                        // text length coloured two characters short. Same for
                        // "@Html.Raw(x)", where the prefix is stripped too.
                        Add(expression.Position,
                            SourceLength(document.Text, expression.Position,
                                expression.Expression.Length + 1),
                            SemanticTokenType.Variable);
                        break;

                    case StatementNode statement:
                        // The keyword, then the code inside it token by
                        // token: painting the whole block one colour made a
                        // Code block a solid stripe, where a string, a
                        // number and a comment all read the same.
                        if (!statement.IsContinuation)
                        {
                            Add(statement.Position, KeywordLength(document.Text, statement.Position),
                                SemanticTokenType.Keyword);
                        }

                        AddVisualBasic(statement.BodyPosition, statement.Code);
                        break;

                    case BlockNode block:
                        // The opening clause the same way: "@If ok Then" is a
                        // keyword, a name and another keyword, not one long
                        // keyword.
                        Add(block.Position, KeywordLength(document.Text, block.Position),
                            SemanticTokenType.Keyword);

                        AddVisualBasic(
                            block.Position + KeywordLength(document.Text, block.Position),
                            ClauseAfterKeyword(document.Text, block));
                        Walk(block.Body);

                        foreach (var clause in block.Clauses) Walk(clause.Body);

                        // The closing keyword as well: "@For Each" came out
                        // blue while its "Next" stayed black, which reads as
                        // the editor not understanding the block at all.
                        AddClosing(block);
                        break;


                }
            }
        }

        /// <summary>Colours a block's closing keyword, wherever it was written.</summary>
        /// <remarks>
        /// Found by searching rather than recorded by the parser: the node
        /// carries the keyword but not where it sits, and the search is over
        /// the block's own text — the first "Next" after the body is the one
        /// that closes it.
        /// </remarks>
        void AddClosing(BlockNode block)
        {
            if (block.Closing.Length == 0) return;

            var after = LastPositionIn(block);

            var at = document.Text.IndexOf(
                block.Closing, Math.Min(after, document.Text.Length),
                StringComparison.OrdinalIgnoreCase);

            if (at < 0) return;

            Add(at, block.Closing.Length, SemanticTokenType.Keyword);
        }

        /// <summary>
        /// Colours a run of Visual Basic, token by token.
        /// </summary>
        /// <remarks>
        /// A scan rather than a parse: the Visual Basic inside a view is
        /// handed to Roslyn for meaning, and asking it to classify every
        /// keystroke as well would put a compilation between typing and
        /// seeing a colour. Strings, comments, numbers and keywords are what
        /// a reader picks out, and those a scan can find exactly.
        /// </remarks>
        void AddVisualBasic(int origin, string code)
        {
            var i = 0;

            while (i < code.Length)
            {
                var c = code[i];

                // A comment runs to the end of its line, and nothing inside
                // it is anything else.
                if (c == '\'')
                {
                    var end = code.IndexOf('\n', i);
                    if (end < 0) end = code.Length;

                    Add(origin + i, end - i, SemanticTokenType.Comment);
                    i = end;
                    continue;
                }

                if (c == '"')
                {
                    var end = i + 1;

                    // "" inside a string is an escaped quote, not the end.
                    while (end < code.Length)
                    {
                        if (code[end] != '"') { end++; continue; }
                        if (end + 1 < code.Length && code[end + 1] == '"') { end += 2; continue; }
                        break;
                    }

                    end = Math.Min(end + 1, code.Length);

                    Add(origin + i, end - i, SemanticTokenType.String);
                    i = end;
                    continue;
                }

                if (char.IsDigit(c) && (i == 0 || !IsWordCharacter(code[i - 1])))
                {
                    var end = i;

                    while (end < code.Length && (char.IsDigit(code[end]) || code[end] == '.')) end++;

                    Add(origin + i, end - i, SemanticTokenType.Number);
                    i = end;
                    continue;
                }

                if (IsWordStart(c))
                {
                    var end = i;

                    while (end < code.Length && IsWordCharacter(code[end])) end++;

                    var word = code.Substring(i, end - i);

                    if (Keywords.Contains(word))
                        Add(origin + i, end - i, SemanticTokenType.Keyword);

                    // A name after a dot is a member of whatever precedes it.
                    else if (i > 0 && code[i - 1] == '.')
                        Add(origin + i, end - i, SemanticTokenType.Property);

                    i = end;
                    continue;
                }

                i++;
            }
        }

        void Add(int position, int length, SemanticTokenType type)
        {
            if (length <= 0) return;

            var (line, character) = LineAndCharacterOf(document.Text, position);

            // Clipped to the line it starts on. A semantic token cannot span
            // lines — the protocol has no way to say so — and a length that
            // runs past the end is read by the editor as reaching into the
            // next one, which is the colour bleeding over the text after it.
            //
            // "@Code … End Code" is the case that does it: measured from the
            // keyword to the end of the body, which is several lines away.
            var endOfLine = document.Text.IndexOf('\n', position);
            if (endOfLine < 0) endOfLine = document.Text.Length;

            // A trailing carriage return is not part of the text either.
            if (endOfLine > position && document.Text[endOfLine - 1] == '\r') endOfLine--;

            var clipped = Math.Min(length, endOfLine - position);

            if (clipped <= 0) return;

            tokens.Add((line, character, clipped, type));
        }

        Walk(document.Parsed.Nodes);

        // The protocol wants them in order; the tree is walked in source order
        // already, but a nested block can put a child before a later sibling.
        return [.. tokens.OrderBy(t => t.Item1).ThenBy(t => t.Item2)];
    }

    /// <summary>Turns an offset into a zero-based line and character.</summary>
    internal static (int Line, int Character) LineAndCharacterOf(string text, int position)
    {
        var offset = Math.Clamp(position, 0, text.Length);

        var line = 0;
        var lineStart = 0;

        for (var i = 0; i < offset; i++)
        {
            if (text[i] != '\n') continue;

            line++;
            lineStart = i + 1;
        }

        return (line, offset - lineStart);
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(
        ITextDocumentIdentifierParams identifier, CancellationToken ct) =>
        // Through our own legend rather than RegistrationOptions.Legend: the
        // library fills RegistrationOptions in from the capability exchange,
        // and a client that asks for semantic tokens without having declared
        // support for them leaves it null. Dereferencing it answered every
        // colouring request with an Internal Error, and an editor that gets an
        // error for its tokens simply shows none — which reads as highlighting
        // being broken, with the reason buried in a JSON-RPC reply.
        Task.FromResult(new SemanticTokensDocument(Legend));

    /// <summary>
    /// The token types this server emits, in protocol order.
    /// </summary>
    /// <remarks>
    /// The index is the wire format: an editor receives a number, not a name,
    /// so reordering this list recolours every token in every open view.
    /// </remarks>
    private static readonly SemanticTokensLegend Legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(
            SemanticTokenType.Macro,
            SemanticTokenType.Variable,
            SemanticTokenType.Keyword,
            SemanticTokenType.String,
            SemanticTokenType.Number,
            SemanticTokenType.Comment,
            SemanticTokenType.Property,
            SemanticTokenType.Operator),
        TokenModifiers = new Container<SemanticTokenModifier>()
    };

    /// <summary>
    /// The document a colouring request is answered into, without a client.
    /// </summary>
    /// <remarks>
    /// A seam for the tests: the failure this guards against happens before
    /// any capability exchange, which a test cannot otherwise reach.
    /// </remarks>
    internal Task<SemanticTokensDocument> GetTokensDocumentForTestAsync() =>
        GetSemanticTokensDocument(null!, CancellationToken.None);

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = Selector.ForVbHtml,
            Legend = Legend,
            Full = true,
            Range = true
        };
}
