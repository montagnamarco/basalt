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

    public VbHtmlCompletionHandler(DocumentStore documents, ProjectCompilation compilation)
    {
        _documents = documents;
        _compilation = compilation;
    }

    public override Task<CompletionItem> Handle(
        CompletionItem request, CancellationToken ct) => Task.FromResult(request);

    public override async Task<CompletionList> Handle(
        CompletionParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return new CompletionList();

        var offset = OffsetOf(document.Text, request.Position);

        // With a compilation the code half is answered properly: the view is
        // generated to Visual Basic and Roslyn is asked about the generated
        // position, which is how Basalt answers the same question.
        if (_compilation.IsReady && _compilation.Provider.Completion is { } completion)
        {
            var answered = await completion
                .GetCompletionsAsync(
                    new LanguageDocument(
                        request.TextDocument.Uri.GetFileSystemPath(), document.Text),
                    offset, ct)
                .ConfigureAwait(false);

            if (answered.Count > 0) return new CompletionList(answered.Select(Translate));
        }

        return new CompletionList(Suggest(document, offset));
    }

    /// <summary>An item as the protocol wants it.</summary>
    private static CompletionItem Translate(IdeCompletionItem item) =>
        new()
        {
            Label = item.DisplayText,
            InsertText = item.InsertionText,
            Detail = item.Detail,
            Documentation = item.Description,
            Kind = KindOf(item.Kind),
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
            return Directives();

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

    private static IReadOnlyList<CompletionItem> Directives() =>
    [
        Item("ModelType", CompletionItemKind.Keyword,
             "Declares the type of the view's model.", "ModelType "),
        Item("Imports", CompletionItemKind.Keyword,
             "Imports a namespace into the view.", "Imports "),
        Item("Code", CompletionItemKind.Keyword,
             "A block of Visual Basic that produces no output.", "Code\n    \nEnd Code"),
        Item("If", CompletionItemKind.Keyword, "Conditional markup.", "If  Then\n\nEnd If"),
        Item("For Each", CompletionItemKind.Keyword,
             "Repeats markup for each item.", "For Each item In \n\nNext"),
        Item("While", CompletionItemKind.Keyword, "Repeats while a condition holds.",
             "While \n\nEnd While"),
        Item("Select Case", CompletionItemKind.Keyword, "Chooses between cases.",
             "Select Case \n    Case \nEnd Select")
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

            // "@" opens the directives, "." the members, "<" the tags.
            TriggerCharacters = new Container<string>("@", ".", "<")
        };
}
