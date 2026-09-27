using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

using LanguageDocument = Basalt.Extensibility.LanguageDocument;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Everywhere a name is used, views included.
/// </summary>
/// <remarks>
/// Through the compilation, so a property renamed in a .vb file can be found
/// from the view that reads it — and the other way round. Without it, "find
/// usages" on a model property silently misses every view, which is worse
/// than not offering the command at all.
/// </remarks>
public sealed class VbHtmlReferencesHandler : ReferencesHandlerBase
{
    private readonly DocumentStore _documents;
    private readonly ProjectCompilation _compilation;

    public VbHtmlReferencesHandler(DocumentStore documents, ProjectCompilation compilation)
    {
        _documents = documents;
        _compilation = compilation;
    }

    public override async Task<LocationContainer?> Handle(
        ReferenceParams request, CancellationToken ct)
    {
        var document = _documents.Get(request.TextDocument.Uri.ToString());

        if (document is null) return null;
        if (_compilation.Provider.Navigation is not { } navigation) return null;

        var offset = VbHtmlCompletionHandler.OffsetOf(document.Text, request.Position);

        var found = await navigation
            .FindReferencesAsync(
                new LanguageDocument(
                    request.TextDocument.Uri.GetFileSystemPath(), document.Text),
                offset, ct)
            .ConfigureAwait(false);

        if (found.Count == 0) return null;

        return new LocationContainer(found.Select(Translate));
    }

    internal static Location Translate(Basalt.Extensibility.SourceLocation location) =>
        new()
        {
            Uri = DocumentUri.FromFileSystemPath(location.FilePath),

            // One-based in the model, zero-based in the protocol.
            Range = new OmniSharp.Extensions.LanguageServer.Protocol.Models.Range(
                new Position(
                    Math.Max(0, location.Range.Start.Line - 1),
                    Math.Max(0, location.Range.Start.Column - 1)),
                new Position(
                    Math.Max(0, location.Range.End.Line - 1),
                    Math.Max(0, location.Range.End.Column - 1))),
        };

    protected override ReferenceRegistrationOptions CreateRegistrationOptions(
        ReferenceCapability capability, ClientCapabilities clientCapabilities) =>
        new() { DocumentSelector = Selector.ForVbHtml };
}
