using Basalt.Razor.Vb;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// The other places the name under the caret appears.
///
/// Answerable from the parse tree alone, like structure and folding: a name
/// written in a Code block and used in an expression is the same name, and
/// saying so does not need a compilation. What it does need is to stay out
/// of the markup — the word "name" in a paragraph is prose, not a symbol.
/// </summary>
public sealed class VbHtmlDocumentHighlightHandler : DocumentHighlightHandlerBase
{
    private readonly DocumentStore _documents;

    public VbHtmlDocumentHighlightHandler(DocumentStore documents) => _documents = documents;

    public override Task<DocumentHighlightContainer?> Handle(
        DocumentHighlightParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return Task.FromResult<DocumentHighlightContainer?>(null);

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        return Task.FromResult(Highlights(document.Text, offset));
    }

    /// <summary>Every occurrence of the name at a position, in code only.</summary>
    internal static DocumentHighlightContainer? Highlights(string text, int offset)
    {
        var caret = Math.Clamp(offset, 0, text.Length);

        // Highlighting a word in the markup would light up prose.
        if (!VbHtmlCodeRegions.IsInCode(text, caret)) return null;

        var name = VbHtmlDefinitionHandler.WordAt(text, caret);

        if (name.Length == 0) return null;

        var found = new List<DocumentHighlight>();

        for (var index = text.IndexOf(name, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(name, index + 1, StringComparison.Ordinal))
        {
            if (!IsWholeWord(text, index, name.Length)) continue;

            // Only where it is code: the same letters in a paragraph are not
            // this symbol.
            if (!VbHtmlCodeRegions.IsInCode(text, index)) continue;

            var (line, character) = VbHtmlSemanticTokensHandler.LineAndCharacterOf(text, index);

            found.Add(new DocumentHighlight
            {
                Kind = DocumentHighlightKind.Text,
                Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                    new Position(line, character),
                    new Position(line, character + name.Length))
            });
        }

        return found.Count == 0 ? null : new DocumentHighlightContainer(found);
    }

    private static bool IsWholeWord(string text, int start, int length)
    {
        if (start > 0 && VbHtmlDefinitionHandler.IsWordCharacter(text[start - 1]))
            return false;

        var after = start + length;

        return after >= text.Length
            || !VbHtmlDefinitionHandler.IsWordCharacter(text[after]);
    }

    protected override DocumentHighlightRegistrationOptions CreateRegistrationOptions(
        DocumentHighlightCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
