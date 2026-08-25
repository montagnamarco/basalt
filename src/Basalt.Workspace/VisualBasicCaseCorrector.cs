using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace Basalt.Workspace;

/// <summary>
/// Restores canonical casing in Visual Basic source, the way Visual Basic
/// editors have always done: type "if" and it becomes "If", "then" becomes
/// "Then", "console.readline" becomes "Console.ReadLine".
///
/// Two passes with different requirements:
///
/// Keywords come from the syntax tree alone. Visual Basic is case-insensitive,
/// so the parser already classifies "if" as an IfKeyword regardless of how it
/// was typed, and the canonical spelling comes from SyntaxFacts. This works on
/// a file in isolation, even one that does not compile.
///
/// Identifiers need the semantic model, because only the resolved symbol knows
/// how its own name is spelled. That pass is skipped when no compilation is
/// available, which is why the two are separate.
///
/// Keywords appearing inside strings and comments are never touched: they are
/// not tokens, so they never enter either pass.
/// </summary>
public static class VisualBasicCaseCorrector
{
    /// <summary>
    /// Corrects keyword casing across the whole text.
    /// </summary>
    public static async Task<IReadOnlyList<TextChange>> GetKeywordChangesAsync(
        string text, TextSpan? limitTo = null, CancellationToken ct = default)
    {
        var tree = VisualBasicSyntaxTree.ParseText(text, cancellationToken: ct);
        var root = await tree.GetRootAsync(ct).ConfigureAwait(false);

        var changes = new List<TextChange>();

        foreach (var token in root.DescendantTokens())
        {
            if (limitTo is { } span && !span.IntersectsWith(token.Span)) continue;
            if (!SyntaxFacts.IsKeywordKind(token.Kind())) continue;

            var canonical = SyntaxFacts.GetText(token.Kind());

            // Contextual keywords have no fixed spelling in SyntaxFacts.
            if (string.IsNullOrEmpty(canonical)) continue;
            if (string.Equals(token.Text, canonical, StringComparison.Ordinal)) continue;

            // Only casing may differ; anything else is a different token.
            if (!string.Equals(token.Text, canonical, StringComparison.OrdinalIgnoreCase)) continue;

            changes.Add(new TextChange(token.Span, canonical));
        }

        return changes;
    }

    /// <summary>
    /// Corrects identifier casing to match the declared symbols, which requires
    /// a compilation that can resolve them.
    /// </summary>
    public static async Task<IReadOnlyList<TextChange>> GetIdentifierChangesAsync(
        Document document, TextSpan? limitTo = null, CancellationToken ct = default)
    {
        var model = await document.GetSemanticModelAsync(ct).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (model is null || root is null) return [];

        var changes = new List<TextChange>();

        foreach (var name in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (limitTo is { } span && !span.IntersectsWith(name.Span)) continue;

            var written = name.Identifier.Text;
            var symbol = model.GetSymbolInfo(name, ct).Symbol;
            if (symbol is null) continue;

            // Rename only when the spelling differs by case: a different name
            // would mean the symbol is not the one written here.
            if (string.Equals(symbol.Name, written, StringComparison.Ordinal)) continue;
            if (!string.Equals(symbol.Name, written, StringComparison.OrdinalIgnoreCase)) continue;

            changes.Add(new TextChange(name.Identifier.Span, symbol.Name));
        }

        return changes;
    }

    /// <summary>Applies keyword casing to a whole document of text.</summary>
    public static async Task<string> CorrectKeywordsAsync(string text, CancellationToken ct = default)
    {
        var changes = await GetKeywordChangesAsync(text, limitTo: null, ct).ConfigureAwait(false);
        return changes.Count == 0 ? text : SourceText.From(text).WithChanges(changes).ToString();
    }
}
