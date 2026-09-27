using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Microsoft.CodeAnalysis.Text;

namespace Basalt.Workspace.Web;

/// <summary>
/// Hover and structure inside a .vbhtml.
///
/// The same bridge completion uses: the template is generated to Visual
/// Basic, the position is carried across by the source map, and the language
/// service answers. Hovering a model property in a template should say what
/// hovering it in a .vb file says, because it is the same property.
/// </summary>
public sealed class VbHtmlNavigationProvider : INavigationProvider
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
    /// A way to learn a .vbrazor's component catalog before writing it, so a
    /// position inside &lt;Header&gt; or a typed RenderFragment's @context
    /// maps into code that actually declares them.
    /// </summary>
    public Func<string, string, CancellationToken, Task<IComponentCatalog?>>? AskCatalog
    { get; init; }

    /// <summary>
    /// What the provider can ask a language service about the generated code.
    ///
    /// A record rather than a constructor per combination: each question is
    /// independently available or not, and four overloads for three questions
    /// is where that stops being readable.
    /// </summary>
    public sealed record Questions
    {
        public Func<string, int, CancellationToken, Task<QuickInfo?>>? QuickInfo
        { get; init; }

        public Func<string, int, CancellationToken, Task<SourceLocation?>>? Definition
        { get; init; }

        public Func<string, int, CancellationToken,
            Task<IReadOnlyList<SourceLocation>>>? References
        { get; init; }
    }

    private readonly Questions _questions = new();

    /// <summary>Without a language service, hover says nothing.</summary>
    public VbHtmlNavigationProvider() { }

    public VbHtmlNavigationProvider(
        Func<string, int, CancellationToken, Task<QuickInfo?>> ask) =>
        _questions = new Questions { QuickInfo = ask };

    public VbHtmlNavigationProvider(Questions questions) => _questions = questions;

    public Task<IReadOnlyList<DocumentSymbol>> GetDocumentSymbolsAsync(
        LanguageDocument document, CancellationToken ct = default)
    {
        var parsed = VbHtmlParser.Parse(document.Text);
        var symbols = new List<DocumentSymbol>();

        foreach (var node in parsed.Nodes)
        {
            var (name, kind) = Describe(node);

            if (name is null) continue;

            var at = SourceRange.At(new SourcePosition(node.Line, 1));

            symbols.Add(new DocumentSymbol(name, kind, at));
        }

        return Task.FromResult<IReadOnlyList<DocumentSymbol>>(symbols);
    }

    /// <summary>
    /// Where the symbol under the caret is declared.
    ///
    /// Usually another file — a model class — which needs no mapping at all.
    /// A definition inside the view itself comes back as a position in the
    /// generated code and has to travel back through the map, or be dropped:
    /// jumping into a file the author cannot see is worse than not jumping.
    /// </summary>
    public async Task<SourceLocation?> GoToDefinitionAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        if (_questions.Definition is null ||
            !VbHtmlCompletionProvider.IsInCode(document.Text, position))
            return null;

        var generated = await TemplateGeneration.ForAsync(
            VbHtmlParser.Parse(document.Text), document.FilePath, Host, document.Text, AskCatalog, ct)
            .ConfigureAwait(false);

        var mapped = generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inclusive)
                  ?? generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inferred);

        if (mapped is not { } at) return null;

        var found = await _questions.Definition(generated.Code, at, ct)
            .ConfigureAwait(false);

        if (found is null) return null;

        // Somewhere else entirely: hand it over as it stands.
        if (!IsGeneratedView(found.FilePath)) return found;

        return BackToTemplate(document, generated, found);
    }

    /// <summary>
    /// Every use of the symbol under the caret.
    ///
    /// Uses in real files pass through; uses inside the generated view come
    /// back as template positions, so renaming a model property shows the
    /// views that use it rather than pretending they do not exist.
    /// </summary>
    public async Task<IReadOnlyList<SourceLocation>> FindReferencesAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        if (_questions.References is null ||
            !VbHtmlCompletionProvider.IsInCode(document.Text, position))
            return [];

        var generated = await TemplateGeneration.ForAsync(
            VbHtmlParser.Parse(document.Text), document.FilePath, Host, document.Text, AskCatalog, ct)
            .ConfigureAwait(false);

        var mapped = generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inclusive)
                  ?? generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inferred);

        if (mapped is not { } at) return [];

        var found = await _questions.References(generated.Code, at, ct)
            .ConfigureAwait(false);

        var results = new List<SourceLocation>();

        foreach (var use in found)
        {
            if (!IsGeneratedView(use.FilePath))
            {
                results.Add(use);
                continue;
            }

            // A use in the scaffolding maps nowhere and is left out, rather
            // than listed against a line the author never wrote.
            if (BackToTemplate(document, generated, use) is { } onTemplate)
                results.Add(onTemplate);
        }

        return results
            .DistinctBy(r => (r.FilePath, r.Range.Start.Line, r.Range.Start.Column))
            .ToList();
    }

    /// <summary>
    /// A location inside the generated view, expressed on the template.
    ///
    /// Dropped when it will not map, which is the honest answer for a
    /// definition that exists only in the scaffolding.
    /// </summary>
    private static SourceLocation? BackToTemplate(
        LanguageDocument document,
        TemplateGeneration.Generated generated,
        SourceLocation found)
    {
        var generatedText = SourceText.From(generated.Code);
        var line = found.Range.Start.Line - 1;

        if (line < 0 || line >= generatedText.Lines.Count) return null;

        var offset = generatedText.Lines[line].Start +
                     Math.Max(0, found.Range.Start.Column - 1);

        if (generated.Map.ToOriginal(offset) is not { } original) return null;

        var templateText = SourceText.From(document.Text);

        if (original < 0 || original > templateText.Length) return null;

        var at = templateText.Lines.GetLinePosition(original);

        return new SourceLocation(
            document.FilePath,
            SourceRange.At(new SourcePosition(at.Line + 1, at.Character + 1)));
    }

    /// <summary>
    /// Whether a path is the generated view rather than a file of the user's.
    /// </summary>
    private static bool IsGeneratedView(string path) =>
        path.Contains("BasaltGeneratedView", StringComparison.OrdinalIgnoreCase) ||
        path.Contains("BasaltScratch", StringComparison.OrdinalIgnoreCase);

    public async Task<QuickInfo?> GetQuickInfoAsync(
        LanguageDocument document, int position, CancellationToken ct = default)
    {
        // Only the code half has anything to say: hovering a paragraph tag
        // is not a question for the Visual Basic compiler.
        if (_questions.QuickInfo is null || !VbHtmlCompletionProvider.IsInCode(document.Text, position))
            return null;

        var generated = await TemplateGeneration.ForAsync(
            VbHtmlParser.Parse(document.Text), document.FilePath, Host, document.Text, AskCatalog, ct)
            .ConfigureAwait(false);

        var mapped = generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inclusive)
                  ?? generated.Map.ToGenerated(position, document.Text, generated.Code, MappingBehavior.Inferred);

        if (mapped is not { } at) return null;

        return await _questions.QuickInfo(generated.Code, at, ct).ConfigureAwait(false);
    }

    /// <summary>What a node contributes to the outline.</summary>
    private static (string? Name, SymbolKind Kind) Describe(VbHtmlNode node) => node switch
    {
        DirectiveNode directive => ($"@{directive.Name} {directive.Value}".TrimEnd(),
                                    SymbolKind.Constant),
        SectionNode section => ($"@Section {section.Name}", SymbolKind.Namespace),
        FunctionsNode => ("@Functions", SymbolKind.Module),
        StatementNode { IsContinuation: true } => (null, SymbolKind.Class),
        StatementNode => ("@Code", SymbolKind.Method),
        BlockNode block => (block.Opening, SymbolKind.Method),
        _ => (null, SymbolKind.Class)
    };
}
