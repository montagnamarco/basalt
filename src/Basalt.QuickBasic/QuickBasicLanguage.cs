using Basalt.Extensibility;
using IdeDiagnostic = Basalt.Extensibility.Diagnostic;
using QbDiagnostic = Basalt.QuickBasic.Diagnostic;

namespace Basalt.QuickBasic;

/// <summary>
/// QuickBASIC, offered to the IDE through the extensibility contracts.
///
/// Nothing here reaches into the IDE: this project references only the
/// contracts, which is what makes it the proof that a language can be added
/// from outside.
/// </summary>
public sealed class QuickBasicLanguageProvider : ILanguageProvider
{
    public LanguageIdentity Identity { get; } = new(
        "quickbasic", "QuickBASIC", [".bas", ".qb"], isCaseSensitive: false);

    public ICompletionProvider? Completion { get; } = new QuickBasicCompletionProvider();
    public IDiagnosticProvider? Diagnostics { get; } = new QuickBasicDiagnosticProvider();
    public ISyntaxHighlightProvider? Highlighting { get; } = new QuickBasicHighlightProvider();
    public INavigationProvider? Navigation { get; } = new QuickBasicNavigationProvider();
    public IFormattingProvider? Formatting => null;

    /// <summary>
    /// What a name means, in the same shape Visual Basic uses.
    ///
    /// No compiler behind it: a table of intrinsics and the parse tree.
    /// </summary>
    public ISymbolDescriptionProvider? Descriptions { get; } =
        new QuickBasicSymbolDescriptionProvider();
    public ICompilerBackend? Compiler { get; } = new QuickBasicCompilerBackend();

    public Task OpenSolutionAsync(string path, CancellationToken ct = default) =>
        Task.CompletedTask;
}

/// <summary>Completion for QuickBASIC: its keywords and what the file declares.</summary>
public sealed class QuickBasicCompletionProvider : ICompletionProvider
{
    public Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var items = new List<CompletionItem>();

        foreach (var keyword in Lexer.Keywords.OrderBy(k => k, StringComparer.Ordinal))
        {
            items.Add(new CompletionItem(keyword, keyword, SymbolKind.Keyword)
            {
                Description = "Keyword."
            });
        }

        // What the program itself declares matters more than the keywords, so
        // a parse failure must not cost the user their own names.
        try
        {
            var program = Parser.Parse(document.Text);

            foreach (var procedure in program.Procedures)
            {
                items.Add(new CompletionItem(
                    procedure.Name, procedure.Name,
                    procedure.IsFunction ? SymbolKind.Function : SymbolKind.Method)
                {
                    Description = Describe(procedure)
                });
            }

            foreach (var variable in SymbolTable.Build(program).Globals)
            {
                items.Add(new CompletionItem(variable.Name, variable.Name, SymbolKind.Variable)
                {
                    Description = $"{variable.Type}{(variable.IsArray ? " array" : "")}."
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The keywords are still worth offering.
        }

        var prefix = WordBefore(document.Text, position);

        if (prefix.Length > 0)
        {
            items = [.. items
                .Where(i => i.DisplayText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(i => i.DisplayText.Length)
                .ThenBy(i => i.DisplayText, StringComparer.OrdinalIgnoreCase)];
        }

        return Task.FromResult<IReadOnlyList<CompletionItem>>(items);
    }

    private static string Describe(Procedure procedure)
    {
        var parameters = string.Join(", ", procedure.Parameters.Select(p => p.Name));

        return procedure.IsFunction
            ? $"FUNCTION {procedure.Name}({parameters}) AS {procedure.ReturnType}"
            : $"SUB {procedure.Name}({parameters})";
    }

    private static string WordBefore(string text, int position)
    {
        var caret = Math.Clamp(position, 0, text.Length);
        var start = caret;

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
            start--;

        return text[start..caret];
    }
}

/// <summary>The problems in a QuickBASIC program, reported while editing.</summary>
public sealed class QuickBasicDiagnosticProvider : IDiagnosticProvider
{
    public Task<IReadOnlyList<IdeDiagnostic>> GetDiagnosticsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        List<QbDiagnostic> found;

        try
        {
            var program = Parser.Parse(document.Text);

            found = [.. program.Diagnostics, .. SymbolTable.Build(program).Diagnostics];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            found = [new QbDiagnostic("QB999", $"The program could not be read: {ex.Message}", 1, 1)];
        }

        return Task.FromResult<IReadOnlyList<IdeDiagnostic>>(
            [.. found.Select(d => new IdeDiagnostic(
                d.Id, d.Message, DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(d.Line, d.Column)),
                document.FilePath))]);
    }
}

/// <summary>
/// Colouring for QuickBASIC.
///
/// No grammar is shipped: the editor's Visual Basic definition already reads
/// the same keywords, strings and comments, and writing a TextMate grammar to
/// say the same thing again would be work for no gain.
/// </summary>
public sealed class QuickBasicHighlightProvider : ISyntaxHighlightProvider
{
    public string? BuiltInDefinitionName => "VB";
}

/// <summary>Finding the procedures a QuickBASIC file declares.</summary>
public sealed class QuickBasicNavigationProvider : INavigationProvider
{
    public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        var symbols = new List<DocumentSymbol>();

        try
        {
            foreach (var procedure in Parser.Parse(document.Text).Procedures)
            {
                var start = new SourcePosition(procedure.Line, procedure.Column);

                symbols.Add(new DocumentSymbol(
                    procedure.Name,
                    procedure.IsFunction ? SymbolKind.Function : SymbolKind.Method,
                    new SourceRange(start, start)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // An outline of nothing is better than an error while typing.
        }

        return Task.FromResult<IReadOnlyList<DocumentSymbol>>(symbols);
    }

    public Task<SourceLocation?> GoToDefinitionAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var name = WordAt(document.Text, position);

        if (name.Length == 0) return Task.FromResult<SourceLocation?>(null);

        try
        {
            var procedure = Parser.Parse(document.Text).Procedures
                .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (procedure is null) return Task.FromResult<SourceLocation?>(null);

            return Task.FromResult<SourceLocation?>(new SourceLocation(
                document.FilePath,
                SourceRange.At(new SourcePosition(procedure.Line, procedure.Column))));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Task.FromResult<SourceLocation?>(null);
        }
    }

    public Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var name = WordAt(document.Text, position);

        if (name.Length == 0) return Task.FromResult<IReadOnlyList<SourceLocation>>([]);

        var locations = new List<SourceLocation>();

        foreach (var token in new Lexer(document.Text).Tokenize())
        {
            if (token.Kind != TokenKind.Identifier) continue;
            if (!token.Text.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;

            locations.Add(new SourceLocation(
                document.FilePath,
                SourceRange.At(new SourcePosition(token.Line, token.Column))));
        }

        return Task.FromResult<IReadOnlyList<SourceLocation>>(locations);
    }

    private static string WordAt(string text, int position)
    {
        var caret = Math.Clamp(position, 0, text.Length);

        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
            start--;

        var end = caret;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;

        return text[start..end];
    }
}

/// <summary>Building a QuickBASIC program into a native executable.</summary>
public sealed class QuickBasicCompilerBackend : ICompilerBackend
{
    private readonly NativeCompiler _compiler = new();

    public event EventHandler<string>? OutputReceived;

    /// <summary>
    /// What this can build for.
    ///
    /// The macOS pair needs nothing extra; the others need the target's
    /// headers and libraries, so they are offered but will say what is missing
    /// rather than failing obscurely at the link step.
    /// </summary>
    public IReadOnlyList<string> SupportedTargets { get; } =
    [
        "osx-arm64", "osx-x64",
        "linux-arm64", "linux-x64",
        "win-arm64", "win-x64"
    ];

    /// <summary>
    /// Produces one of the other targets: VB.NET today, native eventually.
    ///
    /// Writes the generated file and stops there. A target that cannot yet be
    /// built says so rather than writing something that will not link.
    /// </summary>
    private async Task<CompilationResult> GenerateOtherTargetAsync(
        string projectPath,
        string source,
        string targetId,
        string output,
        System.Diagnostics.Stopwatch started,
        CancellationToken ct)
    {
        if (CodeGeneration.CodeGenerators.ById(targetId) is not { } generator)
        {
            return Failed(projectPath, "QB110",
                $"'{targetId}' is not a target this language offers.", started.Elapsed);
        }

        var program = Parser.Parse(source);

        if (program.Diagnostics.Count > 0)
        {
            return new CompilationResult(
                false,
                [.. program.Diagnostics.Select(d => new IdeDiagnostic(
                    d.Id, d.Message, DiagnosticSeverity.Error,
                    SourceRange.At(new SourcePosition(d.Line, d.Column)), projectPath))],
                null,
                started.Elapsed);
        }

        var symbols = SymbolTable.Build(program);

        if (symbols.Diagnostics.Count > 0)
        {
            return new CompilationResult(
                false,
                [.. symbols.Diagnostics.Select(d => new IdeDiagnostic(
                    d.Id, d.Message, DiagnosticSeverity.Error,
                    SourceRange.At(new SourcePosition(d.Line, d.Column)), projectPath))],
                null,
                started.Elapsed);
        }

        var generated = Path.ChangeExtension(output, generator.FileExtension);

        try
        {
            await File.WriteAllTextAsync(
                generated, generator.Generate(program, symbols), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(projectPath, "QB111", ex.Message, started.Elapsed);
        }

        // An unfinished target produces its file and says plainly that it does
        // not build, rather than reporting success for something unusable.
        if (!generator.IsComplete)
        {
            return new CompilationResult(
                false,
                [new IdeDiagnostic("QB112",
                    $"The {generator.DisplayName} target wrote {Path.GetFileName(generated)}, "
                  + "but it cannot be built yet.",
                    DiagnosticSeverity.Warning,
                    SourceRange.At(new SourcePosition(1, 1)), projectPath)],
                generated,
                started.Elapsed);
        }

        return new CompilationResult(true, [], generated, started.Elapsed);
    }

    public async Task<CompilationResult> CompileAsync(
        string projectPath, CompilationTarget target, CancellationToken ct = default)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();

        string source;

        try
        {
            source = await File.ReadAllTextAsync(projectPath, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Failed(projectPath, "QB102", ex.Message, started.Elapsed);
        }

        var output = target.OutputPath ?? Path.ChangeExtension(projectPath, Executable);

        // A target other than C is generated here: only C goes on to clang.
        if (target.CodeTarget is { Length: > 0 } requested
            && !requested.Equals("c", StringComparison.OrdinalIgnoreCase))
        {
            return await GenerateOtherTargetAsync(
                projectPath, source, requested, output, started, ct).ConfigureAwait(false);
        }

        var (architecture, operatingSystem) = Split(target.RuntimeIdentifier);

        var outcome = await _compiler.CompileAsync(
            new CompilationRequest(source, output)
            {
                Architecture = architecture,
                OperatingSystem = operatingSystem
            },
            ct).ConfigureAwait(false);

        if (outcome.CompilerOutput.Length > 0)
            OutputReceived?.Invoke(this, outcome.CompilerOutput);

        return new CompilationResult(
            outcome.Succeeded,
            [.. outcome.Diagnostics.Select(d => new IdeDiagnostic(
                d.Id, d.Message, DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(d.Line, d.Column)),
                projectPath))],
            outcome.OutputPath,
            started.Elapsed);
    }

    private static string Executable => System.OperatingSystem.IsWindows() ? ".exe" : "";

    /// <summary>Reads a runtime identifier such as "osx-arm64".</summary>
    internal static (string Architecture, string OperatingSystem) Split(string runtimeIdentifier)
    {
        var parts = runtimeIdentifier.Split('-');

        if (parts.Length < 2) return (NativeCompiler.HostArchitecture, NativeCompiler.HostOperatingSystem);

        var operatingSystem = parts[0] switch
        {
            "osx" => "macos",
            "win" => "windows",
            _ => "linux"
        };

        // .NET writes x64 where clang writes x86_64.
        var architecture = parts[1] switch
        {
            "x64" => "x86_64",
            "arm64" => "arm64",
            _ => parts[1]
        };

        return (architecture, operatingSystem);
    }

    private static CompilationResult Failed(
        string projectPath, string id, string message, TimeSpan duration) =>
        new(false,
            [new IdeDiagnostic(id, message, DiagnosticSeverity.Error,
                SourceRange.At(new SourcePosition(1, 1)), projectPath)],
            null,
            duration);
}
