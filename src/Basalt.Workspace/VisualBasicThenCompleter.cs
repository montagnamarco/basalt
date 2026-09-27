using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Completes a finished conditional header using Roslyn's optional Then token.</summary>
internal static class VisualBasicThenCompleter
{
    public static IReadOnlyList<TextChange> GetChanges(SourceText source, TextLine line, int position, CancellationToken ct)
    {
        var root = VisualBasicSyntaxTree.ParseText(source, cancellationToken: ct).GetRoot(ct);
        var statement = root.FindToken(line.Start).Parent?.AncestorsAndSelf()
            .FirstOrDefault(node => node is IfStatementSyntax or ElseIfStatementSyntax);
        var condition = statement switch
        {
            IfStatementSyntax header => header.Condition,
            ElseIfStatementSyntax header => header.Condition,
            _ => null
        };
        var then = statement switch
        {
            IfStatementSyntax header => header.ThenKeyword,
            ElseIfStatementSyntax header => header.ThenKeyword,
            _ => default
        };
        if (statement is null || condition is null
            || statement.Parent is not (MultiLineIfBlockSyntax or ElseIfBlockSyntax)
            || (then.RawKind != 0 && !then.IsMissing)
            || statement.Span.Start < line.Start
            || condition.Span.Start < line.Start || condition.Span.End > line.End
            || position < condition.Span.End
            || condition.ContainsDiagnostics
            || condition.DescendantTokens().Any(token => token.IsMissing))
            return [];

        // The rest of the physical line must contain only whitespace or a
        // comment. Parsed continuation, colon and skipped-token trivia mean
        // this is not a complete standalone header yet.
        var tail = TextSpan.FromBounds(condition.Span.End, line.End);
        if (root.DescendantTokens(tail, descendIntoTrivia: true)
                .Any(token => !token.IsMissing && token.Span.Length > 0
                    && token.Span.Start >= tail.Start && token.Span.Start < tail.End)
            || root.DescendantTrivia(tail, descendIntoTrivia: true)
                .Where(trivia => trivia.Span.Start >= tail.Start && trivia.Span.End <= tail.End)
                .Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.CommentTrivia)))
            return [];

        return [new TextChange(new TextSpan(condition.Span.End, 0), " Then")];
    }
}
