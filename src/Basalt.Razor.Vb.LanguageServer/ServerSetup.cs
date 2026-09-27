using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
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

            // The solution is opened as soon as the client says where the
            // workspace is. Started rather than awaited: a large solution
            // takes seconds, and an editor that pauses on startup is worse
            // than one that knows less for a moment.
            .OnInitialized(async (server, request, response, ct) =>
            {
                // workspaceFolders first: RootPath and RootUri are both
                // deprecated in the protocol, and Rider sends neither. The
                // server therefore looked for a solution "under (nowhere)"
                // and answered every question from the markup alone — no
                // completion, no hover, and a log line nobody was reading.
                var root =
                    request.WorkspaceFolders?.FirstOrDefault()?.Uri.GetFileSystemPath()
                    ?? request.RootUri?.GetFileSystemPath()
                    ?? request.RootPath;
                var compilation = server.GetRequiredService<ProjectCompilation>();

                compilation.StartLoading(root, ct);

                // Said out loud, in the editor's own log.
                //
                // Whether a solution was found decides everything the server
                // can answer about types, and until now it decided it in
                // silence: completion offered keywords and nothing else, with
                // no way to tell a missing solution from a broken one.
                server.Window.LogInfo($"Basalt: looking for a solution under {root ?? "(nowhere)"}");

                await compilation.Loaded.ConfigureAwait(false);

                server.Window.LogInfo(compilation.IsReady
                    ? "Basalt: the project is loaded; completion knows the model's types"
                    : $"Basalt: no project loaded — {compilation.Problem ?? "no reason given"}. "
                    + "Completion will offer markup and directives only.");
            });
    }
}
