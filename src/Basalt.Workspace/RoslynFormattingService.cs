using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Basalt.Core.Model;
using Basalt.Core.Services;

namespace Basalt.Workspace;

/// <summary>
/// Formatting backed by Roslyn, the same engine Visual Studio uses.
///
/// Roslyn picks the Visual Basic rules from the project that owns the
/// document, so there is no per-language code here. Formatting runs against a
/// throwaway in-memory project rather than the open solution, which keeps it
/// usable for files that belong to no project.
/// </summary>
public sealed class RoslynFormattingService : IFormattingService, IDisposable
{
    private readonly AdhocWorkspace _workspace = new();

    public Task<FormattingResult> FormatAsync(
        string text, SourceLanguage language, CancellationToken ct = default) =>
        RunAsync(text, language, span: null, ct);

    public Task<FormattingResult> FormatRangeAsync(
        string text, SourceLanguage language, int start, int length, CancellationToken ct = default)
    {
        // A span reaching past the end of the text would throw inside Roslyn;
        // clamping keeps a stale caret position from breaking formatting.
        var from = Math.Clamp(start, 0, text.Length);
        var to = Math.Clamp(start + length, from, text.Length);

        return RunAsync(text, language, new TextSpan(from, to - from), ct);
    }

    private async Task<FormattingResult> RunAsync(
        string text, SourceLanguage language, TextSpan? span, CancellationToken ct)
    {
        if (language is not SourceLanguage.VisualBasic)
            return new FormattingResult(text, Changed: false);

        var document = CreateScratchDocument(text, language);

        var formatted = span is { } range
            ? await Formatter.FormatAsync(document, range, cancellationToken: ct).ConfigureAwait(false)
            : await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);

        var result = (await formatted.GetTextAsync(ct).ConfigureAwait(false)).ToString();

        return new FormattingResult(result, !string.Equals(result, text, StringComparison.Ordinal));
    }

    /// <summary>
    /// Builds a one-document project just to run the formatter.
    ///
    /// Each call uses a fresh project id: reusing one would accumulate
    /// documents in the workspace for the lifetime of the IDE.
    /// </summary>
    private Document CreateScratchDocument(string text, SourceLanguage language)
    {
        var extension = language == SourceLanguage.VisualBasic ? "vb" : "cs";

        var project = _workspace.AddProject(
            $"Formatting_{Guid.NewGuid():N}", language.ToRoslynName());

        return _workspace.AddDocument(project.Id, $"Document.{extension}", SourceText.From(text));
    }

    /// <summary>
    /// Characters that end a block and therefore change their own line's
    /// indentation the moment they are typed.
    ///
    /// VB closes blocks with keywords rather than punctuation, so there is no
    /// single character to react to; VB lines are re-indented when the caret
    /// leaves the line instead.
    /// </summary>
    /// <summary>
    /// Nothing triggers formatting on a single character any more.
    ///
    /// This existed for C#, where "}" and ";" close something and give the
    /// formatter a moment to act. Visual Basic has no such character: a line
    /// is re-indented when the caret leaves it, which is handled elsewhere.
    /// Kept rather than removed because callers ask, and the honest answer is
    /// "no" rather than a missing method.
    /// </summary>
    public bool TriggersFormatting(char character, SourceLanguage language) => false;

    public async Task<TypingFormattingResult> FormatLineAsync(
        string text, SourceLanguage language, int caret, CancellationToken ct = default)
    {
        if (language is not SourceLanguage.VisualBasic)
            return new TypingFormattingResult(text, caret, Changed: false);

        var source = SourceText.From(text);
        var position = Math.Clamp(caret, 0, text.Length);
        var line = source.Lines.GetLineFromPosition(position);

        // An empty line carries no token to anchor indentation to.
        if (line.Span.IsEmpty) return new TypingFormattingResult(text, caret, Changed: false);

        var document = CreateScratchDocument(text, language);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return new TypingFormattingResult(text, caret, Changed: false);

        var span = ContextSpan(source, root, line, position);

        var formatted = await Formatter.FormatAsync(document, span, cancellationToken: ct)
            .ConfigureAwait(false);

        // Only the changes landing on the caret's own line are applied: a wider
        // span lets the formatter see the enclosing block, but re-indenting the
        // lines above would undo formatting the author chose deliberately.
        var changes = (await formatted.GetTextChangesAsync(document, ct).ConfigureAwait(false))
            .Where(change => StaysOnLine(change, line))
            .ToList();

        if (changes.Count == 0) return new TypingFormattingResult(text, caret, Changed: false);

        var updated = source.WithChanges(changes);

        return new TypingFormattingResult(
            updated.ToString(),
            ShiftCaret(caret, changes),
            Changed: true);
    }

    /// <summary>
    /// Span the formatter needs in order to know how far to indent the line.
    ///
    /// It reaches back to the construct that opens the enclosing block: given
    /// only the line itself, the formatter has no opening brace or block
    /// keyword to measure against and leaves the indentation alone.
    /// </summary>
    private static TextSpan ContextSpan(
        SourceText source, SyntaxNode root, TextLine line, int position)
    {
        var anchor = Math.Clamp(position, 0, Math.Max(0, root.FullSpan.End - 1));
        var node = root.FindToken(anchor).Parent;

        // Climb until the node starts on an earlier line: that is the one that
        // establishes the indentation level for the current line.
        while (node?.Parent is not null && node.Span.Start >= line.Start) node = node.Parent;

        var start = Math.Min(node?.Span.Start ?? line.Start, line.Start);
        return TextSpan.FromBounds(start, Math.Min(line.End, source.Length));
    }

    /// <summary>Moves the caret by however much the text before it grew or shrank.</summary>
    private static int ShiftCaret(int caret, IReadOnlyList<TextChange> changes)
    {
        var delta = changes
            .Where(change => change.Span.End <= caret)
            .Sum(change => (change.NewText?.Length ?? 0) - change.Span.Length);

        return Math.Max(0, caret + delta);
    }

    // Explicit continuations can make Roslyn normalize LF to CRLF. Typing
    // corrections must preserve the document's delimiters as well as its lines.
    private static bool StaysOnLine(TextChange change, TextLine line) =>
        change.Span.Start >= line.Start && change.Span.End <= line.End
        && !(change.NewText?.Contains('\r') ?? false)
        && !(change.NewText?.Contains('\n') ?? false);

    /// <summary>
    /// Characters that finish a word.
    ///
    /// Visual Basic corrects casing the moment a word is complete, so the
    /// trigger is the separator typed after it rather than any one keyword
    /// character. C# is case-sensitive and has no such behaviour.
    /// </summary>
    public bool CompletesWord(char character, SourceLanguage language) =>
        language == SourceLanguage.VisualBasic &&
        (char.IsWhiteSpace(character) || character is '(' or ')' or ',' or '.' or '=' or ':');

    public Task<TypingFormattingResult> CompleteLineAsync(
        string text, SourceLanguage language, int caret, CancellationToken ct = default)
    {
        if (language != SourceLanguage.VisualBasic)
            return Task.FromResult(new TypingFormattingResult(text, caret, Changed: false));

        // Leaving a line calls this from the dispatcher. Even cached Roslyn
        // work can parse or bind synchronously before its first incomplete await.
        return Task.Run(async () =>
        {
            var source = SourceText.From(text);
            var position = Math.Clamp(caret, 0, text.Length);
            var line = source.Lines.GetLineFromPosition(position);
            IReadOnlyList<TextChange> changes = line.Span.IsEmpty
                ? []
                : VisualBasicThenCompleter.GetChanges(source, line, position, ct);
            var completed = changes.Count == 0 ? source : source.WithChanges(changes);
            var completedCaret = ShiftCaret(caret, changes);
            var completedLine = completed.Lines.GetLineFromPosition(Math.Clamp(completedCaret, 0, completed.Length));
            IReadOnlyList<TextChange> invocations = completedLine.Span.IsEmpty
                ? []
                : await VisualBasicInvocationCompleter.GetChangesAsync(
                    CreateScratchDocument(completed.ToString(), language), completedLine, completedCaret, ct)
                    .ConfigureAwait(false);
            if (invocations.Count > 0)
            {
                completed = completed.WithChanges(invocations);
                completedCaret = ShiftCaret(completedCaret, invocations);
            }
            var result = await ApplyTypingConventionsAsync(
                completed.ToString(), language, completedCaret, ct).ConfigureAwait(false);
            return result with { Changed = result.Changed || changes.Count > 0 || invocations.Count > 0 };
        }, ct);
    }

    public async Task<TypingFormattingResult> ApplyTypingConventionsAsync(
        string text, SourceLanguage language, int caret, CancellationToken ct = default)
    {
        if (language != SourceLanguage.VisualBasic)
            return new TypingFormattingResult(text, caret, Changed: false);

        var source = SourceText.From(text);
        var position = Math.Clamp(caret, 0, text.Length);
        var line = source.Lines.GetLineFromPosition(position);

        if (line.Span.IsEmpty) return new TypingFormattingResult(text, caret, Changed: false);

        // A half-typed string literal makes the line unparseable, and
        // reformatting it would mangle the text the user is still writing.
        if (HasUnterminatedString(source, line, position))
            return new TypingFormattingResult(text, caret, Changed: false);

        // A declaration edit can change its associated closing keyword. This
        // is the only typing correction allowed to touch another line.
        var terminators = VisualBasicMethodTerminatorCorrector.GetChanges(source, line, position, ct);
        var synchronized = terminators.Count == 0 ? source : source.WithChanges(terminators);

        // Casing first: it rewrites tokens in place and never moves anything,
        // so the spans the formatter works with afterwards stay valid.
        var casing = await VisualBasicCaseCorrector
            .GetKeywordChangesAsync(synchronized.ToString(), line.Span, ct)
            .ConfigureAwait(false);

        var cased = casing.Count == 0 ? synchronized : synchronized.WithChanges(casing);
        var conventions = casing.Concat(terminators).ToList();

        // Then spacing and indentation for the same line.
        var document = CreateScratchDocument(cased.ToString(), language);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null)
            return Result(text, cased.ToString(), caret, conventions);

        var span = ContextSpan(cased, root, cased.Lines.GetLineFromPosition(position), position);

        var formatted = await Formatter.FormatAsync(document, span, cancellationToken: ct)
            .ConfigureAwait(false);

        var currentLine = cased.Lines.GetLineFromPosition(position);
        var layout = (await formatted.GetTextChangesAsync(document, ct).ConfigureAwait(false))
            .Where(change => StaysOnLine(change, currentLine))
            // Trailing whitespace at the caret is where the user is about to
            // type the next word. The formatter sees it as redundant and strips
            // it, which would run that word into the previous one.
            .Where(change => !TouchesCaretWhitespace(change, cased, position))
            .ToList();

        if (layout.Count == 0) return Result(text, cased.ToString(), caret, conventions);

        var final = cased.WithChanges(layout);

        return new TypingFormattingResult(
            final.ToString(),
            ShiftCaret(caret, layout),
            Changed: true);
    }

    /// <summary>
    /// Whether the caret sits inside a string literal that has no closing quote
    /// yet, counting quotes from the start of the line.
    ///
    /// Visual Basic escapes a quote by doubling it, so a pair inside a literal
    /// leaves the parity unchanged and needs no special handling here.
    /// </summary>
    private static bool HasUnterminatedString(SourceText source, TextLine line, int caret)
    {
        var upToCaret = source.ToString(TextSpan.FromBounds(line.Start, caret));

        var quotes = upToCaret.Count(c => c == '"');
        return quotes % 2 != 0;
    }

    /// <summary>
    /// Whether a change would remove the whitespace the caret is sitting in.
    ///
    /// While typing, the space after a word is not redundant: it separates that
    /// word from the one being typed next.
    /// </summary>
    private static bool TouchesCaretWhitespace(TextChange change, SourceText text, int caret)
    {
        if (change.Span.End < caret) return false;
        if (change.Span.Start > caret) return false;

        // Only deletions and shrinking replacements can swallow the separator.
        var newLength = change.NewText?.Length ?? 0;
        if (newLength >= change.Span.Length) return false;

        var removed = text.ToString(change.Span);
        return removed.Length > 0 && removed.All(char.IsWhiteSpace);
    }

    private static TypingFormattingResult Result(
        string original, string updated, int caret, IReadOnlyList<TextChange> changes) =>
        new(updated, caret, Changed: changes.Count > 0
                                    && !string.Equals(original, updated, StringComparison.Ordinal));

    /// <summary>
    /// Works out the indentation of a line that is still empty.
    ///
    /// An empty line has no token for the formatter to anchor to, so a
    /// placeholder declaration is inserted, the text formatted, and the
    /// resulting column measured. A statement probe works for block bodies;
    /// a continuation needs an expression that can join the preceding statement.
    /// </summary>
    public async Task<int> GetIndentationAsync(
        string text, SourceLanguage language, int position, CancellationToken ct = default)
    {
        if (language is not SourceLanguage.VisualBasic) return 0;

        return await Task.Run(async () =>
        {
            var probe = $"zzIndentProbe_{Guid.NewGuid():N}";
            var at = Math.Clamp(position, 0, text.Length);

            // The probe must sit on a line of its own. Roslyn decides whether
            // this identifier continues the preceding statement or starts one.
            var needsNewLine = at > 0 && text[at - 1] != '\n';
            var prefix = needsNewLine ? "\n" : "";
            var candidate = text[..at] + prefix + probe + text[at..];
            var document = CreateScratchDocument(candidate, language);
            var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
            var probePosition = at + prefix.Length;
            var token = root?.FindToken(probePosition);
            var statement = token?.Parent?.FirstAncestorOrSelf<Microsoft.CodeAnalysis.VisualBasic.Syntax.StatementSyntax>();
            var continuation = token?.Span.Start == probePosition && statement?.Span.Start < probePosition;

            // A declaration cannot be used as an operand or argument: Roslyn
            // would recover it as invalid syntax and report column zero. Keep
            // the identifier when it belongs to a statement on an earlier line.
            var keywordLength = 0;
            if (!continuation)
            {
                const string keyword = "Dim ";
                keywordLength = keyword.Length;
                candidate = text[..at] + prefix + keyword + probe + text[at..];
                document = document.WithText(SourceText.From(candidate));
            }

            var formatted = await Formatter.FormatAsync(document, cancellationToken: ct).ConfigureAwait(false);
            var result = (await formatted.GetTextAsync(ct).ConfigureAwait(false)).ToString();
            var index = result.IndexOf(probe, StringComparison.Ordinal);
            if (index < 0) return 0;
            if (continuation)
            {
                // The formatter preserves continuation indentation instead
                // of choosing a smart-indent column. Use its statement layout
                // and Roslyn's parsed argument positions as the anchors.
                var formattedSource = SourceText.From(result);
                var formattedRoot = await formatted.GetSyntaxRootAsync(ct).ConfigureAwait(false);
                var probeToken = formattedRoot?.FindToken(index);
                var probeLine = formattedSource.Lines.GetLineFromPosition(index);
                var arguments = probeToken?.Parent?.Ancestors()
                    .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.ArgumentListSyntax>()
                    .FirstOrDefault(list => list.Span.Start < probeLine.Start);
                if (arguments?.Arguments.FirstOrDefault() is { } firstArgument
                    && firstArgument.Span.Start < probeLine.Start)
                {
                    var firstLine = formattedSource.Lines.GetLineFromPosition(firstArgument.Span.Start);
                    var openingLine = formattedSource.Lines.GetLineFromPosition(arguments.OpenParenToken.Span.Start);
                    if (firstLine.LineNumber == openingLine.LineNumber)
                        return firstArgument.Span.Start - firstLine.Start;
                }

                var anchor = probeToken?.Parent?.FirstAncestorOrSelf<Microsoft.CodeAnalysis.VisualBasic.Syntax.StatementSyntax>();
                if (anchor is not null)
                {
                    var anchorLine = formattedSource.Lines.GetLineFromPosition(anchor.Span.Start);
                    var indentationSize = _workspace.Options.GetOption(FormattingOptions.IndentationSize, language.ToRoslynName());
                    return anchor.Span.Start - anchorLine.Start + indentationSize;
                }
            }
            var lineStart = result.LastIndexOf('\n', Math.Max(0, index - 1)) + 1;
            return Math.Max(0, index - lineStart - keywordLength);
        }, ct).ConfigureAwait(false);
    }

    public void Dispose() => _workspace.Dispose();
}
