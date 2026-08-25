using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LanguageDocument = Basalt.Extensibility.LanguageDocument;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// What a name means, shown where the pointer rests.
/// </summary>
/// <remarks>
/// The signature and the documentation come from the compilation, through the
/// same delegation that answers completion: the view is generated to Visual
/// Basic and Roslyn is asked about the generated position.
///
/// Markup gets no hover at all. A tag is what it says it is, and a card
/// reading "p" over a paragraph is noise a reader has to dismiss.
/// </remarks>
public sealed class VbHtmlHoverHandler : HoverHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly ProjectCompilation _compilation;

    public VbHtmlHoverHandler(DocumentStore documents, ProjectCompilation compilation)
    {
        _documents = documents;
        _compilation = compilation;
    }

    public override async Task<Hover?> Handle(HoverParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return null;
        if (_compilation.Provider.Navigation is not { } navigation) return null;

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        var info = await navigation
            .GetQuickInfoAsync(
                new LanguageDocument(
                    request.TextDocument.Uri.GetFileSystemPath(), document.Text),
                offset, ct)
            .ConfigureAwait(false);

        if (info is null) return null;

        // Markdown rather than plain text: the signature reads as code and the
        // prose reads as prose, which is the whole reason to show both.
        var content = info.Documentation is { Length: > 0 } documentation
            ? $"```vb\n{info.Signature}\n```\n\n{documentation}"
            : $"```vb\n{info.Signature}\n```";

        return new Hover
        {
            Contents = new MarkedStringsOrMarkupContent(
                new MarkupContent { Kind = MarkupKind.Markdown, Value = content }),
        };
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(
        HoverCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
