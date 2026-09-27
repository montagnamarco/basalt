using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// Writes the closing line of a Visual Basic block when the opening line is
/// finished, the way Visual Basic on Windows does: press Enter after
/// "If x Then" and "End If" appears below.
/// </summary>
public static class VisualBasicBlockCompleter
{
    /// <summary>
    /// The closing text for the block opened on the given line, or null when
    /// the line opens no block or the block is already closed.
    /// </summary>
    public static async Task<string?> GetClosingFor(
        string text, int lineIndex, CancellationToken ct = default)
    {
        var source = SourceText.From(text);
        if (lineIndex < 0 || lineIndex >= source.Lines.Count) return null;

        var line = source.Lines[lineIndex];
        var content = line.ToString().Trim();
        if (content.Length == 0) return null;

        // Enter has not been inserted yet. At EOF Roslyn still treats a bare
        // lambda header as an incomplete single-line expression; the newline
        // lets the parser decide whether it opens a multiline block.
        var parseText = line.End == text.Length ? text + "\n" : text;
        var tree = VisualBasicSyntaxTree.ParseText(parseText, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        // Directives live in structured trivia, not in the statement tree.
        var region = root.DescendantTrivia(line.Span, descendIntoTrivia: true)
            .Select(trivia => trivia.GetStructure())
            .OfType<RegionDirectiveTriviaSyntax>()
            .FirstOrDefault();
        if (region is not null)
            return HasMatchingRegionEnd(root, region) ? null : "#End Region";

        // A lambda header belongs to an expression inside a declaration or
        // argument. Starting at the line's first statement misses that block.
        var lambda = root.DescendantNodes(line.Span)
            .OfType<MultiLineLambdaExpressionSyntax>()
            .LastOrDefault(candidate =>
                candidate.SubOrFunctionHeader.Span.End > line.Start &&
                candidate.SubOrFunctionHeader.Span.End <= line.End);
        if (lambda is not null)
            return HasRealClosingToken(lambda) ? null : ClosingFor(lambda);

        // The statement that begins on this line: its parent block, if any, is
        // what needs closing.
        var token = root.FindToken(line.Span.Start);
        var statement = token.Parent?.AncestorsAndSelf()
            .FirstOrDefault(n => n is StatementSyntax && n.SpanStart >= line.Span.Start);

        var block = statement?.Parent;
        if (block is null) return null;

        // The block must actually begin on this line. Without this check a
        // plain statement reports the enclosing block, so typing "Dim x = 1"
        // inside a Sub would offer to write "End Sub".
        var blockLine = source.Lines.GetLineFromPosition(block.SpanStart).LineNumber;
        if (blockLine != lineIndex) return null;

        var closing = ClosingFor(block);
        if (closing is null) return null;

        // Roslyn inserts a zero-width End token when the block is unterminated;
        // a real one means the user already closed it.
        return HasRealClosingToken(block) ? null : closing;
    }

    /// <summary>Canonical closing line for each kind of block.</summary>
    private static string? ClosingFor(SyntaxNode block) => block.Kind() switch
    {
        SyntaxKind.MultiLineIfBlock => "End If",
        SyntaxKind.WhileBlock => "End While",
        SyntaxKind.ForBlock or SyntaxKind.ForEachBlock => "Next",
        SyntaxKind.SubBlock or SyntaxKind.MultiLineSubLambdaExpression => "End Sub",
        SyntaxKind.FunctionBlock or SyntaxKind.MultiLineFunctionLambdaExpression => "End Function",
        SyntaxKind.TryBlock => "End Try",
        SyntaxKind.SelectBlock => "End Select",
        SyntaxKind.SimpleDoLoopBlock or SyntaxKind.DoWhileLoopBlock
            or SyntaxKind.DoUntilLoopBlock => "Loop",
        SyntaxKind.WithBlock => "End With",
        SyntaxKind.UsingBlock => "End Using",
        SyntaxKind.SyncLockBlock => "End SyncLock",
        SyntaxKind.ClassBlock => "End Class",
        SyntaxKind.ModuleBlock => "End Module",
        SyntaxKind.StructureBlock => "End Structure",
        SyntaxKind.InterfaceBlock => "End Interface",
        SyntaxKind.EnumBlock => "End Enum",
        SyntaxKind.NamespaceBlock => "End Namespace",
        SyntaxKind.PropertyBlock => "End Property",
        SyntaxKind.GetAccessorBlock => "End Get",
        SyntaxKind.SetAccessorBlock => "End Set",
        _ => null
    };

    private static bool HasMatchingRegionEnd(SyntaxNode root, RegionDirectiveTriviaSyntax region)
    {
        var depth = 0;

        // Count parsed directives only: text in strings and comments must not
        // supply a closing directive for the editor.
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
        {
            if (trivia.SpanStart < region.SpanStart) continue;

            if (trivia.IsKind(SyntaxKind.RegionDirectiveTrivia))
            {
                depth++;
            }
            else if (trivia.IsKind(SyntaxKind.EndRegionDirectiveTrivia))
            {
                depth--;
                if (depth == 0) return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether the block already carries its closing statement.
    ///
    /// An unterminated block still parses: Roslyn supplies a missing token of
    /// zero width, so the presence of the node proves nothing and its width
    /// must be checked.
    /// </summary>
    private static bool HasRealClosingToken(SyntaxNode block)
    {
        var last = block.ChildNodes().LastOrDefault();

        return last switch
        {
            EndBlockStatementSyntax end => !end.EndKeyword.IsMissing,
            NextStatementSyntax next => !next.NextKeyword.IsMissing,
            LoopStatementSyntax loop => !loop.LoopKeyword.IsMissing,
            _ => false
        };
    }
}
