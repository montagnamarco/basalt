using Basalt.Core.Model;
using Basalt.Workspace.Languages;

namespace Basalt.Shell.ViewModels;

/// <summary>
/// Sends diagnostics, completion and go-to-definition to the language that
/// owns a file.
///
/// Visual Basic keeps the Roslyn path it always had. Every other language
/// registered with the IDE (Razor for Visual Basic, HTML, CSS) answers
/// through its own providers. Those providers map their results back onto
/// the document themselves, so nothing here converts a position: the shell
/// only changes the shape of the answer, not where it points.
/// </summary>
public sealed partial class MainWindowViewModel
{
    /// <summary>
    /// The provider that answers for a file when the file is not plain
    /// Visual Basic, or null when the Roslyn path should answer as before.
    ///
    /// Null also when no language claims the file: a file the IDE does not
    /// know keeps the behaviour it had, rather than being silently emptied.
    /// </summary>
    private Basalt.Extensibility.ILanguageProvider? RoutedProviderFor(string filePath) =>
        Languages.ForFile(filePath) is { } provider and not RoslynLanguageProvider
            ? provider
            : null;

    /// <summary>
    /// The diagnostics a provider reports for a document, in the shape the
    /// problems list keeps them.
    ///
    /// The provider runs on the thread pool: a Razor provider parses the
    /// template and asks Roslyn about the code it generates, and none of
    /// that belongs on the interface thread.
    /// </summary>
    private static async Task<IReadOnlyList<IdeDiagnostic>> ProviderDiagnosticsAsync(
        Basalt.Extensibility.ILanguageProvider provider, string filePath, string text)
    {
        // A language with nothing to say about errors claims none, which
        // clears what an earlier build reported for the file.
        if (provider.Diagnostics is not { } source) return [];

        var document = new Basalt.Extensibility.LanguageDocument(filePath, text);
        var found = await Task.Run(() => source.GetDiagnosticsAsync(document))
            .ConfigureAwait(true);

        return [.. found.Select(d => ToIdeDiagnostic(d, filePath))];
    }

    /// <summary>Completions a provider offers at an offset into a document.</summary>
    private static async Task<IReadOnlyList<CompletionItem>> ProviderCompletionsAsync(
        Basalt.Extensibility.ILanguageProvider provider, string filePath, int offset, string text)
    {
        if (provider.Completion is not { } source) return [];

        var document = new Basalt.Extensibility.LanguageDocument(filePath, text);
        var found = await Task.Run(() => source.GetCompletionsAsync(document, offset))
            .ConfigureAwait(true);

        return [.. found.Select(ToCompletionItem)];
    }

    /// <summary>
    /// Where a provider says the symbol at an offset is declared. The
    /// location is already in the coordinates of the file it names, which
    /// for Razor is the .vb file declaring a model member.
    /// </summary>
    private static async Task<(string FilePath, int Line, int Column)?> ProviderDefinitionAsync(
        Basalt.Extensibility.ILanguageProvider provider, string filePath, int position, string text)
    {
        if (provider.Navigation is not { } source) return null;

        var document = new Basalt.Extensibility.LanguageDocument(filePath, text);
        var found = await Task.Run(() => source.GoToDefinitionAsync(document, position))
            .ConfigureAwait(true);

        if (found is not { } location) return null;

        var start = location.Range.Start;
        return (location.FilePath, start.Line, start.Column);
    }

    /// <summary>
    /// The text a completion or navigation request is about when the caller
    /// did not supply it: the open editor's buffer, else the file on disk.
    /// </summary>
    private string TextOf(string filePath) =>
        OpenDocuments.FirstOrDefault(d => string.Equals(d.FilePath, filePath, StringComparison.Ordinal))
            is { } open
            ? open.Text
            : File.Exists(filePath) ? File.ReadAllText(filePath) : string.Empty;

    private static IdeDiagnostic ToIdeDiagnostic(Basalt.Extensibility.Diagnostic diagnostic, string filePath) =>
        new(
            diagnostic.Id,
            diagnostic.Message,
            diagnostic.Severity switch
            {
                Basalt.Extensibility.DiagnosticSeverity.Error => DiagnosticSeverity.Error,
                Basalt.Extensibility.DiagnosticSeverity.Warning => DiagnosticSeverity.Warning,
                Basalt.Extensibility.DiagnosticSeverity.Info => DiagnosticSeverity.Info,
                _ => DiagnosticSeverity.Hidden
            },
            diagnostic.FilePath ?? filePath,
            diagnostic.Range.Start.Line,
            diagnostic.Range.Start.Column);

    private static CompletionItem ToCompletionItem(Basalt.Extensibility.CompletionItem item) =>
        new(item.DisplayText, item.InsertionText, ToCompletionKind(item.Kind))
        {
            Description = item.Description,
            IsPreselected = item.IsPreselected
        };

    /// <summary>
    /// The editor's completion kinds are fewer than the providers report, so
    /// a kind with no counterpart (a constant, a module, a delegate) takes the
    /// nearest one, the way the editor's icons already draw it.
    /// </summary>
    private static CompletionKind ToCompletionKind(Basalt.Extensibility.SymbolKind kind) => kind switch
    {
        Basalt.Extensibility.SymbolKind.Class or Basalt.Extensibility.SymbolKind.Module => CompletionKind.Class,
        Basalt.Extensibility.SymbolKind.Structure => CompletionKind.Structure,
        Basalt.Extensibility.SymbolKind.Interface => CompletionKind.Interface,
        Basalt.Extensibility.SymbolKind.Enum => CompletionKind.Enum,
        Basalt.Extensibility.SymbolKind.Method
            or Basalt.Extensibility.SymbolKind.Function
            or Basalt.Extensibility.SymbolKind.Delegate => CompletionKind.Method,
        Basalt.Extensibility.SymbolKind.Property => CompletionKind.Property,
        Basalt.Extensibility.SymbolKind.Field
            or Basalt.Extensibility.SymbolKind.Constant => CompletionKind.Field,
        Basalt.Extensibility.SymbolKind.Event => CompletionKind.Event,
        Basalt.Extensibility.SymbolKind.Variable => CompletionKind.Local,
        Basalt.Extensibility.SymbolKind.Parameter => CompletionKind.Parameter,
        Basalt.Extensibility.SymbolKind.Namespace => CompletionKind.Namespace,
        Basalt.Extensibility.SymbolKind.Keyword => CompletionKind.Keyword,
        Basalt.Extensibility.SymbolKind.Snippet => CompletionKind.Snippet,
        _ => CompletionKind.Other
    };
}
