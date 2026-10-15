using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace;

public sealed partial class RoslynLanguageService
{
    public async Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(
        string filePath, int position, CancellationToken ct = default) =>
        await GetQuickActionsForDocumentAsync(await DocumentWhenLoadedAsync(filePath, ct).ConfigureAwait(false), position, ct)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<QuickAction>> GetQuickActionsAsync(
        string filePath, string currentText, int position, CancellationToken ct = default)
    {
        var document = await DocumentWhenLoadedAsync(filePath, ct).ConfigureAwait(false);
        return await GetQuickActionsForDocumentAsync(document?.WithText(SourceText.From(currentText)), position, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The document of a file, after the gate is free. OpenSolutionAsync holds the gate for the whole MSBuild load, and the
    /// workspace has no document for the file until the load is done: a question answered during the load was empty (measured:
    /// 300 ms into a load of about 650 ms), and the editor kept that answer for the same text. An idle gate costs one acquire
    /// and release. The gate is held only by loads and short document updates, never by a question.
    /// </summary>
    private async Task<Document?> DocumentWhenLoadedAsync(string filePath, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        _gate.Release();
        return GetDocument(filePath);
    }

    /// <summary>
    /// Test seam: holds the gate as a load does, until the returned action runs, so a question asked meanwhile must wait.
    /// Production code never calls it.
    /// </summary>
    internal async Task<Action> HoldGateForTestsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        return () => _gate.Release();
    }

    private async Task<IReadOnlyList<QuickAction>> GetQuickActionsForDocumentAsync(
        Document? document, int position, CancellationToken ct)
    {
        if (document is null) return [];

        var text = await document.GetTextAsync(ct).ConfigureAwait(false);
        var caret = Math.Clamp(position, 0, text.Length);

        var diagnostics = await GetDiagnosticsAtAsync(document, caret, ct).ConfigureAwait(false);

        // Titles are written as the providers register: in the interface language, not the operating system's.
        RoslynResourceCulture.UseInterfaceLanguage();

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
