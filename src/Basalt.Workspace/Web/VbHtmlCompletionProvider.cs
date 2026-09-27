using Basalt.Extensibility;
using Basalt.Razor.Vb;
using Basalt.Razor.Vb.Web;

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
        {
            var markup = await _html.GetCompletionsAsync(document, position, ct).ConfigureAwait(false);
            var parameters = await ComponentParametersAsync(document, position, ct).ConfigureAwait(false);
            var directives = DirectiveAttributes(document, position);
            var tagHelpers = await TagHelperAttributesAsync(document, position, ct).ConfigureAwait(false);
            var routes = await RouteValuesAsync(document, position, ct).ConfigureAwait(false);

            return parameters.Count == 0 && directives.Count == 0 && tagHelpers.Count == 0 && routes.Count == 0
                ? markup
                : [.. routes, .. parameters, .. directives, .. tagHelpers, .. markup];
        }

        if (_ask is null) return [];

        var generated = await TemplateGeneration.ForAsync(
            document.FilePath, Host, document.Text, AskCatalog, ct).ConfigureAwait(false);

        if (CaretInGenerated(document.FilePath, document.Text, generated, position) is not { } at)
            return [];

        var (code, caret) = WithSpacesBefore(generated.Code, at, spaces);

        return await _ask(code, caret, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// In a component's markup: the components in scope, where a tag's name
    /// is being written, and a component's parameters, where an attribute is
    /// being written in its tag: "&lt;Counter " offers Step and IncrementBy, and "@bind-Value"
    /// where the component has Value and ValueChanged.
    /// </summary>
    /// <remarks>
    /// From the catalog the build uses to write the tag, so what is offered
    /// is what compiles. Only in a component: a view's tags are HTML and
    /// tag helpers.
    /// </remarks>
    private async Task<IReadOnlyList<CompletionItem>> ComponentParametersAsync(
        LanguageDocument document, int position, CancellationToken ct)
    {
        if (AskCatalog is null || !TemplateGeneration.IsComponent(document.FilePath)) return [];

        var context = HtmlContextReader.At(document.Text, position);

        // "<Co": the components in scope, before HTML's own elements.
        if (context.Kind == HtmlContextKind.ElementName)
        {
            var listing = await AskCatalog(document.FilePath, document.Text, ct).ConfigureAwait(false) as IComponentListing;

            return listing is null
                ? []
                : [.. listing.ComponentNames().Select(name => new CompletionItem(name, name, SymbolKind.Class)
                    {
                        Description = "Component."
                    })];
        }

        if (context.Kind != HtmlContextKind.AttributeName || context.Element is not { Length: > 0 } element ||
            !char.IsUpper(element[0]))
            return [];

        var catalog = await AskCatalog(document.FilePath, document.Text, ct).ConfigureAwait(false);

        if (catalog?.Find(element, -1) is not { } shape) return [];

        var names = new HashSet<string>(shape.Parameters.Select(parameter => parameter.Name), StringComparer.OrdinalIgnoreCase);
        var items = new List<CompletionItem>();

        foreach (var parameter in shape.Parameters)
        {
            items.Add(new CompletionItem(parameter.Name, parameter.Name, SymbolKind.Property)
            {
                Detail = parameter.TypeName,
                Description = $"Parameter of {element}."
            });

            if (names.Contains(parameter.Name + "Changed"))
            {
                items.Add(new CompletionItem("@bind-" + parameter.Name, "@bind-" + parameter.Name, SymbolKind.Property)
                {
                    Detail = parameter.TypeName,
                    Description = $"Binds {parameter.Name} both ways, through {parameter.Name}Changed.",
                    FilterText = "bind-" + parameter.Name
                });
            }
        }

        return items;
    }

    /// <summary>
    /// The tag helpers a view's project offers, for its asp-* attributes; the
    /// view's @addTagHelper lines decide which are in scope.
    /// </summary>
    public Func<string, CancellationToken, Task<TagHelperCatalog?>>? AskTagHelpers { get; init; }

    /// <summary>
    /// The attributes of the tag helpers that can take an element, where an
    /// attribute is being written in a view: "&lt;a " offers asp-action,
    /// asp-controller and asp-route-.
    /// </summary>
    /// <remarks>
    /// Scoped as the build scopes them, by the view's @addTagHelper lines and
    /// every _ViewImports above it. A tag helper is offered by the element
    /// its rules name, whatever attributes they also require: those are the
    /// very attributes being chosen.
    /// </remarks>
    private async Task<IReadOnlyList<CompletionItem>> TagHelperAttributesAsync(
        LanguageDocument document, int position, CancellationToken ct)
    {
        if (AskTagHelpers is null || TemplateGeneration.IsComponent(document.FilePath) ||
            TemplateGeneration.IsPage(document.FilePath))
            return [];

        var context = HtmlContextReader.At(document.Text, position);

        if (context.Kind != HtmlContextKind.AttributeName || context.Element is not { Length: > 0 } element) return [];

        if (await AskTagHelpers(document.FilePath, ct).ConfigureAwait(false) is not { } all) return [];

        var view = VbHtmlParser.Parse(document.Text);
        TemplateGeneration.ApplySharedFiles(view, document.FilePath, "_ViewImports.vbhtml");

        var scoped = all.Scoped(view.TagHelperDirectives);
        var items = new Dictionary<string, CompletionItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var descriptor in scoped.Descriptors)
        {
            if (!descriptor.Rules.Any(rule =>
                    rule.TagName == "*" || string.Equals(scoped.Prefix + rule.TagName, element, StringComparison.OrdinalIgnoreCase)))
                continue;

            foreach (var property in descriptor.Properties)
            {
                var name = property.DictionaryPrefix is { Length: > 0 } prefix ? prefix : property.AttributeName;

                if (string.IsNullOrEmpty(name) || items.ContainsKey(name)) continue;

                items[name] = new CompletionItem(name, name, SymbolKind.Property)
                {
                    Detail = property.TypeName.Replace("Global.", ""),
                    Description = $"Tag helper {descriptor.TypeName.Replace("Global.", "")}."
                };
            }
        }

        return [.. items.Values.OrderBy(item => item.DisplayText, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>The controllers, actions and pages a view's asp-* values can name.</summary>
    public Func<string, CancellationToken, Task<RouteCatalog?>>? AskRoutes { get; init; }

    /// <summary>
    /// The project's controllers inside asp-controller="", the controller's
    /// actions inside asp-action="", and its Razor Pages inside asp-page="".
    /// </summary>
    /// <remarks>
    /// The controller asp-action refers to is the one the same tag names, or
    /// else the view's own, by its folder under Views, as MVC takes it.
    /// </remarks>
    private async Task<IReadOnlyList<CompletionItem>> RouteValuesAsync(
        LanguageDocument document, int position, CancellationToken ct)
    {
        if (AskRoutes is null || TemplateGeneration.IsComponent(document.FilePath) ||
            TemplateGeneration.IsPage(document.FilePath))
            return [];

        var context = HtmlContextReader.At(document.Text, position);
        if (context.Kind != HtmlContextKind.AttributeValue) return [];

        var attribute = context.Attribute.ToLowerInvariant();
        if (attribute is not ("asp-controller" or "asp-action" or "asp-page")) return [];

        if (await AskRoutes(document.FilePath, ct).ConfigureAwait(false) is not { } routes) return [];

        IEnumerable<(string Name, SymbolKind Kind)> values = attribute switch
        {
            "asp-controller" => routes.Actions.Keys.Select(name => (name, SymbolKind.Class)),
            "asp-page" => routes.Pages.Select(page => (page, SymbolKind.File)),
            _ => ControllerOf(document, position) is { } controller && routes.Actions.TryGetValue(controller, out var actions)
                ? actions.Select(action => (action, SymbolKind.Method))
                : []
        };

        return [.. values.OrderBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .Select(value => new CompletionItem(value.Name, value.Name, value.Kind))];
    }

    /// <summary>The controller an asp-action is for: the tag's asp-controller, or the view's folder.</summary>
    private static string? ControllerOf(LanguageDocument document, int position)
    {
        var text = document.Text;
        var tagStart = text.LastIndexOf('<', Math.Max(0, Math.Min(position, text.Length) - 1));
        var tagEnd = text.IndexOf('>', Math.Max(tagStart, 0));
        if (tagEnd < 0) tagEnd = text.Length;

        if (tagStart >= 0)
        {
            var tag = text[tagStart..tagEnd];
            var at = tag.IndexOf("asp-controller=\"", StringComparison.OrdinalIgnoreCase);

            if (at >= 0)
            {
                var start = at + "asp-controller=\"".Length;
                var end = tag.IndexOf('"', start);

                if (end > start) return tag[start..end];
            }
        }

        var folder = Path.GetDirectoryName(document.FilePath);
        var parent = folder is null ? null : Path.GetDirectoryName(folder);

        return parent is not null && string.Equals(Path.GetFileName(parent), "Views", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileName(folder)
            : null;
    }

    /// <summary>
    /// Blazor's directive attributes, where an attribute is being written in
    /// a component's markup: "@onclick", "@bind" and the rest on an element,
    /// "@ref", "@key" and "@attributes" on a component, whose events are its
    /// own parameters.
    /// </summary>
    private static IReadOnlyList<CompletionItem> DirectiveAttributes(LanguageDocument document, int position)
    {
        if (!TemplateGeneration.IsComponent(document.FilePath)) return [];

        var context = HtmlContextReader.At(document.Text, position);

        if (context.Kind != HtmlContextKind.AttributeName || context.Element is not { Length: > 0 } element) return [];

        var onComponent = char.IsUpper(element[0]);

        return
        [
            .. VbHtmlDirectives.Attributes
                .Where(attribute => !onComponent || attribute.Name is "@ref" or "@key" or "@attributes")
                .Select(attribute => new CompletionItem(attribute.Name, attribute.Name, SymbolKind.Keyword)
                {
                    Description = attribute.Description,
                    FilterText = attribute.Name[1..]
                })
        ];
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
