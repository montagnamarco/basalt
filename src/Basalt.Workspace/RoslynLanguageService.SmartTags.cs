using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace;

public sealed partial class RoslynLanguageService
{
    public Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(
        string filePath, int position, CancellationToken ct = default) =>
        GetQuickActionsForDocumentAsync(GetDocument(filePath), position, ct);

    public Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(
        string filePath, string currentText, int position, CancellationToken ct = default) =>
        GetQuickActionsForDocumentAsync(GetDocument(filePath)?.WithText(SourceText.From(currentText)), position, ct);

    private async Task<IReadOnlyList<QuickAction>> GetQuickActionsForDocumentAsync(
        Document? document, int position, CancellationToken ct)
    {
        if (document is null) return [];

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var caret = Math.Clamp(position, 0, text.Length);

        var diagnostics = await GetDiagnosticsAtAsync(document, caret, ct).ConfigureAwait(false);

        var actions = new List<QuickAction>();

        foreach (var provider in _codeFixProviders.Value)
        {
            var fixable = diagnostics
                .Where(d => provider.FixableDiagnosticIds.Contains(d.Id))
                .ToList();

            if (fixable.Count == 0) continue;

            foreach (var diagnostic in fixable)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var context = new CodeFixContext(
                        document,
                        diagnostic,
                        (action, _) => actions.Add(new QuickAction(action.Title, action)
                        {
                            OriginalSolution = document.Project.Solution,
                            ApplicableSpan = diagnostic.Location.SourceSpan
                        }),
                        ct);

                    await provider.RegisterCodeFixesAsync(context).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A provider that fails should not deny the user the fixes
                    // the others are offering.
                }
            }
        }

        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is CompilationUnitSyntax unit && unit.Imports.Count > 0)
        {
            var preview = await new Refactoring.ImportsRefactoring(document.Project.Solution)
                .PreviewAsync(document.FilePath!, ct).ConfigureAwait(false);
            if (preview.CanApply && preview.Changes.Count > 0)
            {
                var replacement = preview.Changes[0].NewText;
                actions.Add(new QuickAction(Localizer.Get(StringKeys.EditorSmartTagTidyImports),
                    CodeAction.Create(Localizer.Get(StringKeys.EditorSmartTagTidyImports),
                        _ => Task.FromResult(document.WithText(SourceText.From(replacement))),
                        "Basalt.TidyImports"))
                {
                    OriginalSolution = document.Project.Solution,
                    ApplicableSpan = unit.Imports.FirstOrDefault(statement => statement.Span.Contains(caret))?.Span
                        ?? unit.Imports[0].Span
                });
            }
        }

        // Two providers can offer the same fix for overlapping diagnostics.
        return [.. actions
            .GroupBy(a => a.Title, StringComparer.Ordinal)
            .Select(group => group.First())];
    }

}
