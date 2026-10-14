using System.Globalization;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// What stops a rename, or comes with it, beyond the renamer's own edits.
/// </summary>
/// <remarks>
/// Roslyn's renamer applies the edit it was asked for and does not refuse a
/// new name that collides: renaming "Twice" to "Main" in a module that has a
/// "Main" produced two Main members, previewed without a word. Its conflict
/// resolution is internal, so a conflict is found here in two public ways:
/// the symbol table says whether the name is already a member of the
/// container, and the compiler says whether the renamed code now has errors
/// it did not have before.
/// </remarks>
internal static class RenameConflicts
{
    /// <summary>
    /// The member of the same container that already has the new name, or
    /// null when there is none.
    /// </summary>
    /// <remarks>
    /// Locals and parameters are left to the compiler: they only collide in
    /// the same block, and the second check reports that as an error. A
    /// member collides with any sibling of that name, overloads included, so
    /// the user is not left with two members that differ only in their
    /// signature when they meant one to be renamed.
    ///
    /// Visual Basic names are case-insensitive, so "TOTAL" collides with
    /// "Total". The symbol table's lookup by name is not reliably so for
    /// every container, so the siblings are compared here, by the language's rule.
    /// </remarks>
    public static ISymbol? SiblingNamed(ISymbol symbol, string newName)
    {
        if (symbol is ILocalSymbol or IParameterSymbol or IRangeVariableSymbol or ILabelSymbol) return null;

        var comparison = symbol.Language == LanguageNames.VisualBasic
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        IEnumerable<ISymbol> candidates = symbol is INamedTypeSymbol { ContainingType: null } type
            ? type.ContainingNamespace?.GetTypeMembers() ?? []
            : symbol.ContainingType?.GetMembers() ?? [];

        return candidates.FirstOrDefault(sibling =>
            string.Equals(sibling.Name, newName, comparison)
            && !sibling.IsImplicitlyDeclared
            && !SymbolEqualityComparer.Default.Equals(sibling, symbol));
    }

    /// <summary>
    /// The first error the renamed code has that the original did not, in the
    /// interface language, or null when the rename introduces none.
    /// </summary>
    /// <remarks>
    /// An error is the same error when its diagnostic ID and its message are the same. Each
    /// error of the original can account for one of the renamed code, so an error the rename
    /// fixed does not hide another it introduced under the same ID: two BC30451 errors
    /// naming different identifiers are two errors, and the second one is reported.
    /// The location is not compared: a rename moves every position after it, and
    /// the renamed code has no location of the original to map back to.
    /// An error that names the renamed symbol reads with the new name afterwards, so the new
    /// name is read back as the old one before comparing: 'Total' is not a member of
    /// 'Customer' stays the same error when Customer becomes Client.
    /// Only the projects the rename changes are compiled.
    /// </remarks>
    public static async Task<string?> IntroducedErrorAsync(
        Solution original, Solution renamed, string oldName, string newName, CancellationToken ct)
    {
        var culture = CultureInfo.GetCultureInfo(Localizer.InterfaceLanguage);

        foreach (var change in renamed.GetChanges(original).GetProjectChanges())
        {
            ct.ThrowIfCancellationRequested();

            var before = original.GetProject(change.ProjectId);
            var after = renamed.GetProject(change.ProjectId);
            if (before is null || after is null) continue;

            var beforeCompilation = await before.GetCompilationAsync(ct).ConfigureAwait(false);
            var afterCompilation = await after.GetCompilationAsync(ct).ConfigureAwait(false);
            if (beforeCompilation is null || afterCompilation is null) continue;

            var unaccounted = beforeCompilation.GetDiagnostics(ct)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .GroupBy(d => (d.Id, Message: d.GetMessage(culture)))
                .ToDictionary(group => group.Key, group => group.Count());

            foreach (var error in afterCompilation.GetDiagnostics(ct)
                         .Where(d => d.Severity == DiagnosticSeverity.Error))
            {
                ct.ThrowIfCancellationRequested();

                var key = (error.Id, Message: AsBefore(error.GetMessage(culture), oldName, newName));

                if (unaccounted.TryGetValue(key, out var remaining) && remaining > 0)
                {
                    unaccounted[key] = remaining - 1;
                    continue;
                }

                return error.GetMessage(culture);
            }
        }

        return null;
    }

    /// <summary>A message of the renamed code with the new name, as a whole word, read as the old one.</summary>
    private static string AsBefore(string message, string oldName, string newName) =>
        System.Text.RegularExpressions.Regex.Replace(
            message,
            @"(?<![\p{L}\p{N}_])" + System.Text.RegularExpressions.Regex.Escape(newName) + @"(?![\p{L}\p{N}_])",
            oldName.Replace("$", "$$"),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
