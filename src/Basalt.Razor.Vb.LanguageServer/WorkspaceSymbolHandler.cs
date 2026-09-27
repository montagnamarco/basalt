using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;
using IdeSymbolKind = Basalt.Extensibility.SymbolKind;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// workspace/symbol: the solution's declarations by name, for an editor's
/// "go to symbol in workspace".
/// </summary>
/// <remarks>
/// The model a view names, its controller, a method a page calls: the server
/// loads the whole solution to answer questions about views, and answered
/// none about finding a declaration by its name.
/// </remarks>
public sealed class VbHtmlWorkspaceSymbolHandler : WorkspaceSymbolsHandlerBase
{
    private readonly ProjectCompilation _compilation;

    public VbHtmlWorkspaceSymbolHandler(ProjectCompilation compilation) => _compilation = compilation;

    public override async Task<Container<WorkspaceSymbol>?> Handle(WorkspaceSymbolParams request, CancellationToken ct)
    {
        var found = await _compilation.SearchSymbolsAsync(request.Query, ct).ConfigureAwait(false);

        return new Container<WorkspaceSymbol>(found.Select(symbol => new WorkspaceSymbol
        {
            Name = symbol.Name,
            Kind = KindOf(symbol.Kind),
            ContainerName = symbol.Container,
            Location = new LocationOrFileLocation(VbHtmlReferencesHandler.Translate(symbol.Location))
        }));
    }

    private static SymbolKind KindOf(IdeSymbolKind kind) => kind switch
    {
        IdeSymbolKind.Namespace => SymbolKind.Namespace,
        IdeSymbolKind.Class or IdeSymbolKind.Module => SymbolKind.Class,
        IdeSymbolKind.Structure => SymbolKind.Struct,
        IdeSymbolKind.Interface => SymbolKind.Interface,
        IdeSymbolKind.Enum => SymbolKind.Enum,
        IdeSymbolKind.Method or IdeSymbolKind.Function => SymbolKind.Method,
        IdeSymbolKind.Property => SymbolKind.Property,
        IdeSymbolKind.Field => SymbolKind.Field,
        IdeSymbolKind.Event => SymbolKind.Event,
        _ => SymbolKind.Variable
    };

    protected override WorkspaceSymbolRegistrationOptions CreateRegistrationOptions(
        WorkspaceSymbolCapability capability, ClientCapabilities clientCapabilities) => new();
}
