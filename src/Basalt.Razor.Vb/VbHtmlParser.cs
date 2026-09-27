using System.Text;

namespace Basalt.Razor.Vb;

/// <summary>
/// Parses .vbhtml templates: Razor's markup model with Visual Basic in place of
/// C#.
///
/// Visual Basic delimits blocks with keywords rather than braces, so the parser
/// recognises openings and closings by name instead of by punctuation. That the
/// language is frozen is what makes this practical: the keyword set is fixed
/// and complete, and cannot grow underneath the parser.
///
/// The parser deliberately does not understand Visual Basic expressions. It
/// finds where an expression ends and hands the text to the VB compiler, which
/// is the authority on whether it is valid.
/// </summary>
public sealed class VbHtmlParser
{
    private readonly string _text;
    private int _index;
    private int _line = 1;

    private VbHtmlParser(string text) => _text = text;

    public static VbHtmlDocument Parse(string text)
    {
        var parser = new VbHtmlParser(text);
        return parser.ParseDocument();
    }

    /// <summary>
    /// Block openings, mapped to the keyword that closes them.
    ///
    /// Order matters: longer openings are tested first so that "For Each" is
    /// not mistaken for "For".
    /// </summary>
    private static readonly (string Opening, string Closing)[] BlockKinds =
    [
        ("For Each", "Next"),
        ("Select Case", "End Select"),
        ("SyncLock", "End SyncLock"),
        ("If", "End If"),
        ("For", "Next"),
        ("While", "End While"),
        ("Do", "Loop"),
        ("Using", "End Using"),
        ("With", "End With"),
        ("Try", "End Try")
    ];

    /// <summary>
    /// Clauses that continue a block without closing it.
    /// </summary>
    private static readonly string[] ContinuationKeywords =
    [
        "ElseIf", "Else", "Case Else", "Case", "Catch", "Finally"
    ];

    private bool AtEnd => _index >= _text.Length;

    private char Current => _text[_index];

    private VbHtmlDocument ParseDocument()
    {
        var document = new VbHtmlDocument();
        ParseInto(document.Nodes, document, closingKeywords: new string[0]);
        return document;
    }

    /// <summary>
    /// Reads nodes until one of the given closings is reached, or the input
    /// ends. Returns the closing that stopped it, or null at end of input.
    /// </summary>
    private string? ParseInto(
        List<VbHtmlNode> into, VbHtmlDocument document, IReadOnlyList<string> closingKeywords)
    {
        var literal = new StringBuilder();
        var literalStart = _index;
        var literalLine = _line;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            into.Add(new HtmlNode(literal.ToString(), literalStart, literalLine));
            literal.Clear();
        }

        while (!AtEnd)
        {
            if (Current == '@')
            {
                // "@@" is how a template writes a literal at sign.
                if (_index + 1 < _text.Length && _text[_index + 1] == '@')
                {
                    // Flushed with the first "@" and resumed after the second, so
                    // every later offset in the node is still the template's: kept
                    // in one run, each was a character early.
                    literal.Append('@');
                    FlushLiteral();
                    Advance(2);
                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                // An at sign inside a word is part of that word: an email
                // address is the everyday case, and Razor has the same
                // heuristic for the same reason.
                if (IsInsideWord())
                {
                    literal.Append('@');
                    Advance();
                    continue;
                }

                // "@*" opens a comment, which produces nothing at all: not a
                // node, not literal text. Without this the comment's text is
                // written into the page, while the editor colours it grey.
                if (_index + 1 < _text.Length && _text[_index + 1] == '*')
                {
                    FlushLiteral();
                    SkipComment(document);

                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                var transitionStart = _index;
                var transitionLine = _line;
                Advance();

                // A closing keyword ends this level rather than starting a node.
                var closing = MatchAny(closingKeywords);
                if (closing is not null)
                {
                    FlushLiteral();
                    Advance(closing.Length);
                    return closing;
                }

                FlushLiteral();
                ParseTransition(into, document, transitionStart, transitionLine);

                literalStart = _index;
                literalLine = _line;
                continue;
            }

            // A block's closing keyword may also stand on its own line with no
            // at sign, which is how Razor's own C# blocks are written and how
            // anyone used to VB writes them. Without this the "End If" was
            // swallowed into the markup: the page rendered the words "End If"
            // and the block ran to the end of the file.
            if (closingKeywords.Count > 0 && AtLineStart())
            {
                var save = _index;
                var saveLine = _line;

                SkipSpacesAndTabs();

                var bare = MatchAny(closingKeywords);

                // Only when the keyword is alone on its line: "Next steps" is
                // prose about what to do, not the end of a For.
                if (bare is not null && RestOfLineIsBlank(_index + bare.Length))
                {
                    FlushLiteral();
                    Advance(bare.Length);
                    return bare;
                }

                _index = save;
                _line = saveLine;
            }

            literal.Append(Current);
            Advance();
        }

        FlushLiteral();
        return null;
    }

    /// <summary>Whether the cursor sits at the start of a line.</summary>
    private bool AtLineStart() => _index == 0 || _text[_index - 1] == '\n';

    /// <summary>Steps over spaces and tabs, stopping at the end of the line.</summary>
    private void SkipSpacesAndTabs()
    {
        while (!AtEnd && (Current == ' ' || Current == '\t')) Advance();
    }

    /// <summary>
    /// Whether nothing but whitespace follows on the line.
    /// </summary>
    /// <remarks>
    /// What separates a closing keyword from a word that merely looks like
    /// one. "Next" alone closes a loop; "Next steps" is prose. The loop
    /// variable in "Next i" is read by the caller, so a bare name is allowed
    /// to follow.
    /// </remarks>
    private bool RestOfLineIsBlank(int from)
    {
        for (var i = from; i < _text.Length; i++)
        {
            var c = _text[i];

            if (c == '\n') return true;
            if (c == '\r' || c == ' ' || c == '\t') continue;

            // "Next i" names the loop variable; anything else on the line
            // means this was not a closing keyword after all.
            if (char.IsLetter(c) || c == '_')
            {
                var word = i;
                while (word < _text.Length &&
                       (char.IsLetterOrDigit(_text[word]) || _text[word] == '_')) word++;

                return RestOfLineIsBlank(word);
            }

            return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the at sign just read sits inside a word.
    ///
    /// "info@example.com" is an address, not a transition. Razor decides the
    /// same way and for the same reason: a template is mostly prose, and
    /// prose contains addresses.
    /// </summary>
    private bool IsInsideWord()
    {
        if (_index == 0) return false;

        var before = _text[_index - 1];

        // Only a letter or digit binds the at sign to what precedes it. A
        // full stop does not: "Hello.@Name" is a transition after a sentence,
        // and treating it as an address would swallow real code.
        if (!char.IsLetterOrDigit(before) && before != '_') return false;

        // Something word-like has to follow too, or "@" ends the word and is
        // a transition after all.
        var after = _index + 1 < _text.Length ? _text[_index + 1] : '\0';

        if (!char.IsLetterOrDigit(after) && after != '_') return false;

        // What precedes must look like the local part of an address rather
        // than markup: "a@Model.Name" inside a <text> block is code, while
        // "info@example.com" is not. The difference is that an address has no
        // markup character between the start of the word and the at sign.
        for (var at = _index - 1; at >= 0; at--)
        {
            var c = _text[at];

            if (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-' || c == '+')
                continue;

            // A word boundary: what sits between it and the at sign is the
            // candidate local part. One character is too short to be one.
            return _index - at - 1 >= 2;
        }

        return _index >= 2;
    }

    /// <summary>
    /// Steps over a <c>@* ... *@</c> comment, which produces nothing.
    ///
    /// The at sign has been read; the star has not. A comment that is never
    /// closed is reported rather than swallowing the rest of the file.
    /// </summary>
    private void SkipComment(VbHtmlDocument document)
    {
        var startLine = _line;

        // Past "@*".
        Advance(2);

        while (!AtEnd)
        {
            if (Current == '*' && _index + 1 < _text.Length && _text[_index + 1] == '@')
            {
                Advance(2);
                return;
            }

            Advance();
        }

        document.Diagnostics.Add(new VbHtmlDiagnostic(
            "VBH003", "This comment is never closed with '*@'.", startLine, 1));
    }

    /// <summary>Handles whatever follows an at sign.</summary>
    private void ParseTransition(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        // "@:" makes the rest of the line literal output. Read before the
        // whitespace is skipped, since the colon follows the at sign directly.
        if (!AtEnd && Current == ':')
        {
            Advance();
            ParseLineOfText(into, document, start, line);
            return;
        }

        // "<text>" is a transition to markup that wraps content without the
        // tag itself appearing in the page.
        if (LooksLikeAt(_index, "<text>"))
        {
            Advance("<text>".Length);

            ParseTextBlock(into, document, start, line);
            return;
        }

        // "@<p>" is a transition to markup inside a code block, which Razor
        // needs because a statement and an element cannot otherwise be told
        // apart there. The element is output, and "@x" inside it is a value:
        // it used to be written as the two characters.
        if (!AtEnd && Current == '<' && IsElementStart(_index))
        {
            ParseElementWithContent(into, document, start, line);
            return;
        }

        SkipHorizontalWhitespace();

        // Directives come first: they are keywords in a position where no
        // expression may appear.
        if (TryReadKeyword("ModelType", out _))
        {
            var value = ReadToEndOfLine().Trim();
            document.ModelType = value;
            into.Add(new DirectiveNode("ModelType", value, start, line));
            return;
        }

        // Coloured as a directive by the editor's grammar since the start,
        // while the parser read it as an expression: the two disagreed, and
        // the page got the word "Inherits" written into it.
        if (TryReadKeyword("Inherits", out _))
        {
            var baseType = ReadToEndOfLine().Trim();

            document.Inherits = baseType;
            into.Add(new DirectiveNode("Inherits", baseType, start, line));
            return;
        }

        // @Layout "_Layout": the layout as a string, which is what a view's
        // Layout property takes. Only with the quote: @layout MainLayout is
        // the Blazor directive, which takes a type and is not supported yet,
        // and @Layout on its own is an expression that writes the property.
        if (LooksLikeLayoutDirective())
        {
            Advance("Layout".Length);

            // Between the quotes, so a comment after them ("' shared") is not
            // part of the name.
            var rest = ReadToEndOfLine();
            var open = rest.IndexOf('"');
            var close = rest.IndexOf('"', open + 1);
            var value = close > open ? rest.Substring(open + 1, close - open - 1) : rest.Trim().Trim('"');

            document.Layout = value;
            into.Add(new DirectiveNode("Layout", value, start, line));
            return;
        }

        // The tag helper directives, as C# Razor has them: which tag helpers
        // are in scope. They used to be refused with VBH008.
        foreach (var kind in TagHelperDirectiveNames)
        {
            if (!LooksLikeKeywordAt(_index, kind)) continue;

            var after = _index + kind.Length;

            if (after < _text.Length && !char.IsWhiteSpace(_text[after])) continue;

            Advance(kind.Length);

            var value = ReadToEndOfLine().Trim().Trim('"').Trim();

            document.TagHelperDirectives.Add(new TagHelperDirective(kind, value));
            into.Add(new DirectiveNode(kind, value, start, line));
            return;
        }

        if (TryReadKeyword("Namespace", out _))
        {
            var value = ReadToEndOfLine().Trim();

            document.Namespace = value;
            document.DeclaresNamespace = true;
            into.Add(new DirectiveNode("Namespace", value, start, line));
            return;
        }

        // @typeparam TItem, or with Visual Basic's constraints:
        // @typeparam TItem As {IComparable, New}. The component becomes generic.
        if (LooksLikeKeywordAt(_index, "typeparam") &&
            _index + "typeparam".Length < _text.Length &&
            _text[_index + "typeparam".Length] is ' ' or '	')
        {
            Advance("typeparam".Length);

            var value = ReadToEndOfLine().Trim();

            document.TypeParameters.Add(value);
            into.Add(new DirectiveNode("typeparam", value, start, line));

            // A view has none; the component callers drop this.
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                VbHtmlDiagnostic.TypeParameterInViewId,
                "@typeparam belongs to Blazor components (.vbrazor); a view cannot be generic.",
                line,
                1));
            return;
        }

        if (TryReadKeyword("Implements", out _))
        {
            var value = ReadToEndOfLine().Trim();

            document.Implements.Add(value);
            into.Add(new DirectiveNode("Implements", value, start, line));
            return;
        }

        if (TryReadKeyword("Attribute", out _))
        {
            var value = ReadToEndOfLine().Trim();

            document.Attributes.Add(value);
            into.Add(new DirectiveNode("Attribute", value, start, line));
            return;
        }

        // Only as a directive: a name, then its value on the same line.
        // "@RenderMode" alone may be a property someone named so.
        if (LooksLikeKeywordAt(_index, "rendermode") &&
            _index + "rendermode".Length < _text.Length &&
            _text[_index + "rendermode".Length] is ' ' or '\t')
        {
            Advance("rendermode".Length);
            SkipHorizontalWhitespace();

            // Where the expression starts, so the generated code can map it
            // back: IntelliSense on "InteractiveServer" describes that word.
            var valuePosition = _index;
            var value = ReadToEndOfLine().TrimEnd();

            document.RenderMode = value;
            document.RenderModePosition = valuePosition;
            document.RenderModeLine = line;
            into.Add(new DirectiveNode("rendermode", value, start, line));

            // Said for every template; a component's callers drop it (see
            // VbHtmlDiagnostic.AppliesTo). A view has no render mode.
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                VbHtmlDiagnostic.RenderModeInViewId,
                "@rendermode belongs to Blazor components (.vbrazor); a view has none. " +
                "Render an interactive component from a view with <component type=\"...\" render-mode=\"...\" />.",
                line,
                1));
            return;
        }

        // "@Model Customer" is how Razor C# spells it; "@ModelType" is the
        // Visual Basic spelling. Both are read, since a template written by
        // someone arriving from C# should not fail over a keyword.
        //
        // "@Using" is deliberately not read as an import: Using also opens a
        // block, and the block is the more common meaning. @Imports is the
        // spelling that always works.
        //
        // Only where a type name follows on the same line: "@Model.Name" in
        // the body is an expression, and reading it as a directive swallowed
        // the rest of the line. A test caught exactly that.
        if (LooksLikeModelDirective() && TryReadKeyword("Model", out _))
        {
            var value = ReadToEndOfLine().Trim();

            document.ModelType = value;
            into.Add(new DirectiveNode("ModelType", value, start, line));
            return;
        }

        if (TryReadKeyword("Imports", out _))
        {
            var value = ReadToEndOfLine().Trim();
            document.Imports.Add(value);
            into.Add(new DirectiveNode("Imports", value, start, line));
            return;
        }

        // "@Inject IClock Clock" asks the container for a service, the way
        // a Razor view in C# does. Razor writes the type first, then the
        // name; Visual Basic would normally write "Clock As IClock", but the
        // directive is Razor's and keeping its order means a C# view can be
        // translated line for line.
        if (TryReadKeyword("Inject", out _))
        {
            var value = ReadToEndOfLine().Trim();

            ParseInjection(document, value, line);
            into.Add(new DirectiveNode("Inject", value, start, line));
            return;
        }

        // "@Page" marks a Razor Page and may carry a route template.
        if (TryReadKeyword("Page", out _))
        {
            var value = ReadToEndOfLine().Trim().Trim('"');

            document.PageRoute = value;
            into.Add(new DirectiveNode("Page", value, start, line));
            return;
        }

        if (TryReadKeyword("Code", out _))
        {
            ParseCodeBlock(into, document, start, line);
            return;
        }

        // Members the view declares for itself. Written into the class rather
        // than into Execute, since a method cannot live inside a method.
        // "@bind" and "@bind-Value" name an attribute, not an expression.
        // Read as one the attribute was split in three before the writer ever
        // saw the tag: <input @bind="@x" /> became the expression "bind"
        // followed by the literal ="  — the binding was lost and a meaningless
        // attribute reached the browser.
        //
        // Left in the markup as written, so the tag arrives whole and the
        // writer turns it into the pair of attributes a binding really is.
        if (LooksLikeKeywordAt(_index, "bind") || LooksLikeDirectiveAttribute())
        {
            into.Add(new HtmlNode("@", start, line));
            return;
        }

        if (TryReadKeyword("Functions", out _))
        {
            ParseFunctionsBlock(into, document, start, line);
            return;
        }

        // A named piece of markup for the layout to place.
        if (TryReadKeyword("Section", out _))
        {
            ParseSection(into, document, start, line);
            return;
        }

        // "@Await Html.PartialAsync(...)" writes the result of an async call.
        // Without this, Await was read as the name of a variable and the call
        // beside it became literal text on the page.
        if (TryReadKeyword("Await", out _))
        {
            SkipSpacesAndTabs();
            ParseExpression(into, start, line, awaited: true);
            return;
        }

        // A block opening: If, For Each, While and the rest. Except when the
        // word is not opening a block at all — "@If(a, b, c)" is Visual
        // Basic's conditional operator, and reading it as an If statement
        // swallowed the rest of the line into a block with no end.
        foreach (var (opening, closing) in BlockKinds)
        {
            if (IsOperatorNotBlock(opening)) continue;

            if (!TryReadKeyword(opening, out _)) continue;

            ParseBlock(into, document, opening, closing, start, line);
            return;
        }

        // A directive Basalt does not have. Said plainly rather than written
        // into the page as an expression: about twenty constructs used to
        // fail that way, and a template author had no way to tell whether
        // something was unsupported or simply mistyped.
        if (UnsupportedDirective() is { } unsupported)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH008", Explain(unsupported), line, 1));

            ReadToEndOfLine();
            return;
        }

        // Anything else is an expression to write out.
        ParseExpression(into, start, line);
    }

    /// <summary>
    /// Reads the rest of a line as output, for <c>@:</c>.
    ///
    /// The line is markup, not a string: it may contain expressions, so it is
    /// parsed rather than taken verbatim. Only the newline ends it.
    /// </summary>
    private void ParseLineOfText(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        var literal = new System.Text.StringBuilder();
        var literalStart = _index;
        var literalLine = _line;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            into.Add(new HtmlNode(literal.ToString(), literalStart, literalLine));
            literal.Clear();
        }

        while (!AtEnd && Current != '\n')
        {
            if (Current == '@')
            {
                if (_index + 1 < _text.Length && _text[_index + 1] == '@')
                {
                    // Flushed with the first "@" and resumed after the second, so
                    // every later offset in the node is still the template's: kept
                    // in one run, each was a character early.
                    literal.Append('@');
                    FlushLiteral();
                    Advance(2);
                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                if (IsInsideWord())
                {
                    literal.Append('@');
                    Advance();
                    continue;
                }

                var transitionStart = _index;
                var transitionLine = _line;

                Advance();
                FlushLiteral();
                ParseTransition(into, document, transitionStart, transitionLine);

                literalStart = _index;
                literalLine = _line;
                continue;
            }

            literal.Append(Current);
            Advance();
        }

        // The newline belongs to the output: "@:one" and "@:two" on separate
        // lines produce two lines, as they do in Razor.
        if (!AtEnd && Current == '\n')
        {
            literal.Append('\n');
            Advance();
        }

        FlushLiteral();
    }

    /// <summary>
    /// Reads a <c>&lt;text&gt;...&lt;/text&gt;</c> block.
    ///
    /// The tags mark a run of markup and do not appear in the page, which is
    /// what they are for: writing several lines of output from inside a block
    /// without wrapping them in an element.
    /// </summary>
    private void ParseTextBlock(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        var literal = new System.Text.StringBuilder();
        var literalStart = _index;
        var literalLine = _line;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            into.Add(new HtmlNode(literal.ToString(), literalStart, literalLine));
            literal.Clear();
        }

        while (!AtEnd)
        {
            if (Current == '<' && LooksLikeAt(_index, "</text>"))
            {
                Advance("</text>".Length);
                FlushLiteral();
                return;
            }

            if (Current == '@')
            {
                if (_index + 1 < _text.Length && _text[_index + 1] == '@')
                {
                    // Flushed with the first "@" and resumed after the second, so
                    // every later offset in the node is still the template's: kept
                    // in one run, each was a character early.
                    literal.Append('@');
                    FlushLiteral();
                    Advance(2);
                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                if (IsInsideWord())
                {
                    literal.Append('@');
                    Advance();
                    continue;
                }

                var transitionStart = _index;
                var transitionLine = _line;

                Advance();
                FlushLiteral();
                ParseTransition(into, document, transitionStart, transitionLine);

                literalStart = _index;
                literalLine = _line;
                continue;
            }

            literal.Append(Current);
            Advance();
        }

        FlushLiteral();

        document.Diagnostics.Add(new VbHtmlDiagnostic(
            "VBH004", "This <text> block is never closed with '</text>'.", line, 1));
    }

    /// <summary>
    /// Reads <c>@Functions ... End Functions</c>.
    ///
    /// The text is taken as written and put on the document: it becomes
    /// members of the generated class, so the parser has nothing to say about
    /// its shape beyond finding where it ends.
    /// </summary>
    private void ParseFunctionsBlock(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        // Where the members begin, past the line break after the keyword, so
        // they can be mapped line for line like the body of a code block.
        var bodyLine = line + LineBreaksBefore();
        var bodyStart = _index;

        while (bodyStart < _text.Length && char.IsWhiteSpace(_text[bodyStart])) bodyStart++;

        var body = ReadUntilKeyword("End Functions", out var closed);

        if (!closed)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH005", "This @Functions block is never closed with 'End Functions'.",
                line, 1));
            return;
        }

        document.Functions.Add(body.Trim());
        into.Add(new FunctionsNode(body.Trim(), start, line, bodyLine, bodyStart));
    }

    /// <summary>
    /// Reads <c>@Section Name ... End Section</c>.
    ///
    /// The body is markup and is parsed as such: a section holds a page
    /// fragment, expressions and all, not a string.
    /// </summary>
    private void ParseSection(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        SkipHorizontalWhitespace();

        var name = ReadIdentifier();

        if (name.Length == 0)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH006", "This @Section has no name.", line, 1));
            return;
        }

        var section = new SectionNode(name, start, line);

        var closing = ParseInto(section.Body, document, new[] { "End Section" });

        if (closing is null)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH007", $"The section '{name}' is never closed with 'End Section'.",
                line, 1));
        }

        into.Add(section);
    }

    /// <summary>An identifier at the current position, or an empty string.</summary>
    private string ReadIdentifier()
    {
        var start = _index;

        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_')) Advance();

        return _text.Substring(start, _index - start);
    }

    /// <summary>
    /// Reads raw text up to a keyword, which is consumed.
    ///
    /// Used where the content is Visual Basic the parser does not read: it
    /// only has to find the end.
    /// </summary>
    private string ReadUntilKeyword(string keyword, out bool closed)
    {
        var start = _index;

        while (!AtEnd)
        {
            if (LooksLikeKeywordAt(_index, keyword))
            {
                // Back over the "@" that introduces the closing keyword: the
                // scan matches "End Functions" and the template writes
                // "@End Functions", so the marker was left at the end of the
                // body and written into the class as a stray line. The
                // generated Visual Basic did not compile, and every template
                // using @Functions was affected — views included.
                var end = _index;

                if (end > start && _text[end - 1] == '@') end--;

                var body = _text.Substring(start, end - start);

                Advance(SkipToKeywordEnd(keyword));
                closed = true;

                return body;
            }

            Advance();
        }

        closed = false;

        return _text.Substring(start);
    }

    /// <summary>How far past the current position a multi-word keyword runs.</summary>
    private int SkipToKeywordEnd(string keyword)
    {
        var at = _index;

        foreach (var part in keyword.Split(' '))
        {
            while (at < _text.Length && (_text[at] == ' ' || _text[at] == '\t')) at++;
            at += part.Length;
        }

        return at - _index;
    }

    /// <summary>
    /// Reads the type and name from an <c>@Inject</c> directive.
    /// </summary>
    /// <remarks>
    /// Both orders are accepted: Razor's "IClock Clock" and Visual Basic's
    /// own "Clock As IClock". A view translated from C# keeps its lines, and
    /// one written from scratch reads like Visual Basic.
    /// </remarks>
    private void ParseInjection(VbHtmlDocument document, string value, int line)
    {
        if (value.Length == 0)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH010", "@Inject needs a type and a name.", line, 1));
            return;
        }

        var asIndex = value.IndexOf(" As ", StringComparison.OrdinalIgnoreCase);

        if (asIndex > 0)
        {
            document.Injected.Add(new InjectedService(
                value.Substring(asIndex + 4).Trim(),
                value.Substring(0, asIndex).Trim()));
            return;
        }

        var space = value.LastIndexOf(' ');

        if (space <= 0)
        {
            document.Diagnostics.Add(new VbHtmlDiagnostic(
                "VBH010",
                $"@Inject needs a type and a name: \"{value}\" has only one.",
                line, 1));
            return;
        }

        document.Injected.Add(new InjectedService(
            value.Substring(0, space).Trim(),
            value.Substring(space + 1).Trim()));
    }

    /// <summary>
    /// Whether a less-than sign here opens an element rather than being a
    /// comparison.
    /// </summary>
    /// <remarks>
    /// "@&lt;p&gt;" is markup; "@&lt;" alone is not something a template
    /// writes. A letter must follow, as in every element name.
    /// </remarks>
    private bool IsElementStart(int at) =>
        at + 1 < _text.Length && char.IsLetter(_text[at + 1]);

    /// <summary>
    /// Reads an element and its content as literal markup.
    /// </summary>
    /// <remarks>
    /// Nesting is counted so that an element containing one of its own kind
    /// ends where it really ends: a &lt;div&gt; holding a &lt;div&gt; used to
    /// stop at the inner closing tag and spill the rest into the code around
    /// it.
    /// </remarks>
    private void ParseElementAsMarkup(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        var name = ElementNameAt(_index);

        if (name.Length == 0)
        {
            // Not an element after all: write the sign and carry on rather
            // than consuming the rest of the file looking for a close.
            into.Add(new HtmlNode("<", start, line));
            Advance();
            return;
        }

        var markupStart = _index;
        var depth = 0;

        while (!AtEnd)
        {
            if (Current != '<')
            {
                Advance();
                continue;
            }

            var closing = _index + 1 < _text.Length && _text[_index + 1] == '/';
            var here = ElementNameAt(closing ? _index + 1 : _index);

            var tagEnd = _text.IndexOf('>', _index);
            if (tagEnd < 0) break;

            // Self-closing tags open nothing, so they must not be counted.
            var selfClosing = tagEnd > 0 && _text[tagEnd - 1] == '/';

            var sameElement = string.Equals(here, name, StringComparison.OrdinalIgnoreCase);

            while (_index <= tagEnd) Advance();

            if (!sameElement) continue;

            if (closing)
            {
                depth--;
                if (depth == 0) break;
            }
            else if (!selfClosing)
            {
                depth++;
            }
            else if (depth == 0)
            {
                break;
            }
        }

        into.Add(new HtmlNode(
            _text.Substring(markupStart, _index - markupStart), start, line));
    }

    /// <summary>The element name a tag starts with, or an empty string.</summary>
    private string ElementNameAt(int at)
    {
        var index = at + 1;
        var nameStart = index;

        while (index < _text.Length &&
               (char.IsLetterOrDigit(_text[index]) || _text[index] is '-' or '_'))
            index++;

        return _text.Substring(nameStart, index - nameStart);
    }

    /// <summary>Reads a <c>@Code ... End Code</c> block of plain statements.</summary>
    private void ParseCodeBlock(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        var code = new StringBuilder();

        // Where the code really begins: "@Code" is usually followed by a line
        // break, and the pragma has to name the line the first statement is
        // on rather than the line the keyword is on.
        var bodyLine = line + LineBreaksBefore();

        // Where the code itself begins, past the line break and the indent
        // that follow the keyword. The mapping is anchored here: from the
        // keyword instead, the entry covered "@Code" and stopped short of
        // the body's end, so a caret in the last statements mapped nowhere.
        var bodyStart = _index;

        while (bodyStart < _text.Length && char.IsWhiteSpace(_text[bodyStart])) bodyStart++;

        // Markup inside the block ends the statements before it and starts a
        // new run after it, each run mapped from where its own code begins.
        var wroteMarkup = false;
        var flushedAny = false;

        void FlushCode()
        {
            var text = code.ToString();
            var trimmed = text.Trim();

            if (trimmed.Length > 0)
            {
                var leading = text.Length - text.TrimStart().Length;
                var segmentStart = _index - text.Length + leading;
                var segmentLine = _line - CountBreaks(text, leading, text.Length);

                // The first run carries the "@Code" keyword; a later one is
                // placed where its own code starts.
                into.Add(flushedAny
                    ? new StatementNode(trimmed, segmentStart, segmentLine, segmentLine, segmentStart,
                        isContinuation: true)
                    : new StatementNode(trimmed, start, line, segmentLine, segmentStart));

                flushedAny = true;
            }

            code.Clear();
        }

        while (!AtEnd)
        {
            // "@<li>…</li>" or "@:text" at the start of a line: markup inside
            // the block, as a Visual Basic view written for MVC 5 has it. The
            // "@" and the line it sits on used to be passed to the compiler
            // as code, which failed on the first one.
            if (Current == '@' && AtStartOfCodeLine(code) && _index + 1 < _text.Length &&
                (_text[_index + 1] == ':' || (_text[_index + 1] == '<' && IsElementStart(_index + 1))))
            {
                FlushCode();

                var transitionStart = _index;
                var transitionLine = _line;

                Advance();

                if (Current == ':')
                {
                    Advance();
                    ParseLineOfText(into, document, transitionStart, transitionLine);
                }
                else
                {
                    ParseElementWithContent(into, document, transitionStart, transitionLine);
                }

                wroteMarkup = true;
                continue;
            }

            // A string or a comment may contain the closing keyword, and used
            // to end the block there: Dim s = "End Code" cut it in half.
            if (Current == '"')
            {
                code.Append(ReadStringLiteral());
                continue;
            }

            if (Current == '\'')
            {
                while (!AtEnd && Current != '\n')
                {
                    code.Append(Current);
                    Advance();
                }

                continue;
            }

            if (Current == 'E' && LooksLikeKeywordAt(_index, "End Code"))
            {
                if (wroteMarkup)
                {
                    FlushCode();
                }
                else
                {
                    into.Add(new StatementNode(
                        code.ToString().Trim(), start, line, bodyLine, bodyStart));
                }

                Advance("End Code".Length);
                return;
            }

            code.Append(Current);
            Advance();
        }

        document.Diagnostics.Add(new VbHtmlDiagnostic(
            "VBH001", "'@Code' is not closed by a matching 'End Code'.", line, 1));

        if (wroteMarkup)
            FlushCode();
        else
            into.Add(new StatementNode(code.ToString().Trim(), start, line, bodyLine, bodyStart));
    }

    /// <summary>
    /// Whether nothing but indentation stands between the last line break of
    /// the code read so far and here.
    /// </summary>
    private static bool AtStartOfCodeLine(StringBuilder code)
    {
        for (var at = code.Length - 1; at >= 0; at--)
        {
            if (code[at] == '\n') return true;
            if (code[at] != ' ' && code[at] != '\t' && code[at] != '\r') return false;
        }

        return true;
    }

    /// <summary>How many line breaks a stretch of text holds.</summary>
    private static int CountBreaks(string text, int from, int to)
    {
        var breaks = 0;

        for (var at = from; at < to; at++)
            if (text[at] == '\n') breaks++;

        return breaks;
    }

    /// <summary>
    /// Reads an element, "@&lt;li&gt;…&lt;/li&gt;", as markup whose content may
    /// hold expressions.
    /// </summary>
    /// <remarks>
    /// The extent is found first, counting nested elements of the same name
    /// as <see cref="ParseElementAsMarkup"/> does; then the markup inside it
    /// is read with its transitions, so "@x" in it is written as a value, not
    /// as the two characters.
    /// </remarks>
    private void ParseElementWithContent(
        List<VbHtmlNode> into, VbHtmlDocument document, int start, int line)
    {
        // Style and script hold CSS and JavaScript, where "@media" and
        // "@keyframes" are not Visual Basic: their content stays literal, as
        // it always was.
        if (IsRawTextElementAt(_index))
        {
            ParseElementAsMarkup(into, document, start, line);
            return;
        }

        var end = EndOfElement(_index);

        // An element whose closing tag cannot be found — "<li>" left open —
        // is taken to the end of its line rather than to the end of the file,
        // which swallowed "End Code" and everything after it.
        if (end < 0) end = EndOfLineAt(_index);

        var literal = new StringBuilder();
        var literalStart = _index;
        var literalLine = _line;

        void FlushLiteral()
        {
            if (literal.Length == 0) return;

            into.Add(new HtmlNode(literal.ToString(), literalStart, literalLine));
            literal.Clear();
        }

        while (!AtEnd && _index < end)
        {
            if (Current == '@' && !IsInsideWord())
            {
                if (_index + 1 < _text.Length && _text[_index + 1] == '@')
                {
                    // Flushed with the first "@" and resumed after the second, so
                    // every later offset in the node is still the template's: kept
                    // in one run, each was a character early.
                    literal.Append('@');
                    FlushLiteral();
                    Advance(2);
                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                // A comment inside the element produces nothing.
                if (_index + 1 < _text.Length && _text[_index + 1] == '*')
                {
                    FlushLiteral();
                    SkipComment(document);

                    literalStart = _index;
                    literalLine = _line;
                    continue;
                }

                var transitionStart = _index;
                var transitionLine = _line;

                Advance();
                FlushLiteral();
                ParseTransition(into, document, transitionStart, transitionLine);

                literalStart = _index;
                literalLine = _line;
                continue;
            }

            // A style or script nested inside is CSS or JavaScript as well:
            // copied as written, to its end.
            if (Current == '<' && IsRawTextElementAt(_index) && EndOfElement(_index) is var rawEnd && rawEnd > 0)
            {
                while (_index < rawEnd && !AtEnd)
                {
                    literal.Append(Current);
                    Advance();
                }

                continue;
            }

            literal.Append(Current);
            Advance();
        }

        FlushLiteral();
    }

    /// <summary>Whether a style or script element opens here.</summary>
    private bool IsRawTextElementAt(int at)
    {
        if (at + 1 >= _text.Length || _text[at + 1] == '/') return false;

        var name = ElementNameAt(at);

        return name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("script", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Where the element starting here ends, just past its closing tag, or -1.
    /// </summary>
    private int EndOfElement(int at)
    {
        var name = ElementNameAt(at);

        if (name.Length == 0) return -1;

        var firstTagEnd = _text.IndexOf('>', at);

        if (firstTagEnd < 0) return -1;

        // A void element has no closing tag, written with a slash or not:
        // "<br>" waited for a "</br>" that never comes.
        if (VoidElements.Contains(name)) return firstTagEnd + 1;

        // Never past the end of the code block the element sits in: a tag
        // left open must not reach a matching one further down the page.
        var limit = EndCodeLineAfter(at);

        var index = at;
        var depth = 0;

        while (index < _text.Length)
        {
            var open = _text.IndexOf('<', index);

            if (open < 0 || open >= limit) return -1;

            var closing = open + 1 < _text.Length && _text[open + 1] == '/';
            var here = ElementNameAt(closing ? open + 1 : open);

            // Not a tag at all — "@(i < 3)" — so the next ">" is not its end.
            if (here.Length == 0)
            {
                index = open + 1;
                continue;
            }

            var tagEnd = _text.IndexOf('>', open);

            if (tagEnd < 0) return -1;

            index = tagEnd + 1;

            if (!string.Equals(here, name, StringComparison.OrdinalIgnoreCase)) continue;

            var selfClosing = _text[tagEnd - 1] == '/';

            if (closing)
            {
                depth--;
                if (depth == 0) return index;
            }
            else if (!selfClosing)
            {
                depth++;
            }
            else if (depth == 0)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>The elements HTML defines without a closing tag.</summary>
    private static readonly HashSet<string> VoidElements = new(StringComparer.OrdinalIgnoreCase)
    {
        "area", "base", "br", "col", "embed", "hr", "img", "input",
        "link", "meta", "param", "source", "track", "wbr",
    };

    /// <summary>Where the line holding an offset ends.</summary>
    private int EndOfLineAt(int at)
    {
        var end = _text.IndexOf('\n', at);

        return end < 0 ? _text.Length : end;
    }

    /// <summary>
    /// Where the next line beginning with "End Code" starts, or the end of the text.
    /// </summary>
    private int EndCodeLineAfter(int at)
    {
        // Found once per document: scanning from each element to the next
        // "End Code" made parsing grow with elements times file size, and the
        // language server parses on every keystroke.
        _endCodeLines ??= FindEndCodeLines();

        foreach (var lineStart in _endCodeLines)
            if (lineStart >= at) return lineStart;

        return _text.Length;
    }

    private List<int>? _endCodeLines;

    /// <summary>Where each line beginning with "End Code" starts, in order.</summary>
    private List<int> FindEndCodeLines()
    {
        var starts = new List<int>();
        var from = 0;

        while (true)
        {
            var found = _text.IndexOf("End Code", from, StringComparison.OrdinalIgnoreCase);

            if (found < 0) return starts;

            var lineStart = found;

            while (lineStart > 0 && (_text[lineStart - 1] == ' ' || _text[lineStart - 1] == '\t')) lineStart--;

            if (lineStart == 0 || _text[lineStart - 1] == '\n') starts.Add(lineStart);

            from = found + "End Code".Length;
        }
    }

    /// <summary>
    /// Reads a control-flow block: its opening clause, its body, any
    /// continuation clauses, and its closing keyword.
    /// </summary>
    /// <summary>
    /// The Razor directive at this position, when it is one Basalt has not.
    ///
    /// Only where it reads as a directive: a name followed by a space and a
    /// value, at the start of a transition. "@page" in prose is not one.
    /// </summary>
    private string? UnsupportedDirective()
    {
        foreach (var name in NotSupported)
        {
            if (!LooksLikeKeywordAt(_index, name)) continue;

            // A directive is followed by its argument or ends the line;
            // "@Layout</p>" is the view's Layout property written out, and
            // used to be refused as though it were the directive.
            var after = _index + name.Length;

            if (after >= _text.Length || char.IsWhiteSpace(_text[after]))
                return name;
        }

        return null;
    }

    /// <summary>
    /// Directives Razor has and Basalt does not.
    ///
    /// The Blazor ones are here because a template copied from a component
    /// will contain them, and saying "not supported" is more use than writing
    /// the word into the page.
    /// </summary>
    /// <summary>The directives that decide which tag helpers apply.</summary>
    private static readonly string[] TagHelperDirectiveNames =
        ["addTagHelper", "removeTagHelper", "tagHelperPrefix"];

    private static readonly string[] NotSupported =
    [
        // The tag helper directives are read before this list is consulted.
        "preservewhitespace", "helper", "layout"
    ];

    /// <summary>What to say about a directive that is not supported.</summary>
    private static string Explain(string name) => name.ToLowerInvariant() switch
    {
        "layout" => "Write the layout as a string — @Layout \"_Layout\" — or set it in a "
                  + "_ViewStart.vbhtml. The Blazor form, @layout with a type, is not supported yet.",

        "helper" => "@helper is a WebPages feature that ASP.NET Core never carried "
                  + "forward. Use @Functions instead.",

        "preservewhitespace" =>
            $"@{name} belongs to Blazor components and is not supported in .vbrazor yet.",

        _ => $"@{name} is not supported by Basalt."
    };

    /// <summary>
    /// Whether the "@" just read opens one of Blazor's directive attributes —
    /// @onclick, @onclick:preventDefault, @ref, @key, @attributes, @formname,
    /// @rendermode — rather than an expression.
    /// </summary>
    /// <remarks>
    /// Only in an attribute's place: after white space, and followed by "="
    /// or ":". "@online" in prose is still an expression. Read as one, the
    /// attribute was split before the writer saw the tag: @onclick became
    /// the value of a variable called onclick.
    /// </remarks>
    private bool LooksLikeDirectiveAttribute()
    {
        var at = _index - 1;

        if (at <= 0 || _text[at] != '@' || !char.IsWhiteSpace(_text[at - 1])) return false;

        var end = _index;

        while (end < _text.Length && (char.IsLetterOrDigit(_text[end]) || _text[end] == '-')) end++;

        if (end >= _text.Length) return false;

        var name = _text.Substring(_index, end - _index);
        var isEvent = name.Length > 2 && name.StartsWith("on", StringComparison.Ordinal) && char.IsLower(name[2]);

        // A colon only for an event's own flags: "@online: 5" in prose is
        // still the variable followed by a colon.
        if (_text[end] == ':')
        {
            return isEvent &&
                (string.CompareOrdinal(_text, end + 1, "preventDefault", 0, "preventDefault".Length) == 0 ||
                 string.CompareOrdinal(_text, end + 1, "stopPropagation", 0, "stopPropagation".Length) == 0);
        }

        if (_text[end] != '=') return false;

        return isEvent || name is "ref" or "key" or "attributes" or "formname" or "rendermode";
    }

    /// <summary>
    /// Whether "Layout" here opens the directive: the word, spaces, then a quote.
    /// </summary>
    private bool LooksLikeLayoutDirective()
    {
        if (!LooksLikeKeywordAt(_index, "Layout")) return false;

        var at = _index + "Layout".Length;

        if (at >= _text.Length || (_text[at] != ' ' && _text[at] != '\t')) return false;

        while (at < _text.Length && (_text[at] == ' ' || _text[at] == '\t')) at++;

        return at < _text.Length && _text[at] == '"';
    }

    /// <summary>
    /// Whether "Model" here opens the directive rather than an expression.
    ///
    /// The directive is "@Model Customer": a space, then a type. An
    /// expression is "@Model.Name" or "@Model(0)", where what follows the
    /// word is punctuation.
    /// </summary>
    private bool LooksLikeModelDirective()
    {
        if (!LooksLikeKeywordAt(_index, "Model")) return false;

        var at = _index + "Model".Length;

        // A space, then something that could be a type name.
        if (at >= _text.Length || (_text[at] != ' ' && _text[at] != '\t')) return false;

        while (at < _text.Length && (_text[at] == ' ' || _text[at] == '\t')) at++;

        return at < _text.Length && (char.IsLetter(_text[at]) || _text[at] == '_');
    }

    /// <summary>
    /// A loop variable written after a closing keyword, as in "Next i".
    ///
    /// Only an identifier: anything else on that line is markup and stays
    /// markup.
    /// </summary>
    private string ReadLoopVariable()
    {
        var save = _index;
        var saveLine = _line;

        SkipHorizontalWhitespace();

        var start = _index;

        while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_')) Advance();

        var name = _text.Substring(start, _index - start);

        // Nothing identifier-like: put the position back so the text is read
        // as the markup it is.
        if (name.Length == 0)
        {
            _index = save;
            _line = saveLine;
        }

        return name;
    }

    /// <summary>
    /// Whether an opening line is a complete statement rather than a block.
    ///
    /// Visual Basic allows "If x Then DoIt()" on one line, and it closes
    /// itself. Only If has this form, and only when something follows Then.
    /// </summary>
    private static bool IsSingleLine(string opening, string clause)
    {
        if (!string.Equals(opening, "If", StringComparison.OrdinalIgnoreCase)) return false;

        var then = clause.LastIndexOf("Then", StringComparison.OrdinalIgnoreCase);

        if (then < 0) return false;

        // Something after Then, and not a comment.
        var after = clause.Substring(then + 4).Trim();

        return after.Length > 0 && !after.StartsWith("'", StringComparison.Ordinal);
    }

    private void ParseBlock(
        List<VbHtmlNode> into,
        VbHtmlDocument document,
        string opening,
        string closing,
        int start,
        int line)
    {
        // The rest of the opening line is the clause: "x > 0 Then" after "If".
        var clause = ReadToEndOfLine().TrimEnd();

        // "If x Then Y()" is a whole statement on one line, not a block: it
        // used to be read as one and swallow the rest of the file looking for
        // an "@End If" that was never coming.
        if (IsSingleLine(opening, clause))
        {
            into.Add(new StatementNode($"{opening} {clause.Trim()}".Trim(), start, line));
            return;
        }

        var block = new BlockNode($"{opening} {clause.Trim()}".Trim(), closing, start, line);

        // Continuations end a section without ending the block, so they stop
        // the body just as the real closing does.
        var stoppers = new List<string> { closing };
        stoppers.AddRange(ContinuationKeywords);

        var current = block.Body;

        while (true)
        {
            var stopped = ParseInto(current, document, stoppers);

            if (stopped is null)
            {
                document.Diagnostics.Add(new VbHtmlDiagnostic(
                    "VBH002",
                    $"'{opening}' is not closed by a matching '{closing}'.",
                    line, 1));
                break;
            }

            if (string.Equals(stopped, closing, StringComparison.OrdinalIgnoreCase))
            {
                // ParseInto has just consumed the keyword, which sits on the
                // current line: nothing after it has been read yet.
                block.ClosingPosition = _index - stopped.Length;
                block.ClosingLine = _line;

                // "@Next i" names the loop variable after the keyword. It
                // belongs to the closing statement, and used to be left
                // behind as literal text — so the page got a stray " i".
                var trailing = ReadLoopVariable();

                if (trailing.Length > 0)
                    block.Closing = $"{closing} {trailing}";

                break;
            }

            // A continuation: read its own clause and start a new section.
            var keywordPosition = _index - stopped.Length;
            var keywordLine = _line;

            var continuationClause = ReadToEndOfLine().TrimEnd();

            var section = new BlockClause(
                $"{stopped} {continuationClause.Trim()}".Trim(), keywordPosition, keywordLine);
            block.Clauses.Add(section);
            current = section.Body;
        }

        into.Add(block);
    }

    /// <summary>
    /// Reads an expression to write out, either parenthesised or a bare member
    /// chain.
    /// </summary>
    private void ParseExpression(
        List<VbHtmlNode> into, int start, int line, bool awaited = false)
    {
        var raw = false;

        // "@AddressOf Handler" is one expression, not the word AddressOf
        // followed by text. Read as a member chain it stopped at the keyword
        // and left the method name in the markup, so onclick="@AddressOf Go"
        // produced AddAttribute(.., AddressOf) and an unterminated string
        // after it — the component did not compile.
        //
        // The keyword stays in the expression rather than becoming a flag:
        // it is Visual Basic as written, and every writer passes it through
        // untouched.
        var prefix = string.Empty;

        if (LooksLikeKeywordAt(_index, "AddressOf"))
        {
            var keyword = _index;

            Advance("AddressOf".Length);
            SkipSpacesAndTabs();

            // As written, spaces included, so the expression's text is the
            // template's text and a caret maps character for character.
            prefix = _text.Substring(keyword, _index - keyword);
        }

        // @Html.Raw(...) writes its argument without encoding.
        if (LooksLikeKeywordAt(_index, "Html.Raw"))
        {
            Advance("Html.Raw".Length);
            raw = true;
        }

        string expression;

        // Where the text of the expression starts, for the mapping: here, or
        // one past an opening parenthesis, past any spaces inside it.
        var expressionStart = _index;

        if (!AtEnd && Current == '(')
        {
            expression = ReadBalancedParentheses();

            // Strip the outer parentheses the template wrote.
            if (expression.Length >= 2)
            {
                expression = expression.Substring(1, expression.Length - 2);
                expressionStart++;
            }
        }
        else
        {
            expression = ReadMemberChain();
        }

        expressionStart += expression.Length - expression.TrimStart().Length;

        // "AddressOf " stays part of the expression and starts at the "@".
        if (prefix.Length > 0) expressionStart = start + 1;

        into.Add(new ExpressionNode(
            prefix + expression.Trim(), raw, start, line, awaited, expressionStart));
    }

    /// <summary>
    /// Reads a bare expression: an identifier followed by member accesses and
    /// call or index arguments.
    ///
    /// Stops at the first character that cannot continue the chain, which is
    /// how Razor decides where markup resumes.
    /// </summary>
    private string ReadMemberChain()
    {
        var builder = new StringBuilder();

        // "New" is the one expression keyword that must be followed by a
        // space, and a space is otherwise where a chain ends. Without this
        // "@New List(Of String)().Count" read as the single word "New" and
        // wrote the rest of the line into the page as markup.
        if (LooksLikeKeywordAt(_index, "New"))
        {
            builder.Append(_text, _index, 3);
            Advance(3);

            while (!AtEnd && (Current == ' ' || Current == '\t'))
            {
                builder.Append(Current);
                Advance();
            }
        }

        while (!AtEnd)
        {
            var c = Current;

            if (char.IsLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
                Advance();
                continue;
            }

            if (c is '(' or '[')
            {
                builder.Append(ReadBalancedParentheses());
                continue;
            }

            // A dot continues the chain only when a name follows it. Otherwise
            // it is sentence punctuation and belongs to the markup.
            if (c == '.' && _index + 1 < _text.Length &&
                (char.IsLetter(_text[_index + 1]) || _text[_index + 1] == '_'))
            {
                builder.Append(c);
                Advance();
                continue;
            }

            break;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a parenthesised or bracketed run, respecting nesting and string
    /// literals so that a bracket inside a string does not end it.
    /// </summary>
    private string ReadBalancedParentheses()
    {
        var open = Current;
        var close = open == '(' ? ')' : ']';

        var builder = new StringBuilder();
        var depth = 0;

        while (!AtEnd)
        {
            var c = Current;

            if (c == '"')
            {
                builder.Append(ReadStringLiteral());
                continue;
            }

            if (c == open) depth++;
            else if (c == close) depth--;

            builder.Append(c);
            Advance();

            if (depth == 0) break;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a Visual Basic string literal, where a quote is escaped by
    /// doubling it.
    /// </summary>
    private string ReadStringLiteral()
    {
        var builder = new StringBuilder();
        builder.Append(Current);
        Advance();

        while (!AtEnd)
        {
            if (Current == '"')
            {
                builder.Append(Current);
                Advance();

                // A doubled quote is an escaped quote, not the end.
                if (!AtEnd && Current == '"')
                {
                    builder.Append(Current);
                    Advance();
                    continue;
                }

                break;
            }

            builder.Append(Current);
            Advance();
        }

        return builder.ToString();
    }

    private string ReadToEndOfLine()
    {
        var builder = new StringBuilder();

        while (!AtEnd && Current != '\n')
        {
            if (Current == '"')
            {
                builder.Append(ReadStringLiteral());
                continue;
            }

            builder.Append(Current);
            Advance();
        }

        return builder.ToString();
    }

    /// <summary>Which of the given keywords appears at the cursor, if any.</summary>
    private string? MatchAny(IReadOnlyList<string> keywords)
    {
        foreach (var keyword in keywords)
            if (LooksLikeKeywordAt(_index, keyword))
                return keyword;

        return null;
    }

    /// <summary>
    /// Whether a block keyword is really the start of an expression.
    ///
    /// Visual Basic overloads two words. <c>If(condition, a, b)</c> is the
    /// conditional operator and <c>If(o, fallback)</c> the null-coalescing
    /// one — both expressions — while <c>If condition Then</c> opens a block.
    /// The parenthesis immediately after the word is what separates them, and
    /// the same is true of <c>Do</c> in no realistic template, so only If is
    /// treated this way.
    /// </summary>
    private bool IsOperatorNotBlock(string keyword)
    {
        if (!string.Equals(keyword, "If", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!LooksLikeAt(_index, "If")) return false;

        // No space: "If(" is the operator, "If (" is a parenthesised condition.
        return _index + 2 < _text.Length && _text[_index + 2] == '(';
    }

    /// <summary>
    /// How many line breaks sit between here and the first non-blank
    /// character, which is how far the body is from the keyword.
    /// </summary>
    private int LineBreaksBefore()
    {
        var breaks = 0;

        for (var at = _index; at < _text.Length; at++)
        {
            if (_text[at] == '\n') breaks++;
            else if (!char.IsWhiteSpace(_text[at])) break;
        }

        return breaks;
    }

    private bool TryReadKeyword(string keyword, out string matched)
    {
        matched = keyword;

        if (!LooksLikeKeywordAt(_index, keyword)) return false;

        Advance(keyword.Length);
        return true;
    }

    /// <summary>
    /// Whether some exact text sits at an offset, case-insensitively.
    ///
    /// Unlike the keyword check, this wants no word boundary and treats
    /// spaces literally: "&lt;/text&gt;" is punctuation, not an identifier.
    /// </summary>
    private bool LooksLikeAt(int offset, string text) =>
        offset + text.Length <= _text.Length
        && string.Compare(_text, offset, text, 0, text.Length,
               StringComparison.OrdinalIgnoreCase) == 0;

    /// <summary>
    /// Whether a keyword sits at the given offset, matched case-insensitively
    /// and not running into a longer identifier.
    ///
    /// Multi-word keywords such as "For Each" may be separated by any run of
    /// spaces, as Visual Basic allows.
    /// </summary>
    private bool LooksLikeKeywordAt(int offset, string keyword)
    {
        var at = offset;

        foreach (var part in keyword.Split(' '))
        {
            while (at < _text.Length && (_text[at] == ' ' || _text[at] == '\t')) at++;

            if (at + part.Length > _text.Length) return false;

            if (string.Compare(_text, at, part, 0, part.Length,
                    StringComparison.OrdinalIgnoreCase) != 0)
                return false;

            at += part.Length;
        }

        // "Iffy" must not match "If".
        if (at < _text.Length && (char.IsLetterOrDigit(_text[at]) || _text[at] == '_'))
            return false;

        return true;
    }

    private void SkipHorizontalWhitespace()
    {
        while (!AtEnd && (Current == ' ' || Current == '\t')) Advance();
    }

    private void Advance(int count = 1)
    {
        for (var i = 0; i < count && !AtEnd; i++)
        {
            if (_text[_index] == '\n') _line++;
            _index++;
        }
    }
}
