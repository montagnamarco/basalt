using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Putting a variable's value back where it was used.
///
/// The inverse of extracting one, and the harder direction: replacing a name
/// with an expression is only safe when the value cannot have changed between
/// the declaration and the uses, so a variable that is assigned more than
/// once is refused rather than inlined wrongly.
/// </summary>
public sealed class InlineVariableRefactoring
{
    private const string Title = "Inline Variable";

    private readonly Solution _solution;

    public InlineVariableRefactoring(Solution solution) => _solution = solution;

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int position, CancellationToken ct = default)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(Title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                Title, "Inlining a variable is only supported in Visual Basic so far.");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is null || model is null)
            return RefactoringPreview.Refused(Title, "The file could not be read.");

        var token = root.FindToken(Math.Clamp(position, 0, text.Length - 1));

        if (ResolveLocal(model, token, ct) is not { } local)
        {
            return RefactoringPreview.Refused(
                Title, "Put the caret on a local variable to inline it.");
        }

        if (Declaration(root, local) is not (var declarator, var statement, var value)
            || value is null)
        {
            return RefactoringPreview.Refused(
                Title, $"'{local.Name}' has no value to put in its place.");
        }

        // A Dim that declares several names at once would leave the others
        // behind if this one were removed, so it is left alone.
        if (declarator.Names.Count > 1)
        {
            return RefactoringPreview.Refused(
                Title, "This declaration names more than one variable.");
        }

        var references = await FindReferencesAsync(local, document, ct).ConfigureAwait(false);

        var assignments = references
            .Where(r => IsAssignedTo(root, r))
            .ToList();

        if (assignments.Count > 0)
        {
            return RefactoringPreview.Refused(
                Title,
                $"'{local.Name}' is given a new value after it is declared, "
              + "so its uses do not all mean the same thing.");
        }

        var uses = references.Where(r => !statement.Span.Contains(r)).ToList();

        if (uses.Count == 0)
        {
            return RefactoringPreview.Refused(
                Title, $"'{local.Name}' is never used, so there is nothing to inline.");
        }

        var updated = Rewrite(text, statement, value, uses);

        return new RefactoringPreview(
            $"Inline '{local.Name}' into {uses.Count} use{(uses.Count == 1 ? "" : "s")}",
            [new FileChangePreview(filePath, text.ToString(), updated)]);
    }

    /// <summary>The local a token names, whether it is the declaration or a use.</summary>
    private static ILocalSymbol? ResolveLocal(
        SemanticModel model, SyntaxToken token, CancellationToken ct)
    {
        for (var node = token.Parent; node is not null; node = node.Parent)
        {
            if (model.GetDeclaredSymbol(node, ct) is ILocalSymbol declared) return declared;

            if (node is IdentifierNameSyntax
                && model.GetSymbolInfo(node, ct).Symbol is ILocalSymbol used)
            {
                return used;
            }

            if (node is StatementSyntax) break;
        }

        return null;
    }

    /// <summary>Where a local is declared, and what it is set to.</summary>
    private static (VariableDeclaratorSyntax Declarator,
                    StatementSyntax Statement,
                    ExpressionSyntax? Value)? Declaration(SyntaxNode root, ILocalSymbol local)
    {
        foreach (var declarator in root.DescendantNodes().OfType<VariableDeclaratorSyntax>())
        {
            var named = declarator.Names.Any(n =>
                string.Equals(n.Identifier.ValueText, local.Name, StringComparison.OrdinalIgnoreCase));

            if (!named) continue;

            if (declarator.FirstAncestorOrSelf<StatementSyntax>() is not { } statement) continue;

            return (declarator, statement, declarator.Initializer?.Value);
        }

        return null;
    }

    /// <summary>Every place a local is named, by span.</summary>
    private static async Task<List<TextSpan>> FindReferencesAsync(
        ILocalSymbol local, Document document, CancellationToken ct)
    {
        var found = await SymbolFinder
            .FindReferencesAsync(local, document.Project.Solution, ct)
            .ConfigureAwait(false);

        return
        [
            .. found
                .SelectMany(r => r.Locations)
                .Where(l => !l.IsImplicit)
                .Select(l => l.Location.SourceSpan)
        ];
    }

    /// <summary>
    /// Whether a use is the left side of an assignment.
    ///
    /// A variable written after it is declared cannot be inlined: its uses
    /// would no longer all mean the same expression.
    /// </summary>
    private static bool IsAssignedTo(SyntaxNode root, TextSpan span)
    {
        var node = root.FindNode(span);

        return node.FirstAncestorOrSelf<AssignmentStatementSyntax>() is { } assignment
            && assignment.Left.Span.Contains(span);
    }

    /// <summary>
    /// Writes the value into each use and takes the declaration out.
    ///
    /// Later spans first, so replacing one does not move the next.
    /// </summary>
    private static string Rewrite(
        SourceText text,
        StatementSyntax declaration,
        ExpressionSyntax value,
        List<TextSpan> uses)
    {
        // Bracketed where it needs to be: inlining "a + b" into "x * 2" must
        // not quietly become "a + b * 2".
        var written = NeedsBrackets(value) ? $"({value})" : value.ToString();

        var updated = text;

        foreach (var use in uses.OrderByDescending(u => u.Start))
            updated = updated.Replace(use, written);

        // The whole line goes, including the line ending it sat on.
        var line = updated.Lines.GetLineFromPosition(declaration.SpanStart);

        return updated
            .Replace(TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak), "")
            .ToString();
    }

    /// <summary>
    /// Whether an expression needs bracketing where it lands.
    ///
    /// Anything with an operator in it does: precedence at the destination is
    /// not the precedence it was written under.
    /// </summary>
    private static bool NeedsBrackets(ExpressionSyntax value) =>
        value is BinaryExpressionSyntax or UnaryExpressionSyntax;
}
