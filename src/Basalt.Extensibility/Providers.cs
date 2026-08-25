namespace Basalt.Extensibility;

/// <summary>
/// A document as the providers see it.
///
/// Text plus a path, with no editor or workspace behind it, so a provider can
/// be exercised from a test without an IDE running.
/// </summary>
public sealed record LanguageDocument(string FilePath, string Text)
{
    /// <summary>Project the document belongs to, when it belongs to one.</summary>
    public string? ProjectPath { get; init; }
}

/// <summary>
/// Everything a language can offer the IDE.
///
/// Each capability is a separate interface returned from here rather than a
/// member of one large one: a language that has a parser but no compiler
/// implements what it has and returns null for the rest, instead of throwing
/// from methods it cannot support.
/// </summary>
public interface ILanguageProvider
{
    LanguageIdentity Identity { get; }

    ICompletionProvider? Completion { get; }
    IDiagnosticProvider? Diagnostics { get; }
    ISyntaxHighlightProvider? Highlighting { get; }
    INavigationProvider? Navigation { get; }
    IFormattingProvider? Formatting { get; }

    /// <summary>
    /// How this language describes a symbol, when it can.
    ///
    /// Null for a language with nothing to say, and the tooltip simply does
    /// not appear rather than appearing empty.
    /// </summary>
    ISymbolDescriptionProvider? Descriptions => null;
    ICompilerBackend? Compiler { get; }

    /// <summary>
    /// Tells the provider which solution is open, when it needs one.
    ///
    /// A provider backed by a compiler resolves symbols across projects; one
    /// that only parses a single file can ignore this.
    /// </summary>
    Task OpenSolutionAsync(string solutionOrProjectPath, CancellationToken ct = default)
        => Task.CompletedTask;
}

/// <summary>Entries offered while typing.</summary>
public interface ICompletionProvider
{
    /// <summary>
    /// Entries available at a position, given as an offset into the text.
    /// </summary>
    Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default);

    /// <summary>
    /// Characters that should open the list as soon as they are typed.
    /// A dot in most languages; a language may add its own.
    /// </summary>
    IReadOnlyList<char> TriggerCharacters => ['.'];

    /// <summary>Overloads and the current argument, while typing a call.</summary>
    Task<SignatureHelp?> GetSignatureHelpAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
        => Task.FromResult<SignatureHelp?>(null);
}

/// <summary>Errors and warnings for a document.</summary>
public interface IDiagnosticProvider
{
    Task<IReadOnlyList<Diagnostic>> GetDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct = default);
}

/// <summary>How a document is coloured.</summary>
public interface ISyntaxHighlightProvider
{
    /// <summary>
    /// Name of a definition AvaloniaEdit already knows, when one fits.
    /// </summary>
    string? BuiltInDefinitionName => null;

    /// <summary>
    /// Scope name of a TextMate grammar to use instead, for languages the
    /// editor has no built-in definition for.
    /// </summary>
    string? TextMateScopeName => null;

    /// <summary>
    /// Grammar contents, when the language ships its own rather than relying on
    /// one already installed.
    /// </summary>
    Task<string?> GetGrammarAsync(CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}

/// <summary>
/// Describing a symbol, in a form the tooltip can draw for any language.
///
/// Separate from navigation and completion because the same description
/// answers two questions — what is this, and what does this call expect —
/// and because a dialect can offer it without offering the rest. QuickBASIC
/// has a table of intrinsics and no compiler; it can still say what MID$
/// takes.
/// </summary>
public interface ISymbolDescriptionProvider
{
    /// <summary>
    /// What the symbol at a position is.
    ///
    /// Null when there is nothing there worth describing, which is most
    /// positions in a file.
    /// </summary>
    Task<SymbolDescription?> DescribeAsync(
        LanguageDocument document, int position, CancellationToken ct = default);

    /// <summary>
    /// What the call being written at a position expects.
    ///
    /// Null when the position is not inside a call. The set carries the
    /// overloads and which one fits; each description carries which parameter
    /// is being written.
    /// </summary>
    Task<SymbolDescriptionSet?> DescribeCallAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
        => Task.FromResult<SymbolDescriptionSet?>(null);
}

/// <summary>Moving around code.</summary>
public interface INavigationProvider
{
    /// <summary>Symbols declared in a document, nested as they are written.</summary>
    Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
        LanguageDocument document, CancellationToken ct = default);

    /// <summary>Where the symbol at a position is declared.</summary>
    Task<SourceLocation?> GoToDefinitionAsync(
        LanguageDocument document, int position, CancellationToken ct = default);

    /// <summary>Everywhere the symbol at a position is used.</summary>
    Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SourceLocation>>([]);

    /// <summary>Signature and documentation for the symbol at a position.</summary>
    Task<QuickInfo?> GetQuickInfoAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
        => Task.FromResult<QuickInfo?>(null);

    /// <summary>Symbols across the whole solution, filtered by a query.</summary>
    Task<IReadOnlyList<SourceLocation>> SearchSymbolsAsync(
        string query, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SourceLocation>>([]);
}

/// <summary>Laying out code.</summary>
public interface IFormattingProvider
{
    Task<FormattingResult> FormatDocumentAsync(
        LanguageDocument document, CancellationToken ct = default);

    Task<FormattingResult> FormatRangeAsync(
        LanguageDocument document, int start, int length, CancellationToken ct = default);

    /// <summary>
    /// Tidies the line the caret is on, applied when the caret leaves it.
    /// </summary>
    Task<FormattingResult> FormatLineAsync(
        LanguageDocument document, int caret, CancellationToken ct = default);

    /// <summary>
    /// Indentation, in spaces, that a new line at this position should start
    /// with. Asked after Enter, where the line is still empty.
    /// </summary>
    Task<int> GetIndentationAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
        => Task.FromResult(0);

    /// <summary>
    /// The line that closes a block opened on the given line, when one is
    /// opened and not yet closed. "End If" for Visual Basic, and so on.
    /// </summary>
    Task<string?> GetBlockClosingAsync(
        LanguageDocument document, int lineIndex, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>Whether typing this character should re-lay out its line.</summary>
    bool TriggersFormatting(char character) => false;
}

/// <summary>A machine a compiler can produce output for.</summary>
public sealed record CompilationTarget(string RuntimeIdentifier)
{
    /// <summary>Where the output goes.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Optimised rather than debuggable.</summary>
    public bool Optimise { get; init; } = true;

    /// <summary>Runs without a runtime installed on the target machine.</summary>
    public bool SelfContained { get; init; }

    /// <summary>
    /// Which of a language's outputs to produce, where it has more than one.
    ///
    /// Null asks for the language's usual one, so a caller that does not care
    /// gets what it always got.
    /// </summary>
    public string? CodeTarget { get; init; }
}

/// <summary>What came out of a compilation.</summary>
public sealed record CompilationResult(
    bool Succeeded,
    IReadOnlyList<Diagnostic> Diagnostics,
    string? OutputPath,
    TimeSpan Duration);

/// <summary>Turning source into something runnable.</summary>
public interface ICompilerBackend
{
    /// <summary>Targets this backend can produce, as runtime identifiers.</summary>
    IReadOnlyList<string> SupportedTargets { get; }

    Task<CompilationResult> CompileAsync(
        string projectPath, CompilationTarget target, CancellationToken ct = default);

    /// <summary>Output written while compiling, for the output panel.</summary>
    event EventHandler<string>? OutputReceived;
}
