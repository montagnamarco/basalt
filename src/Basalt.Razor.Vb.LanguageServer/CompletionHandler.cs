using Basalt.Razor.Vb;

// Aliased rather than imported: CompletionItem is a name both the protocol
// and the extensibility model use, for the two sides of the same idea.
using LanguageDocument = Basalt.Extensibility.LanguageDocument;
using IdeCompletionItem = Basalt.Extensibility.CompletionItem;
using IdeSymbolKind = Basalt.Extensibility.SymbolKind;
using Basalt.Razor.Vb.Web;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Newtonsoft.Json.Linq;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// What can be written at the caret in a view.
///
/// Three things, depending on where the caret is: a Razor directive at the
/// start of a line, a member of the model after "Model.", or an HTML tag
/// inside markup.
/// </summary>
public sealed class VbHtmlCompletionHandler : CompletionHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly ProjectCompilation _compilation;
    private readonly object _resolveLock = new();
    private readonly Dictionary<string, CompletionBatch> _resolveBatches = new();
    private const int MaximumBatches = 8;
    private const int MaximumItems = 2048;
    private static readonly TimeSpan ResolveLifetime = TimeSpan.FromMinutes(2);

    private sealed record CompletionBatch(
        OpenDocument Document, long Revision, DateTime Created,
        ProjectCompilation.CompletionCapture Capture, IReadOnlyList<IdeCompletionItem> Items);

    public VbHtmlCompletionHandler(DocumentStore documents, ProjectCompilation compilation)
    {
        _documents = documents;
        _compilation = compilation;
    }

    public override async Task<CompletionItem> Handle(
        CompletionItem request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // Only tokens issued by this server can select a stored snapshot. Never
        // trust file paths, positions or generated text supplied by the client.
        if (request.Data is not JObject data ||
            data["batch"]?.Type != JTokenType.String ||
            data["index"]?.Type != JTokenType.Integer)
            return request;

        if (!long.TryParse(data["index"]!.ToString(), out var indexValue) ||
            indexValue < 0 || indexValue >= MaximumItems) return request;
        var index = (int)indexValue;
        CompletionBatch? batch;
        lock (_resolveLock)
        {
            PruneBatches();
            _resolveBatches.TryGetValue(data["batch"]!.Value<string>()!, out batch);
        }
        if (batch is null || index >= batch.Items.Count || !IsCurrent(batch)) return request;
        var item = batch.Items[index];
        if (request.Label != item.DisplayText || request.InsertText != item.InsertionText)
            return request;

        var description = await _compilation
            .GetCompletionDescriptionAsync(batch.Capture, item.DisplayText, ct)
            .ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        if (!IsCurrent(batch) || string.IsNullOrEmpty(description)) return request;

        // Roslyn supplies the signature first, followed by documentation. The
        // rest of the client's item (edits, sort/filter text, data) survives.
        return request with
        {
            Detail = description.Split('\n', 2)[0].TrimEnd('\r'),
            Documentation = description
        };
    }

    private bool IsCurrent(CompletionBatch batch) =>
        _compilation.IsReady && _compilation.Revision == batch.Revision &&
        ReferenceEquals(_documents.Get(batch.Document.Uri), batch.Document) &&
        DateTime.UtcNow - batch.Created < ResolveLifetime;

    private void PruneBatches()
    {
        foreach (var pair in _resolveBatches.ToArray())
            if (!IsCurrent(pair.Value)) _resolveBatches.Remove(pair.Key);
    }

    public override async Task<CompletionList> Handle(
        CompletionParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return new CompletionList();

        var offset = OffsetOf(document.Text, request.Position);

        // Roslyn answers a typed character differently from an explicit
        // request: after "= New " it preselects the declared type.
        var typed = request.Context is { TriggerKind: CompletionTriggerKind.TriggerCharacter } context &&
                    context.TriggerCharacter is { Length: 1 } character
            ? character[0]
            : (char?)null;

        // With a compilation the code half is answered properly: the view is
        // generated to Visual Basic and Roslyn is asked about the generated
        // position, which is how Basalt answers the same question.
        if (_compilation.IsReady && _compilation.Provider.Completion is not null)
        {
            var revision = _compilation.Revision;

            var (answered, capture) = await _compilation
                .GetCompletionsAsync(
                    new LanguageDocument(
                        request.TextDocument.Uri.GetFileSystemPath(), document.Text),
                    offset, ct, typed)
                .ConfigureAwait(false);

            if (answered.Count > 0)
            {
                var translated = answered.Select(Translate).ToArray();
                if (capture.GeneratedText is not null &&
                    revision == _compilation.Revision &&
                    ReferenceEquals(_documents.Get(document.Uri), document))
                {
                    var id = Guid.NewGuid().ToString("N");
                    var retained = answered.Take(MaximumItems).ToArray();
                    lock (_resolveLock)
                    {
                        PruneBatches();
                        while (_resolveBatches.Count >= MaximumBatches)
                            _resolveBatches.Remove(_resolveBatches.MinBy(pair => pair.Value.Created).Key);
                        _resolveBatches[id] = new CompletionBatch(
                            document, revision, DateTime.UtcNow, capture, retained);
                    }
                    for (var index = 0; index < retained.Length; index++)
                        translated[index] = translated[index] with
                        {
                            Data = new JObject { ["batch"] = id, ["index"] = index }
                        };
                }
                return new CompletionList(translated);
            }
        }

        // A space in Visual Basic that Roslyn had nothing for gets nothing:
        // the markup reading below would take "a < b " for a tag and offer
        // attributes.
        if (typed == ' ' && (VbHtmlCodeRegions.IsInCode(document.Text, offset) ||
                             VbHtmlCodeRegions.IsAfterStatementBody(document.Text, offset)))
            return new CompletionList();

        return new CompletionList(Suggest(document, offset));
    }

    /// <summary>An item as the protocol wants it.</summary>
    private static CompletionItem Translate(IdeCompletionItem item) =>
        new()
        {
            Label = item.DisplayText,
            InsertText = item.InsertionText,
            Detail = item.Detail,
            FilterText = item.FilterText,
            Documentation = item.Description,
            Kind = KindOf(item.Kind),
            Preselect = item.IsPreselected,
        };

    private static CompletionItemKind KindOf(IdeSymbolKind kind) => kind switch
    {
        IdeSymbolKind.Method => CompletionItemKind.Method,
        IdeSymbolKind.Property => CompletionItemKind.Property,
        IdeSymbolKind.Field => CompletionItemKind.Field,
        IdeSymbolKind.Class => CompletionItemKind.Class,
        IdeSymbolKind.Structure => CompletionItemKind.Struct,
        IdeSymbolKind.Interface => CompletionItemKind.Interface,
        IdeSymbolKind.Enum => CompletionItemKind.Enum,
        IdeSymbolKind.Module => CompletionItemKind.Module,
        IdeSymbolKind.Namespace => CompletionItemKind.Module,
        IdeSymbolKind.Keyword => CompletionItemKind.Keyword,
        IdeSymbolKind.Variable => CompletionItemKind.Variable,
        IdeSymbolKind.Parameter => CompletionItemKind.Variable,
        IdeSymbolKind.Event => CompletionItemKind.Event,
        _ => CompletionItemKind.Text,
    };

    /// <summary>What to offer at an offset, for tests as well as the editor.</summary>
    internal static IReadOnlyList<CompletionItem> Suggest(OpenDocument document, int offset)
    {
        var before = document.Text[..Math.Clamp(offset, 0, document.Text.Length)];

        if (before.EndsWith("Model.", StringComparison.Ordinal))
            return ModelMembers(document.Parsed);

        // A directive follows an "@" that begins a line.
        var lineStart = before.LastIndexOf('\n') + 1;
        var line = before[lineStart..];

        if (line.TrimStart().StartsWith('@') && !line.Contains(' '))
            return Directives(document);

        return Markup(document.Text, offset);
    }

    /// <summary>
    /// What the markup half offers, read from where the caret is.
    ///
    /// The same vocabulary the IDE uses, from the shared library: a second
    /// list would drift, and a tag would complete in one editor and not the
    /// other. It used to be 27 names offered anywhere at all — inside running
    /// text, inside an attribute value, everywhere.
    /// </summary>
    private static IReadOnlyList<CompletionItem> Markup(string text, int offset)
    {
        var context = HtmlContextReader.At(text, offset);

        var items = context.Kind switch
        {
            HtmlContextKind.ElementName =>
                HtmlLanguage.Elements.Select(name => Item(
                    name,
                    CompletionItemKind.Property,
                    HtmlLanguage.VoidElements.Contains(name)
                        ? "Element without a closing tag."
                        : "Element.",
                    name)),

            HtmlContextKind.AttributeName =>
                HtmlLanguage.AttributesFor(context.Element).Select(name => Item(
                    name, CompletionItemKind.Property, "Attribute.", name)),

            HtmlContextKind.AttributeValue =>
                HtmlLanguage.ValuesFor(context.Element, context.Attribute).Select(value =>
                    Item(value, CompletionItemKind.Value, "Value.", value)),

            // In content only a new element makes sense, and only once "<" is
            // typed: the whole vocabulary offered into running text is noise.
            _ => []
        };

        return [.. items];
    }

    /// <summary>
    /// The directives a file of this kind takes, from the table kept beside
    /// the parser: the server offered seven from a list of its own.
    /// </summary>
    private static IReadOnlyList<CompletionItem> Directives(OpenDocument document) =>
    [
        .. VbHtmlDirectives.For(document.Uri).Select(directive =>
            Item(directive.Name, CompletionItemKind.Keyword, directive.Description, directive.Insertion))
    ];

    /// <summary>
    /// The members the model offers.
    ///
    /// Without a compilation to ask, the type's name is all that is known, so
    /// the view declares what it holds and the editor says so rather than
    /// guessing at members it cannot see.
    /// </summary>
    private static IReadOnlyList<CompletionItem> ModelMembers(VbHtmlDocument parsed)
    {
        if (parsed.ModelType is not { Length: > 0 } modelType)
        {
            return
            [
                Item("(no model declared)", CompletionItemKind.Text,
                     "Add @ModelType to say what this view is given.", "")
            ];
        }

        // Reached only while the solution is still loading, or when there is
        // no solution to load: with a compilation the caller answers from
        // Roslyn and never gets here. Three members of System.Object used to
        // be offered as though they were the model's own — saying what is
        // missing beats a list that looks right and is not.
        return
        [
            Item($"({modelType})", CompletionItemKind.Text,
                 "The members of this type will be offered once the project has "
               + "finished loading.",
                 "")
        ];
    }


    private static CompletionItem Item(
        string label, CompletionItemKind kind, string detail, string insert) =>
        new()
        {
            Label = label,
            Kind = kind,
            Detail = detail,
            InsertText = insert.Length > 0 ? insert : label
        };

    /// <summary>Turns a line and character into an offset in the text.</summary>
    internal static int OffsetOf(string text, Position position)
    {
        var offset = 0;
        var line = 0;

        while (line < position.Line && offset < text.Length)
        {
            var next = text.IndexOf('\n', offset);
            if (next < 0) return text.Length;

            offset = next + 1;
            line++;
        }

        return Math.Min(offset + position.Character, text.Length);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability, ClientCapabilities clientCapabilities) =>
        new()
        {
            DocumentSelector = Selector.ForVbHtml,
            ResolveProvider = true,

            // "@" opens the directives, "." the members, "<" the tags, and a
            // space in code the types after As and New, as in Visual Studio.
            // A space where nothing can follow gets an empty list, which the
            // client does not show.
            TriggerCharacters = new Container<string>("@", ".", "<", " ")
        };
}
