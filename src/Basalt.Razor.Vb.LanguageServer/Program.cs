using Microsoft.Extensions.DependencyInjection;
using Basalt.Razor.Vb.LanguageServer;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;
using OmniSharp.Extensions.LanguageServer.Server;

// A language server speaks over standard input and output, so nothing else
// may be written there: a stray Console.WriteLine corrupts the protocol and
// the editor reports the server as crashed.

var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options => options
    .WithInput(Console.OpenStandardInput())
    .WithOutput(Console.OpenStandardOutput())
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

    // Structure and folding: answerable from the parse tree alone, so they
    // work even before the solution has finished loading.
    .WithHandler<VbHtmlDocumentHighlightHandler>()
    .WithHandler<VbHtmlLinkedEditingRangeHandler>()
    .WithHandler<VbHtmlDocumentSymbolHandler>()
    .WithHandler<VbHtmlFoldingRangeHandler>()
    .WithHandler<VbHtmlWatchedFilesHandler>()

    // The solution is opened as soon as the client says where the workspace
    // is. Started rather than awaited: a large solution takes seconds, and an
    // editor that pauses on startup is worse than one that knows less for a
    // moment.
    .OnInitialized(async (server, request, response, ct) =>
    {
        // workspaceFolders first: RootPath and RootUri are both deprecated in
        // the protocol, and Rider sends neither. The server therefore looked
        // for a solution "under (nowhere)" and answered every question from
        // the markup alone — no completion, no hover, and a log line nobody
        // was reading.
        var root =
            request.WorkspaceFolders?.FirstOrDefault()?.Uri.GetFileSystemPath()
            ?? request.RootUri?.GetFileSystemPath()
            ?? request.RootPath;
        var compilation = server.GetRequiredService<ProjectCompilation>();

        compilation.StartLoading(root, ct);

        // Said out loud, in the editor's own log.
        //
        // Whether a solution was found decides everything the server can
        // answer about types, and until now it decided it in silence:
        // completion offered keywords and nothing else, with no way to tell
        // a missing solution from a broken one.
        server.Window.LogInfo($"Basalt: looking for a solution under {root ?? "(nowhere)"}");

        await compilation.Loaded.ConfigureAwait(false);

        server.Window.LogInfo(compilation.IsReady
            ? "Basalt: the project is loaded; completion knows the model's types"
            : $"Basalt: no project loaded — {compilation.Problem ?? "no reason given"}. "
            + "Completion will offer markup and directives only.");
    }));

await server.WaitForExit;
