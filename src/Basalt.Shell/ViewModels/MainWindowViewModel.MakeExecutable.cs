using Basalt.Core.Localization;
using Basalt.Core.Model;
using Basalt.Core.Settings;
using Basalt.Workspace.Publishing;

namespace Basalt.Shell.ViewModels;

/// <summary>What the user last chose in "Make executable", kept per solution.</summary>
public sealed record MakeExecutableChoice(IReadOnlyList<string> Rids, string? OutputRoot);

/// <summary>"File > Make executable": publishing the startup project as a program.</summary>
public sealed partial class MainWindowViewModel
{
    private Basalt.Workspace.Publishing.PublishService? _publishService;

    /// <summary>The service shares the IDE's retargeting session and writes into the Output panel.</summary>
    internal Basalt.Workspace.Publishing.PublishService Publisher
    {
        get
        {
            if (_publishService is not null) return _publishService;

            _publishService = new Basalt.Workspace.Publishing.PublishService { Compatibility = Compatibility };
            _publishService.OutputReceived += (_, line) => AppendOutput(line);
            return _publishService;
        }
    }

    /// <summary>The platforms and folder chosen last time for this solution, if any.</summary>
    public MakeExecutableChoice? MakeExecutableChoice { get; set; }

    /// <summary>The startup project's name, for the dialog title.</summary>
    public string MakeExecutableProjectName =>
        StartupProject is null
            ? Localizer.Get(StringKeys.MenuProject)
            : Path.GetFileNameWithoutExtension(StartupProject);

    internal void RestoreMakeExecutableChoice(SolutionSession session) =>
        MakeExecutableChoice = session.PublishRids.Count > 0 || session.PublishOutputRoot is not null
            ? new MakeExecutableChoice([.. session.PublishRids], session.PublishOutputRoot)
            : null;

    /// <summary>
    /// Publishes the startup project for each target and reports into the
    /// Output panel, Problems panel and status bar. Returns the folder to
    /// show the user, or null when nothing was made.
    /// </summary>
    public async Task<string?> MakeExecutableAsync(PublishRequest request)
    {
        // Remembered before the work starts: the choice stands even when the
        // publish fails, so fixing the code and trying again is one click.
        // The old default (dist) was stored as "no choice", so it already
        // follows the new default (bin/Publish) without any migration.
        var defaultRoot = PublishRequest.DefaultOutputRoot(request.ProjectPath);
        MakeExecutableChoice = new MakeExecutableChoice(
            [.. request.Targets.Select(t => t.Rid)],
            string.Equals(request.ResolvedOutputRoot, defaultRoot, StringComparison.Ordinal) ? null : request.OutputRoot);
        RememberSession();

        if (IsBusy)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeBusy);
            return null;
        }

        BeginBusy();
        var operation = Operations.Start(Localizer.Get(StringKeys.StatusMakeExeRunning));
        IDisposable? gate = null;

        try
        {
            // A publish builds too: not beside another build of the project.
            // Waited for before anything is cleared or announced, so that a
            // publish queued behind another build leaves that build's output
            // and status alone.
            gate = await BuildGate.AcquireAsync(
                Localizer.Get(StringKeys.GatePublish),
                holder => StatusMessage = Localizer.Get(StringKeys.StatusWaitingForBuild, holder),
                operation.Token).ConfigureAwait(true);

            BuildOutput.Clear();
            Diagnostics.Clear();
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeRunning);

            // The publish compiles from disk, like a build.
            await SaveAllAsync().ConfigureAwait(true);
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeRunning);

            var results = await Publisher.PublishAsync(request, operation.Token).ConfigureAwait(true);

            foreach (var result in results)
            {
                if (result.Succeeded)
                {
                    AppendOutput(Localizer.Get(StringKeys.MakeExeProduced,
                        result.Target.Rid, result.ArtifactPath, PublishService.FormatSize(result.SizeBytes)));
                    continue;
                }

                foreach (var diagnostic in result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
                    Diagnostics.Add(diagnostic);
            }

            var failed = results.FirstOrDefault(r => !r.Succeeded);
            if (failed is not null)
            {
                StatusMessage = Localizer.Get(StringKeys.StatusMakeExeFailed, failed.Target.Rid);
                return null;
            }

            var last = results[^1];
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeDone,
                last.ArtifactPath, PublishService.FormatSize(last.SizeBytes));
            return last.ArtifactPath;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeStopped);
            return null;
        }
        catch (Exception exception)
        {
            StatusMessage = Localizer.Get(StringKeys.StatusMakeExeInterrupted, exception.Message);
            AppendOutput($"[error] {exception.Message}");
            return null;
        }
        finally
        {
            Operations.Finish(operation);
            gate?.Dispose();
            EndBusy();
        }
    }
}
