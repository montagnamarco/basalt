using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Rename;

namespace Basalt.Workspace.Refactoring;

/// <summary>
/// Renaming a symbol everywhere it is used.
///
/// Roslyn's own renamer does the work: it knows which "Count" is the one being
/// renamed and which belongs to something else, which a search and replace
/// cannot.
/// </summary>
public sealed class RenameRefactoring
{
    private readonly Solution _solution;

    public RenameRefactoring(Solution solution) => _solution = solution;

    /// <summary>
    /// Works out what renaming would change.
    ///
    /// The solution is not modified: what comes back is a description, and
    /// applying it is a separate step the user takes after looking.
    /// </summary>
    public async Task<RefactoringPreview> PreviewAsync(
        string filePath, int position, string newName, CancellationToken ct = default)
    {
        const string title = "Rename";

        if (!IsValidName(newName))
            return RefactoringPreview.Refused(title, $"'{newName}' is not a valid name.");

        var document = _solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal));

        if (document is null)
            return RefactoringPreview.Refused(title, "That file is not part of the solution.");

        var symbol = await SymbolFinder
            .FindSymbolAtPositionAsync(document, position, ct)
            .ConfigureAwait(false);

        if (symbol is null)
            return RefactoringPreview.Refused(title, "There is nothing to rename here.");

        if (symbol.IsImplicitlyDeclared)
            return RefactoringPreview.Refused(title, $"'{symbol.Name}' cannot be renamed.");

        // Renaming something from a referenced assembly would change nothing
        // here and mislead about what is happening.
        if (symbol.Locations.All(l => !l.IsInSource))
        {
            return RefactoringPreview.Refused(
                title, $"'{symbol.Name}' is declared outside this solution.");
        }

        Solution renamed;

        try
        {
            renamed = await Renamer
                .RenameSymbolAsync(_solution, symbol, new SymbolRenameOptions(), newName, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return RefactoringPreview.Refused(title, ex.Message);
        }

        var changes = new List<FileChangePreview>(
            await DescribeChangesAsync(renamed, ct).ConfigureAwait(false));

        // The Razor views too. Roslyn does not see them — a .vbhtml is not a
        // solution document — so renaming a model property would otherwise
        // leave every view naming the old one, and break only at build time.
        changes.AddRange(await new ViewRename(_solution)
            .PreviewAsync(symbol, newName, ViewRename.ViewsOf(_solution), ct)
            .ConfigureAwait(false));

        return changes.Count == 0
            ? RefactoringPreview.Refused(title, $"Renaming '{symbol.Name}' would change nothing.")
            : new RefactoringPreview($"Rename '{symbol.Name}' to '{newName}'", changes);
    }

    /// <summary>The files that differ between the original solution and a new one.</summary>
    private async Task<IReadOnlyList<FileChangePreview>> DescribeChangesAsync(
        Solution updated, CancellationToken ct)
    {
        var changes = new List<FileChangePreview>();

        foreach (var projectChange in updated.GetChanges(_solution).GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments())
            {
                ct.ThrowIfCancellationRequested();

                var before = _solution.GetDocument(documentId);
                var after = updated.GetDocument(documentId);

                if (before?.FilePath is not { Length: > 0 } path || after is null) continue;

                var originalText = await before.GetTextAsync(ct).ConfigureAwait(false);
                var newText = await after.GetTextAsync(ct).ConfigureAwait(false);

                if (originalText.ContentEquals(newText)) continue;

                changes.Add(new FileChangePreview(
                    path, originalText.ToString(), newText.ToString()));
            }
        }

        return changes;
    }

    /// <summary>
    /// Whether a name can be used.
    ///
    /// Checked here rather than left to the renamer, which accepts anything
    /// and produces code that will not compile.
    /// </summary>
    internal static bool IsValidName(string name)
    {
        if (name.Length == 0) return false;

        if (!char.IsLetter(name[0]) && name[0] != '_') return false;

        // A Visual Basic name may end in a type character, which is part of it.
        var bare = name.TrimEnd('$', '%', '&', '!', '#', '@');

        return bare.Length > 0 && bare.All(c => char.IsLetterOrDigit(c) || c == '_');
    }
}
