using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Keeps the compilation current as files change on disk.
/// </summary>
/// <remarks>
/// The model a view is typed on lives in a .vb file, and the server answers
/// about its members from a compilation of the project. Without this the
/// answers describe the class as it was when the solution opened — which is
/// worse than no answer, because it looks current.
/// </remarks>
public sealed class VbHtmlWatchedFilesHandler : IDidChangeWatchedFilesHandler
{
    private readonly ProjectCompilation _compilation;

    public VbHtmlWatchedFilesHandler(ProjectCompilation compilation) =>
        _compilation = compilation;

    public async Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken ct)
    {
        foreach (var change in request.Changes)
        {
            await _compilation
                .FileChangedAsync(change.Uri.GetFileSystemPath(), ct)
                .ConfigureAwait(false);
        }

        return Unit.Value;
    }

    public DidChangeWatchedFilesRegistrationOptions GetRegistrationOptions(
        DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities) =>
        new();
}
