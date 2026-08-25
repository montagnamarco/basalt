using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Tidying the Imports at the top of a file.
///
/// Written here rather than taken from Roslyn: without an analyser configured
/// it reports no diagnostic for an unused import, so there is no code fix to
/// offer. Verified by asking for the quick actions on one.
/// </summary>
public sealed class ImportsRefactoring
{
    private const string Title = "Sort and Remove Imports";

    private readonly Solution _solution;

    public ImportsRefactoring(Solution solution) => _solution = solution;

    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, CancellationToken ct = default)
    {
        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(Title, "That file is not part of the solution.");

        if (document.Project.Language != LanguageNames.VisualBasic)
        {
            return RefactoringPreview.Refused(
                Title, "Tidying imports is only supported in Visual Basic so far.");
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var text = await document.GetTextAsync(ct).ConfigureAwait(false);

        if (root is not CompilationUnitSyntax unit || model is null)
            return RefactoringPreview.Refused(Title, "The file could not be read.");

        var imports = unit.Imports.ToList();

        if (imports.Count == 0)
            return RefactoringPreview.Refused(Title, "This file has no imports.");

        var used = UsedNamespaces(root, model, ct);

        var kept = imports
            .Where(i => IsUsed(i, used))
            .OrderBy(NameOf, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var removed = imports.Count - kept.Count;
        var reordered = !kept.Select(NameOf).SequenceEqual(
            imports.Where(i => IsUsed(i, used)).Select(NameOf), StringComparer.Ordinal);

        if (removed == 0 && !reordered)
            return RefactoringPreview.Refused(Title, "The imports are already tidy.");

        var lineEnding = text.ToString().Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        var replacement = string.Join(lineEnding, kept.Select(k => k.ToString().TrimEnd()));

        // From the first import to the end of the last, so what sits between
        // them goes too, and what surrounds them is untouched.
        var block = TextSpan.FromBounds(imports[0].SpanStart, imports[^1].Span.End);

        var updated = text.Replace(block, replacement).ToString();

        var summary = removed > 0
            ? $"Remove {removed} unused import{(removed == 1 ? "" : "s")}"
              + (reordered ? " and sort the rest" : "")
            : "Sort the imports";

        return new RefactoringPreview(
            summary, [new FileChangePreview(filePath, text.ToString(), updated)]);
    }

    /// <summary>
    /// Every namespace something in the file actually refers to.
    ///
    /// Taken from the symbols the names bind to rather than from the text: a
    /// namespace can be used without ever being written out.
    /// </summary>
    private static HashSet<string> UsedNamespaces(
        SyntaxNode root, SemanticModel model, CancellationToken ct)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in root.DescendantNodes())
        {
            ct.ThrowIfCancellationRequested();

            // Inside the imports themselves nothing counts as a use.
            if (node.FirstAncestorOrSelf<ImportsStatementSyntax>() is not null) continue;

            if (node is not SimpleNameSyntax and not MemberAccessExpressionSyntax) continue;

            var symbol = model.GetSymbolInfo(node, ct).Symbol
                      ?? model.GetSymbolInfo(node, ct).CandidateSymbols.FirstOrDefault();

            if (symbol is null) continue;

            for (var ns = symbol.ContainingNamespace; ns is { IsGlobalNamespace: false };
                 ns = ns.ContainingNamespace)
            {
                used.Add(ns.ToDisplayString());
            }
        }

        return used;
    }

    /// <summary>
    /// Whether an import is one of the used namespaces.
    ///
    /// An alias or an XML namespace is always kept: what it stands for is not
    /// a namespace this can check, and dropping one would change the code.
    /// </summary>
    private static bool IsUsed(ImportsStatementSyntax import, HashSet<string> used)
    {
        foreach (var clause in import.ImportsClauses)
        {
            if (clause is not SimpleImportsClauseSyntax { Alias: null } simple) return true;

            if (used.Contains(simple.Name.ToString())) return true;
        }

        return false;
    }

    /// <summary>An import as its name, for sorting.</summary>
    private static string NameOf(ImportsStatementSyntax import) =>
        import.ImportsClauses.FirstOrDefault()?.ToString() ?? import.ToString();
}
