using Basalt.Extensibility;
using Basalt.Razor.Vb;

namespace Basalt.Workspace.Web;

/// <summary>
/// Completion inside a .vbhtml, for both halves of the file.
///
/// The markup half is HTML and gets HTML completion. The code half is Visual
/// Basic, and rather than guessing at what a model holds, the template is
/// generated to Visual Basic, the caret is carried across through the source
/// map, and the language service is asked. What comes back is what the
/// compiler knows, which is the only thing worth offering.
///
/// This is how Razor itself works: it does not reimplement completion, it
/// generates a document, delegates, and maps the answers back.
/// </summary>
public sealed class VbHtmlCompletionProvider : ICompletionProvider
{
    /// <summary>
    /// Which runtime the view is generated for while answering questions.
    /// </summary>
    /// <remarks>
    /// The generated code must compile against what the user's project
    /// actually references. Generating the standalone shape for an ASP.NET
    /// Core project makes the view inherit a base class that project has
    /// never heard of, and Roslyn then resolves nothing at all — every
    /// question comes back empty, which reads as "nothing to suggest here"
    /// rather than as a fault.
    ///
    /// Standalone by default, because it is the shape that compiles against
    /// the least: a project without ASP.NET Core cannot resolve RazorPage,
    /// and defaulting the other way broke every caller that had none. Whoever
    /// knows the project is a web one says so.
    /// </remarks>
    public ViewHost Host { get; init; } = ViewHost.Standalone;

    /// <summary>
    /// A way to learn a .vbrazor's component catalog before writing it, so
    /// tags are written the way the build writes them rather than the way
    /// the writer alone can guess.
    /// </summary>
    public Func<string, string, CancellationToken, Task<IComponentCatalog?>>? AskCatalog
    { get; init; }

    private readonly HtmlCompletionProvider _html = new();
    private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<CompletionItem>>>? _ask;

    private readonly Func<string, int, CancellationToken, Task<SignatureHelp?>>?
        _askSignature;

    /// <summary>
    /// Without a language service to ask, only the markup half answers.
    ///
    /// That is the honest fallback: a template edited outside a solution has
    /// no compilation behind it, and offering invented members would be
    /// worse than offering none.
    /// </summary>
    public VbHtmlCompletionProvider() { }

    /// <summary>
    /// With a way to ask the Visual Basic language service about a position
    /// in generated code.
    /// </summary>
    public VbHtmlCompletionProvider(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CompletionItem>>> ask) =>
        _ask = ask;

    /// <summary>
    /// With a way to ask about calls as well as about names.
    /// </summary>
    public VbHtmlCompletionProvider(
        Func<string, int, CancellationToken, Task<IReadOnlyList<CompletionItem>>> ask,
        Func<string, int, CancellationToken, Task<SignatureHelp?>> askSignature)
    {
        _ask = ask;
        _askSignature = askSignature;
    }

    public async Task<IReadOnlyList<CompletionItem>> GetCompletionsAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        var answered = await AnswerAsync(document, position, ct).ConfigureAwait(false);

        // "@Mo" at the start of a line may become "@ModelType" or "@Model.":
        // the directives the parser reads come first, then whatever the code
        // there could be. Neither editor offered most of them: the language
        // server had seven in a list of its own, the IDE none.
        if (TemplateGeneration.IsPage(document.FilePath) ||
            !VbHtmlDirectives.IsDirectivePosition(document.Text, position))
            return answered;

        var directives = VbHtmlDirectives.For(document.FilePath)
            .Select(directive => new CompletionItem(directive.Name, directive.Insertion, SymbolKind.Keyword)
            {
                Description = directive.Description,
                FilterText = directive.Name
            });

        return [.. directives, .. answered];
    }

    private async Task<IReadOnlyList<CompletionItem>> AnswerAsync(
        LanguageDocument document, int position, CancellationToken ct)
    {
        var spaces = SpacesBeforeCaret(document.Text, position);

        // Which half the caret is in decides who answers. A statement block's
        // trailing whitespace counts as code here, and only here: after
        // "= New " on the block's last line a space has asked for the types.
        var inCode = TemplateGeneration.IsInCode(document.FilePath, document.Text, position) ||
                     (spaces.Length > 0 && !TemplateGeneration.IsPage(document.FilePath) &&
                      VbHtmlCodeRegions.IsAfterStatementBody(document.Text, position));

        if (!inCode)
            return await _html.GetCompletionsAsync(document, position, ct).ConfigureAwait(false);

        if (_ask is null) return [];

        var generated = await TemplateGeneration.ForAsync(
            document.FilePath, Host, document.Text, AskCatalog, ct).ConfigureAwait(false);

        if (CaretInGenerated(document.FilePath, document.Text, generated, position) is not { } at)
            return [];

        var (code, caret) = WithSpacesBefore(generated.Code, at, spaces);

        return await _ask(code, caret, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The spaces and tabs typed just before the caret, or none when they
    /// reach back to the start of the line, where they are indentation.
    /// </summary>
    internal static string SpacesBeforeCaret(string template, int position)
    {
        var start = position;

        while (start > 0 && template[start - 1] is ' ' or '\t')
            start--;

        if (start == 0 || template[start - 1] is '\n' or '\r') return "";

        return template[start..position];
    }

    /// <summary>
    /// The generated code with the spaces typed before the caret, where the
    /// writer left them out.
    /// </summary>
    /// <remarks>
    /// Inside a line the writer keeps them and the caret already follows
    /// them. At the end of a statement it drops them, and the caret is
    /// carried to the end of the word: Roslyn, told a space was typed, found
    /// none and offered nothing. They are put back for this question only;
    /// the writer, its mappings and what gets compiled are unchanged.
    /// </remarks>
    internal static (string Code, int Caret) WithSpacesBefore(string code, int at, string spaces)
    {
        if (spaces.Length == 0) return (code, at);

        if (at >= spaces.Length && string.CompareOrdinal(code, at - spaces.Length, spaces, 0, spaces.Length) == 0)
            return (code, at);

        return (code.Insert(at, spaces), at + spaces.Length);
    }

    /// <summary>
    /// What the call the caret is inside expects.
    ///
    /// Delegated like everything else: the runtime's own helpers used to be
    /// described by a table of three entries, which said nothing about the
    /// model's methods — the ones a template actually calls.
    /// </summary>
    public async Task<SignatureHelp?> GetSignatureHelpAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        // Nothing in the markup half takes arguments.
        if (_askSignature is null ||
            !TemplateGeneration.IsInCode(document.FilePath, document.Text, position)) return null;

        var generated = await TemplateGeneration.ForAsync(
            document.FilePath, Host, document.Text, AskCatalog, ct)
            .ConfigureAwait(false);

        if (CaretInGenerated(document.FilePath, document.Text, generated, position) is not { } at)
            return null;

        return await _askSignature(generated.Code, at, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Where a template position lands in the generated code.
    ///
    /// The line-accurate mapping first, because span arithmetic drifts: a
    /// mapped region and the code written from it are different lengths, so
    /// an offset measured from the region start means a different place at
    /// the other end. Roslyn forgives that for completion, which looks around
    /// the caret, but signature help answered nothing at all.
    ///
    /// The span mapping stays as the fallback for a caret the line mapping
    /// cannot place — past the end of everything mapped, typically, where
    /// Inclusive and then Inferred still find something useful.
    /// </summary>
    /// <remarks>
    /// The mapping itself first, now that it starts at each expression's own
    /// text on both sides: the line heuristic counts from the last "@" on the
    /// line and takes the first mapping there, so in <c>@a @String.Join(",",
    /// b)</c> it answered about Write(a).
    /// </remarks>
    private static int? CaretInGenerated(
        string path, string template, TemplateGeneration.Generated generated, int position) =>
        TemplateGeneration.StatementLineCaret(path, template, generated, position)
        ?? generated.Map.ToGenerated(position, template, generated.Code, MappingBehavior.Strict)
        ?? (TemplateGeneration.IsPage(path) ? null : VbHtmlCodeRegions.CaretInGenerated(
            template, generated.Code, generated.Map, position))
        ?? generated.Map.ToGenerated(position, template, generated.Code, MappingBehavior.Inclusive)
        ?? generated.Map.ToGenerated(position, template, generated.Code, MappingBehavior.Inferred)
        ?? NearestBefore(generated.Map, position);

    /// <summary>
    /// The end of the last mapping that starts before a position.
    ///
    /// Where the caret has run past everything mapped — typing a dot after
    /// the final expression — the language service still has to be asked
    /// about somewhere, and the end of that expression is the right place.
    /// </summary>
    private static int? NearestBefore(SourceMap map, int position)
    {
        SourceMapping? best = null;

        foreach (var mapping in map.Mappings)
            if (mapping.Original.Start <= position) best = mapping;

        return best is { } found ? found.Generated.Start + found.Generated.Length : null;
    }

    /// <summary>
    /// Whether a position sits in the Visual Basic half of the template.
    ///
    /// The decision itself lives in the shared library, so the standalone
    /// language server answers it the same way.
    /// </summary>
    internal static bool IsInCode(string text, int position) =>
        VbHtmlCodeRegions.IsInCode(text, position);
}
