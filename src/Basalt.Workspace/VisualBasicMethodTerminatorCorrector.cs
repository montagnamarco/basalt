using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>Synchronizes a method's closing keyword after editing its declaration.</summary>
internal static class VisualBasicMethodTerminatorCorrector
{
    public static IReadOnlyList<TextChange> GetChanges(
        SourceText source, TextLine line, int position, CancellationToken ct)
    {
        var root = VisualBasicSyntaxTree.ParseText(source, cancellationToken: ct).GetRoot(ct);
        var anchor = Math.Clamp(position, line.Start, Math.Max(line.Start, line.End - 1));
        var declaration = root.FindToken(anchor).Parent?.FirstAncestorOrSelf<MethodStatementSyntax>();
        if (declaration?.Parent is not MethodBlockSyntax block
            || declaration.DescendantTokens().Any(token => token.IsMissing)
            || source.Lines.GetLineFromPosition(declaration.Span.Start).LineNumber != line.LineNumber)
            return [];

        var opening = declaration.SubOrFunctionKeyword;
        var ending = block.EndSubOrFunctionStatement;
        // A mismatched terminator is recovered as a direct body statement,
        // followed by a missing expected terminator. Use that parser-owned
        // statement only when it is the final one in this method's body.
        if (ending.BlockKeyword.IsMissing && block.Statements.LastOrDefault() is EndBlockStatementSyntax recovered)
            ending = recovered;
        var closing = ending.BlockKeyword;
        if (closing.IsMissing
            || (!closing.IsKind(SyntaxKind.SubKeyword) && !closing.IsKind(SyntaxKind.FunctionKeyword))
            || opening.IsKind(closing.Kind())
            || source.Lines.GetLineFromPosition(closing.Span.Start).LineNumber <= line.LineNumber)
            return [];

        var change = new TextChange(closing.Span, opening.IsKind(SyntaxKind.SubKeyword) ? "Sub" : "Function");

        // Roslyn owns both the pairing and error recovery. A repaired candidate
        // must be a complete method with the same boundary; otherwise an
        // unfinished lambda or nested block could have consumed this terminator.
        var candidate = source.WithChanges(change);
        var candidateRoot = VisualBasicSyntaxTree.ParseText(candidate, cancellationToken: ct).GetRoot(ct);
        var repaired = candidateRoot.FindToken(opening.Span.Start).Parent?
            .FirstAncestorOrSelf<MethodBlockSyntax>();
        if (repaired is null
            || repaired.ContainsDiagnostics
            || repaired.SubOrFunctionStatement.Span.Start != declaration.Span.Start
            || repaired.EndSubOrFunctionStatement.BlockKeyword.Span.Start != closing.Span.Start)
            return [];

        return [change];
    }
}
