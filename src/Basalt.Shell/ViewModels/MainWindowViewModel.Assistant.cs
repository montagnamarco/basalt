using Avalonia.Threading;
using Basalt.Core.Model;
using Basalt.Workspace;

namespace Basalt.Shell.ViewModels;

/// <summary>What the assistant's tools ask of the open solution.</summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// Findings for one file, or the ones currently shown for the solution.
    ///
    /// Called from a tool running on a pool thread: the editors' text is read
    /// on the interface thread, the language service is asked off it.
    /// </summary>
    internal async Task<IReadOnlyList<IdeDiagnostic>> GetDiagnosticsForAssistantAsync(
        string? filePath, CancellationToken ct)
    {
        if (filePath is null)
            return await Dispatcher.UIThread.InvokeAsync(() => Diagnostics.ToList());

        var open = await Dispatcher.UIThread.InvokeAsync(() => OpenDocuments
            .Where(d => !d.OpenInDesigner)
            .Select(d => (d.FilePath, d.Text))
            .ToList());

        // The path the tool gives is canonical; the language service knows
        // the file under the spelling it was loaded with, which is the
        // open document's.
        foreach (var (path, text) in open)
        {
            if (!PathIdentity.Same(path, filePath)) continue;

            await _languageService.UpdateDocumentAsync(path, text, ct).ConfigureAwait(false);

            return await _languageService.GetDiagnosticsAsync(path, ct).ConfigureAwait(false);
        }

        return await _languageService.GetDiagnosticsAsync(filePath, ct).ConfigureAwait(false);
    }

    internal Task<IReadOnlyList<RoslynLanguageService.FoundSymbol>> SearchSymbolDetailsForAssistantAsync(
        string name, CancellationToken ct) =>
        _languageService.SearchSymbolDetailsAsync(name, ct);
}
