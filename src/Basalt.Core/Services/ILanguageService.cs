using Basalt.Core.Model;

namespace Basalt.Core.Services;

/// <summary>
/// Roslyn-based language service, valid for both C# and VB.NET.
/// Positions are absolute offsets within the document text.
/// </summary>
public interface ILanguageService
{
    Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default);

    /// <summary>Updates in memory the text of a document open in the editor.</summary>
    Task UpdateDocumentAsync(string filePath, string text, CancellationToken ct = default);

    Task<IReadOnlyList<IdeDiagnostic>> GetDiagnosticsAsync(string filePath, CancellationToken ct = default);

    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(string filePath, int position, CancellationToken ct = default);

    /// <summary>Returns the definition location of the symbol under the caret.</summary>
    Task<(string FilePath, int Line, int Column)?> GoToDefinitionAsync(string filePath, int position, CancellationToken ct = default);

    /// <summary>Informational text (signature and documentation) for the symbol under the caret.</summary>
    Task<string?> GetQuickInfoAsync(string filePath, int position, CancellationToken ct = default);
}
