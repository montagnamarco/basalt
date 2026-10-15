using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// The columns of a multi-line lambda, as Visual Studio lays them out: the
/// body one indent deeper than the line that holds the lambda header, and the
/// closing "End Sub" or "End Function" at that line's own indentation.
/// </summary>
/// <remarks>
/// Roslyn's formatter indents a lambda body from the column of the expression
/// that starts it: in <c>        Dim f = Sub()</c> it puts the body at column 20,
/// under the Sub keyword. Visual Basic users expect 12, one level in from the
/// statement, so the formatter's layout is rebased onto the header line. The
/// typing path and Format Document use the same rule, so a line does not move
/// when the caret leaves it and again when the document is formatted.
/// </remarks>
internal static class VisualBasicLambdaBodyIndentation
{
    /// <summary>Bounds the passes of <see cref="Apply"/>: one lambda is fixed per pass.</summary>
    private const int MaxPasses = 10_000;

    /// <summary>
    /// One level in from the line the lambda starts on, or null when the
    /// statement at <paramref name="statementStart"/> is not a direct statement
    /// of a lambda body (a block nested inside the lambda, or outside it).
    /// </summary>
    public static int? ColumnFor(SyntaxNode root, SourceText text, int statementStart, int indentationSize)
    {
        var token = root.FindToken(statementStart);
        var statement = token.Parent?.FirstAncestorOrSelf<StatementSyntax>();
        if (statement?.Parent is not MultiLineLambdaExpressionSyntax lambda) return null;

        return LeadingLength(text.Lines.GetLineFromPosition(lambda.SpanStart), text) + indentationSize;
    }

    /// <summary>
    /// Aligns every multi-line lambda in a whole document. Lambdas are visited
    /// outermost first; moving an outer body moves the lambdas nested in it, so
    /// each pass fixes one lambda and parses again.
    /// </summary>
    public static string Apply(string text, int indentationSize)
    {
        var current = text;
        for (var pass = 0; pass < MaxPasses; pass++)
        {
            var source = SourceText.From(current);
            var root = VisualBasicSyntaxTree.ParseText(current).GetRoot();
            var changes = FirstMisalignment(source, root, indentationSize);
            if (changes.Count == 0) return current;

            current = source.WithChanges(changes).ToString();
        }
        return current;
    }

    /// <summary>
    /// Rebases the indentation of one line of a text the formatter has laid out.
    /// The closing line sits under the header, always. A body line that the
    /// formatter re-indented keeps its offset from the lambda's keyword, measured
    /// from the header line's indentation. A body line the formatter left alone
    /// (a continuation, for one) is not moved, so leaving it again changes nothing.
    /// </summary>
    /// <param name="formatterMovedIndentation">
    /// Whether the formatter changed the leading whitespace of this line.
    /// </param>
    /// <returns>The text and the caret, moved with the indentation the line gained or lost.</returns>
    public static (string Text, int Caret) AlignLine(string text, int caret, int lineIndex, bool formatterMovedIndentation)
    {
        var source = SourceText.From(text);
        if (lineIndex < 0 || lineIndex >= source.Lines.Count) return (text, caret);

        var line = source.Lines[lineIndex];
        if (IsBlank(line, source)) return (text, caret);

        var root = VisualBasicSyntaxTree.ParseText(text).GetRoot();
        if (IsInsideToken(root, line)) return (text, caret);

        var lambda = InnermostLambdaAround(source, root, lineIndex);
        if (lambda is null) return (text, caret);

        var headerLine = source.Lines.GetLineFromPosition(lambda.SpanStart);
        var headerLength = LeadingLength(headerLine, source);
        var keywordColumn = lambda.SpanStart - headerLine.Start;
        var endIndex = source.Lines.GetLineFromPosition(lambda.EndSubOrFunctionStatement.SpanStart).LineNumber;

        var current = LeadingLength(line, source);
        var desired = lineIndex == endIndex ? headerLength
            : formatterMovedIndentation ? Math.Max(0, headerLength + current - keywordColumn)
            : current;
        if (desired == current) return (text, caret);

        var aligned = source.WithChanges(Reindent(line, source, desired)).ToString();

        // The caret keeps its place in the text. Inside the indentation it stays
        // in the indentation, at the same offset or the new end of it.
        var moved = caret < line.Start ? caret
            : caret >= line.Start + current ? caret + (desired - current)
            : line.Start + Math.Min(caret - line.Start, desired);
        return (aligned, Math.Clamp(moved, 0, aligned.Length));
    }

    /// <summary>Whether a formatter change replaces the leading whitespace of the line.</summary>
    public static bool MovesIndentation(TextChange change, TextLine line, SourceText source) =>
        change.Span.Start >= line.Start && change.Span.End <= line.Start + LeadingLength(line, source);

    private static IReadOnlyList<TextChange> FirstMisalignment(SourceText source, SyntaxNode root, int indentationSize)
    {
        foreach (var lambda in root.DescendantNodes().OfType<MultiLineLambdaExpressionSyntax>())
        {
            var end = lambda.EndSubOrFunctionStatement;

            // Without its own closing line the lambda's extent is a guess: leave it.
            if (end.EndKeyword.IsMissing) continue;

            var headerIndex = source.Lines.GetLineFromPosition(lambda.SpanStart).LineNumber;
            var endIndex = source.Lines.GetLineFromPosition(end.SpanStart).LineNumber;
            if (endIndex <= headerIndex) continue;

            var headerLength = LeadingLength(source.Lines[headerIndex], source);
            var changes = new List<TextChange>();

            // The body moves as one block, by the amount its first statement is off.
            var bodyLength = headerLength + indentationSize;
            var first = FirstStatementLine(source, root, headerIndex, endIndex);
            if (first is { } firstLine)
            {
                var shift = bodyLength - LeadingLength(firstLine, source);
                if (shift != 0)
                {
                    for (var index = headerIndex + 1; index < endIndex; index++)
                    {
                        var line = source.Lines[index];

                        // A line that starts inside a token (an XML literal's text) is
                        // data, not indentation: the formatter never moves it either.
                        if (IsBlank(line, source) || IsInsideToken(root, line)) continue;

                        var length = Math.Max(0, LeadingLength(line, source) + shift);
                        changes.Add(Reindent(line, source, length));
                    }
                }
            }

            // The closing line sits under the header's own indentation.
            var endLine = source.Lines[endIndex];
            if (!IsInsideToken(root, endLine) && LeadingLength(endLine, source) != headerLength)
                changes.Add(Reindent(endLine, source, headerLength));

            if (changes.Count > 0) return changes;
        }

        return [];
    }

    private static MultiLineLambdaExpressionSyntax? InnermostLambdaAround(SourceText source, SyntaxNode root, int lineIndex)
    {
        MultiLineLambdaExpressionSyntax? innermost = null;
        var innermostHeader = -1;
        foreach (var lambda in root.DescendantNodes().OfType<MultiLineLambdaExpressionSyntax>())
        {
            if (lambda.EndSubOrFunctionStatement.EndKeyword.IsMissing) continue;

            var headerIndex = source.Lines.GetLineFromPosition(lambda.SpanStart).LineNumber;
            var endIndex = source.Lines.GetLineFromPosition(lambda.EndSubOrFunctionStatement.SpanStart).LineNumber;
            if (lineIndex <= headerIndex || lineIndex > endIndex || headerIndex <= innermostHeader) continue;

            innermost = lambda;
            innermostHeader = headerIndex;
        }
        return innermost;
    }

    private static TextLine? FirstStatementLine(SourceText source, SyntaxNode root, int headerIndex, int endIndex)
    {
        for (var index = headerIndex + 1; index < endIndex; index++)
        {
            var line = source.Lines[index];
            if (!IsBlank(line, source) && !IsInsideToken(root, line)) return line;
        }
        return null;
    }

    /// <summary>
    /// Whether the line starts inside a token rather than in the trivia before one.
    /// Roslyn's token spans exclude trivia, so the start of a line in a multi-line
    /// token is strictly inside that token's span.
    /// </summary>
    private static bool IsInsideToken(SyntaxNode root, TextLine line)
    {
        var token = root.FindToken(line.Start);
        return token.Span.Start < line.Start && line.Start < token.Span.End;
    }

    private static TextChange Reindent(TextLine line, SourceText source, int length) =>
        new(TextSpan.FromBounds(line.Start, line.Start + LeadingLength(line, source)), new string(' ', length));

    private static int LeadingLength(TextLine line, SourceText source)
    {
        var length = 0;
        while (line.Start + length < line.End && source[line.Start + length] is ' ' or '\t') length++;
        return length;
    }

    private static bool IsBlank(TextLine line, SourceText source) =>
        LeadingLength(line, source) == line.End - line.Start;
}
