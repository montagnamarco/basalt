using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>
/// Everything the server offers, apart from where its bytes come from.
/// </summary>
/// <remarks>
/// Program.cs used to build this inline over standard input and output, which
/// meant the only way to test it was to call a handler class directly —
/// proving that the handler answers correctly, never that the JSON-RPC
/// framing, the capability exchange or the position encoding around it are
/// right. Pulled out here, a test can hand the very same configuration a pair
/// of in-memory streams instead and hold a real protocol conversation with it.
/// </remarks>
public static class ServerSetup
{
    /// <summary>Registers the services, handlers and startup behaviour.</summary>
    /// <param name="options">The server being built, not yet started.</param>
    /// <param name="input">Where the server reads requests from.</param>
    /// <param name="output">Where the server writes responses to.</param>
    public static void Configure(LanguageServerOptions options, Stream input, Stream output)
    {
        InitializeParams startupRequest = null!;
        options
            .WithInput(input)
            .WithOutput(output)
            .WithServices(services => services
                .AddSingleton<DocumentStore>()
                .AddSingleton<ProjectCompilation>())
            .WithHandler<VbHtmlTextDocumentHandler>()
            .WithHandler<VbHtmlCompletionHandler>()
            .WithHandler<VbHtmlSemanticTokensHandler>()
            .WithHandler<VbHtmlDefinitionHandler>()
            .WithHandler<VbHtmlSignatureHelpHandler>()
            .WithHandler<VbHtmlHoverHandler>()
            .WithHandler<VbHtmlFormattingHandler>()
            .WithHandler<VbHtmlRangeFormattingHandler>()
            .WithHandler<VbHtmlOnTypeFormattingHandler>()
            .WithHandler<VbHtmlReferencesHandler>()

            // Structure and folding: answerable from the parse tree alone, so
            // they work even before the solution has finished loading.
            .WithHandler<VbHtmlDocumentHighlightHandler>()
            .WithHandler<VbHtmlLinkedEditingRangeHandler>()
            .WithHandler<VbHtmlDocumentSymbolHandler>()
            .WithHandler<VbHtmlFoldingRangeHandler>()
            .WithHandler<VbHtmlWatchedFilesHandler>()

            // Startup owns the client's solution choice and loading progress.
            // Handlers can answer from markup until Roslyn is ready.
            .OnInitialized((server, request, response, ct) =>
            {
                startupRequest = request;
                return Task.CompletedTask;
            })
            // OmniSharp's OnInitialized runs before the initialize response.
            // Progress/create is only legal after the client's initialized
            // notification, which is when OnStarted runs.
            .OnStarted((server, ct) => ProjectStartup.LoadAsync(server, startupRequest, ct));
    }
}
