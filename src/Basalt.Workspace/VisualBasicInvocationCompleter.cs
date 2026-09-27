using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Adds omitted parentheses to a finished, resolved argumentless call statement.</summary>
internal static class VisualBasicInvocationCompleter
{
    public static async Task<IReadOnlyList<TextChange>> GetChangesAsync(
        Document document, TextLine line, int position, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return [];
        var statement = root.FindToken(line.Start).Parent?.FirstAncestorOrSelf<StatementSyntax>();
        var expression = statement switch
        {
            ExpressionStatementSyntax call => call.Expression,
            CallStatementSyntax call => call.Invocation,
            _ => null
        };
        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.ArgumentList is not null) return [];
            expression = invocation.Expression;
        }
        if (statement is null || expression is null
            || expression is not (IdentifierNameSyntax or GenericNameSyntax or MemberAccessExpressionSyntax)
            || statement.Span.Start < line.Start || statement.Span.End > line.End
            || position < expression.Span.End
            || statement.ContainsDiagnostics
            || statement.DescendantTokens().Any(token => token.IsMissing))
            return [];

        // A complete physical line can end with a comment. Continuations,
        // another statement or skipped tokens must not acquire parentheses.
        var tail = TextSpan.FromBounds(expression.Span.End, line.End);
        if (root.DescendantTokens(tail, descendIntoTrivia: true)
                .Any(token => !token.IsMissing && token.Span.Length > 0
                    && token.Span.Start >= tail.Start && token.Span.Start < tail.End)
            || root.DescendantTrivia(tail, descendIntoTrivia: true)
                .Where(trivia => trivia.Span.Start >= tail.Start && trivia.Span.End <= tail.End)
                .Any(trivia => !trivia.IsKind(SyntaxKind.WhitespaceTrivia) && !trivia.IsKind(SyntaxKind.CommentTrivia)))
            return [];

        // Statement syntax alone cannot distinguish a method from a property
        // or resolve overloads. Let Roslyn bind the exact expression first.
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        if (model?.GetSymbolInfo(expression, ct).Symbol is not IMethodSymbol { Parameters.Length: 0 })
            return [];

        return [new TextChange(new TextSpan(expression.Span.End, 0), "()")];
    }
}
