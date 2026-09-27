using Basalt.Core.Model;
using Basalt.Extensibility;
using CoreDiagnostic = Basalt.Core.Model.IdeDiagnostic;
using CoreSeverity = Basalt.Core.Model.DiagnosticSeverity;
using ExtDiagnostic = Basalt.Extensibility.Diagnostic;
using ExtSeverity = Basalt.Extensibility.DiagnosticSeverity;
using ExtCompletionItem = Basalt.Extensibility.CompletionItem;

namespace Basalt.Workspace.Languages;

/// <summary>Completion backed by Roslyn.</summary>
public sealed class RoslynCompletionProvider : ICompletionProvider
{
    private readonly RoslynLanguageService _language;

    public RoslynCompletionProvider(RoslynLanguageService language) => _language = language;

    public async Task<IReadOnlyList<ExtCompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        var items = await _language.GetCompletionsAsync(document.FilePath, position, ct)
            .ConfigureAwait(false);

        return items.Select(Convert).ToList();
    }

    /// <summary>
    /// Turns a Roslyn completion into the extensibility one.
    ///
    /// Public so the Razor language server converts the same way rather than
    /// keeping a second mapping that could drift.
    /// </summary>
    public static ExtCompletionItem Convert(Core.Model.CompletionItem item) =>
        new(item.DisplayText, item.InsertionText, MapKind(item.Kind))
        {
            Description = item.Description,
            IsPreselected = item.IsPreselected
        };

    private static SymbolKind MapKind(CompletionKind kind) => kind switch
    {
        CompletionKind.Class => SymbolKind.Class,
        CompletionKind.Structure => SymbolKind.Structure,
        CompletionKind.Interface => SymbolKind.Interface,
        CompletionKind.Enum => SymbolKind.Enum,
        CompletionKind.Method => SymbolKind.Method,
        CompletionKind.Property => SymbolKind.Property,
        CompletionKind.Field => SymbolKind.Field,
        CompletionKind.Event => SymbolKind.Event,
        CompletionKind.Local => SymbolKind.Variable,
        CompletionKind.Parameter => SymbolKind.Parameter,
        CompletionKind.Namespace => SymbolKind.Namespace,
        CompletionKind.Keyword => SymbolKind.Keyword,
        CompletionKind.Snippet => SymbolKind.Snippet,
        _ => SymbolKind.Unknown
    };
}

/// <summary>Diagnostics backed by Roslyn's semantic model.</summary>
internal sealed class RoslynDiagnosticProvider : IDiagnosticProvider
{
    private readonly RoslynLanguageService _language;

    public RoslynDiagnosticProvider(RoslynLanguageService language) => _language = language;

    public async Task<IReadOnlyList<ExtDiagnostic>> GetDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        var diagnostics = await _language.GetDiagnosticsAsync(document.FilePath, ct)
            .ConfigureAwait(false);

        return diagnostics.Select(Convert).ToList();
    }

    internal static ExtDiagnostic Convert(CoreDiagnostic diagnostic)
    {
        var start = new SourcePosition(Math.Max(1, diagnostic.Line), Math.Max(1, diagnostic.Column));

        return new ExtDiagnostic(
            diagnostic.Id,
            diagnostic.Message,
            MapSeverity(diagnostic.Severity),
            SourceRange.At(start),
            diagnostic.FilePath);
    }

    private static ExtSeverity MapSeverity(CoreSeverity severity) => severity switch
    {
        CoreSeverity.Error => ExtSeverity.Error,
        CoreSeverity.Warning => ExtSeverity.Warning,
        CoreSeverity.Info => ExtSeverity.Info,
        _ => ExtSeverity.Hidden
    };
}

/// <summary>Navigation backed by Roslyn's symbol model.</summary>
internal sealed class RoslynNavigationProvider : INavigationProvider
{
    private readonly RoslynLanguageService _language;

    public RoslynNavigationProvider(RoslynLanguageService language) => _language = language;

    public async Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        return await _language.GetDocumentSymbolsAsync(document.FilePath, ct)
            .ConfigureAwait(false);
    }

    public async Task<SourceLocation?> GoToDefinitionAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        var target = await _language.GoToDefinitionAsync(document.FilePath, position, ct)
            .ConfigureAwait(false);

        if (target is not { } found) return null;

        var start = new SourcePosition(found.Line, found.Column);
        return new SourceLocation(found.FilePath, SourceRange.At(start));
    }

    public async Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        return await _language.FindReferencesAsync(document.FilePath, position, ct)
            .ConfigureAwait(false);
    }

    public async Task<QuickInfo?> GetQuickInfoAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        await _language.UpdateDocumentAsync(document.FilePath, document.Text, ct)
            .ConfigureAwait(false);

        var info = await _language.GetQuickInfoAsync(document.FilePath, position, ct)
            .ConfigureAwait(false);

        if (info is null) return null;

        // The service returns signature and documentation joined by a blank
        // line; splitting them back lets the UI style each part.
        var separator = info.IndexOf("\n\n", StringComparison.Ordinal);

        return separator < 0
            ? new QuickInfo(info)
            : new QuickInfo(info[..separator], info[(separator + 2)..]);
    }

    public Task<IReadOnlyList<SourceLocation>> SearchSymbolsAsync(
        string query, CancellationToken ct = default) =>
        _language.SearchSymbolsAsync(query, ct);
}

/// <summary>Formatting backed by Roslyn.</summary>
internal sealed class RoslynFormattingProvider : IFormattingProvider
{
    private readonly RoslynFormattingService _formatting;
    private readonly SourceLanguage _language;

    public RoslynFormattingProvider(RoslynFormattingService formatting, SourceLanguage language)
    {
        _formatting = formatting;
        _language = language;
    }

    public async Task<FormattingResult> FormatDocumentAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        var result = await _formatting.FormatAsync(document.Text, _language, ct)
            .ConfigureAwait(false);

        return new FormattingResult(result.Text, 0, result.Changed);
    }

    public async Task<FormattingResult> FormatRangeAsync(
        LanguageDocument document, int start, int length, CancellationToken ct = default)
    {
        var result = await _formatting.FormatRangeAsync(document.Text, _language, start, length, ct)
            .ConfigureAwait(false);

        return new FormattingResult(result.Text, start, result.Changed);
    }

    public async Task<FormattingResult> FormatLineAsync(
        LanguageDocument document, int caret, CancellationToken ct = default)
    {
        // Visual Basic corrects casing and spacing together; C# only re-indents.
        var result = _language == SourceLanguage.VisualBasic
            ? await _formatting.ApplyTypingConventionsAsync(document.Text, _language, caret, ct)
                .ConfigureAwait(false)
            : await _formatting.FormatLineAsync(document.Text, _language, caret, ct)
                .ConfigureAwait(false);

        return new FormattingResult(result.Text, result.Caret, result.Changed);
    }

    public Task<int> GetIndentationAsync(
        LanguageDocument document, int position, CancellationToken ct = default) =>
        _formatting.GetIndentationAsync(document.Text, _language, position, ct);

    public Task<string?> GetBlockClosingAsync(
        LanguageDocument document, int lineIndex, CancellationToken ct = default) =>
        _language == SourceLanguage.VisualBasic
            ? VisualBasicBlockCompleter.GetClosingFor(document.Text, lineIndex, ct)
            : Task.FromResult<string?>(null);

    public bool TriggersFormatting(char character) =>
        _formatting.TriggersFormatting(character, _language);
}

/// <summary>Colouring, using the definitions AvaloniaEdit already carries.</summary>
internal sealed class RoslynHighlightProvider : ISyntaxHighlightProvider
{
    private readonly SourceLanguage _language;

    public RoslynHighlightProvider(SourceLanguage language) => _language = language;

    public string? BuiltInDefinitionName =>
        _language == SourceLanguage.VisualBasic ? "VB" : "C#";
}

/// <summary>Compilation through MSBuild.</summary>
internal sealed class MsBuildCompilerBackend : ICompilerBackend
{
    private readonly MsBuildBuildService _build = new();

    public MsBuildCompilerBackend() =>
        _build.OutputReceived += (_, line) => OutputReceived?.Invoke(this, line);

    /// <summary>
    /// MSBuild resolves the target from the project file, so any runtime
    /// identifier the installed SDK supports will do.
    /// </summary>
    public IReadOnlyList<string> SupportedTargets =>
        ["win-x64", "win-arm64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

    public event EventHandler<string>? OutputReceived;

    public async Task<CompilationResult> CompileAsync(
        string projectPath, CompilationTarget target, CancellationToken ct = default)
    {
        var result = await _build
            .BuildAsync(projectPath, target.Optimise ? "Release" : "Debug", ct)
            .ConfigureAwait(false);

        return new CompilationResult(
            result.Succeeded,
            result.Diagnostics.Select(RoslynDiagnosticProvider.Convert).ToList(),
            result.OutputAssemblyPath,
            result.Duration);
    }
}
