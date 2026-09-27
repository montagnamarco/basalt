using Microsoft.Extensions.DependencyInjection;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.WorkDone;
using OmniSharp.Extensions.LanguageServer.Protocol.Window;

namespace Basalt.Razor.Vb.LanguageServer;

/// <summary>Chooses the client's solution and reports the initial compiler load.</summary>
internal static class ProjectStartup
{
    internal static async Task LoadAsync(
        ILanguageServer server, InitializeParams request, CancellationToken ct)
    {
        var compilation = server.Services.GetRequiredService<ProjectCompilation>();
        var root = request.WorkspaceFolders?.FirstOrDefault()?.Uri.GetFileSystemPath()
            ?? request.RootUri?.GetFileSystemPath()
            ?? request.RootPath;
        if (string.IsNullOrWhiteSpace(root)) root = compilation.WorkingDirectory();
        server.Window.LogInfo($"Basalt: looking for a solution under {root}");

        var candidates = ProjectCompilation.DiscoverTargets(root);
        var solutions = candidates.Where(path =>
            !path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var target = candidates.FirstOrDefault();
        if (solutions.Length > 1)
        {
            // Basic action titles work with older clients too. Extra action
            // properties need a capability and are unnecessary for this choice.
            var actions = solutions.Select(path => new MessageActionItem
            {
                Title = Path.GetRelativePath(root, path)
            }).ToArray();
            try
            {
                var selected = await server.Window.ShowMessageRequest(new ShowMessageRequestParams
                {
                    Type = MessageType.Info,
                    Message = "Choose the solution Basalt should load.",
                    Actions = new Container<MessageActionItem>(actions)
                }, ct).ConfigureAwait(false);
                var index = Array.FindIndex(actions, action => action.Title == selected?.Title);
                if (index < 0)
                {
                    compilation.DeclineLoading("no solution was selected; project loading was canceled");
                    LogResult(server, compilation);
                    return;
                }
                target = solutions[index];
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                compilation.DeclineLoading("solution selection was canceled");
                return;
            }
            catch (Exception ex)
            {
                compilation.DeclineLoading($"the client could not select a solution: {ex.Message}");
                LogResult(server, compilation);
                return;
            }
        }

        IWorkDoneObserver? progress = null;
        try
        {
            // OmniSharp 0.19.9 treats a present boolean as supported even
            // when it is false; Value carries the client's actual opt-in.
            if (target is not null && request.Capabilities?.Window?.WorkDoneProgress.Value == true &&
                server.WorkDoneManager.IsSupported)
            {
                try
                {
                    progress = await server.WorkDoneManager.Create(new WorkDoneProgressBegin
                    {
                        Title = "Loading Basalt project",
                        Message = Path.GetFileName(target),
                        Cancellable = false
                    }, onComplete: () => new WorkDoneProgressEnd
                    {
                        Message = ct.IsCancellationRequested ? "Project loading was canceled."
                            : compilation.IsReady ? $"Loaded {Path.GetFileName(target)}."
                            : $"Project loading failed: {compilation.Problem}"
                    }, cancellationToken: ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    compilation.DeclineLoading("project loading was canceled");
                    return;
                }
                catch (Exception ex)
                {
                    // Progress is optional. A client rejecting its creation
                    // must not stop the selected solution from being loaded.
                    server.Window.LogInfo($"Basalt: project progress is unavailable: {ex.Message}");
                }
            }

            if (target is null) compilation.StartLoading(root, ct);
            else compilation.StartLoadingTarget(target, ct);
            await compilation.Loaded.ConfigureAwait(false);
            LogResult(server, compilation);
        }
        finally
        {
            progress?.Dispose();
        }
    }

    private static void LogResult(ILanguageServer server, ProjectCompilation compilation) =>
        server.Window.LogInfo(compilation.IsReady
            ? "Basalt: the project is loaded; completion knows the model's types"
            : $"Basalt: no project loaded — {compilation.Problem ?? "no reason given"}. "
                + "Completion will offer markup and directives only.");
}
