using Basalt.Core.Model;
using Basalt.Extensibility;

namespace Basalt.Workspace.Languages;

/// <summary>
/// Visual Basic and C# on top of Roslyn, exposed through the extensibility
/// contracts.
///
/// The existing Roslyn services stay as they are and are adapted here: the
/// abstraction has to fit the language the IDE already supports before it can
/// be trusted to fit one it does not.
/// </summary>
public sealed class RoslynLanguageProvider : ILanguageProvider, IDisposable
{
    private readonly RoslynLanguageService _language;
    private readonly RoslynFormattingService _formatting;
    private readonly SourceLanguage _sourceLanguage;

    private RoslynLanguageProvider(
        LanguageIdentity identity,
        SourceLanguage sourceLanguage,
        RoslynLanguageService language,
        RoslynFormattingService formatting)
    {
        Identity = identity;
        _sourceLanguage = sourceLanguage;
        _language = language;
        _formatting = formatting;

        Completion = new RoslynCompletionProvider(language);
        Diagnostics = new RoslynDiagnosticProvider(language);
        Navigation = new RoslynNavigationProvider(language);
        Formatting = new RoslynFormattingProvider(formatting, sourceLanguage);
        Highlighting = new RoslynHighlightProvider(sourceLanguage);
        Descriptions = new RoslynSymbolDescriptionProvider(language);
        Compiler = new MsBuildCompilerBackend();
    }

    /// <summary>
    /// Visual Basic. The IDE's first language, and the one its behaviour is
    /// shaped around.
    /// </summary>
    public static RoslynLanguageProvider CreateVisualBasic(
        RoslynLanguageService language, RoslynFormattingService formatting) =>
        new(new LanguageIdentity(
                id: "vb",
                displayName: "Visual Basic",
                fileExtensions: [".vb"],
                isCaseSensitive: false),
            SourceLanguage.VisualBasic,
            language,
            formatting);

    public LanguageIdentity Identity { get; }

    public ICompletionProvider? Completion { get; }
    public IDiagnosticProvider? Diagnostics { get; }
    public ISyntaxHighlightProvider? Highlighting { get; }
    public INavigationProvider? Navigation { get; }
    public IFormattingProvider? Formatting { get; }
    public ICompilerBackend? Compiler { get; }

    public ISymbolDescriptionProvider? Descriptions { get; }

    public Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default) =>
        _language.OpenSolutionAsync(solutionOrProjectPath, ct);

    public void Dispose()
    {
        _language.Dispose();
        _formatting.Dispose();
    }
}
