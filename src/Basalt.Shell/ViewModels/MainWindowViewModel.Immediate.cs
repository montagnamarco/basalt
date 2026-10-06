using Basalt.Core.Localization;
using Basalt.Workspace;
using Basalt.Workspace.Immediate;
using Basalt.Workspace.Testing;

namespace Basalt.Shell.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ImmediateTargetResolver? _immediateTargets;
    private readonly object _immediateGate = new();

    /// <summary>
    /// Held while the startup project is being built, whoever builds it: the
    /// toolbar, a run, a debug start or a line of the Immediate window.
    /// </summary>
    internal readonly BuildGate BuildGate = new();

    private int _busyCount;

    /// <summary>
    /// Marks the start of something that makes the IDE busy (a build, a
    /// publish, opening a solution, one of them still queued).
    ///
    /// Counted rather than set: when two overlap, the first to end must not
    /// clear the flag while the other is still going.
    /// </summary>
    internal void BeginBusy()
    {
        _busyCount++;
        IsBusy = true;
    }

    internal void EndBusy()
    {
        if (_busyCount > 0) _busyCount--;

        IsBusy = _busyCount > 0;
    }

    /// <summary>The status a second build request shows while one is queued, for the tests.</summary>
    internal async Task<string> BuildQueuedStatusForTestsAsync()
    {
        await BuildProjectForTestsAsync();

        return StatusMessage;
    }

    /// <summary>Whether a debug session is under way; set by the window that owns the session.</summary>
    public Func<bool>? IsDebuggingProbe { get; set; }

    /// <summary>
    /// Makes a test service wait for the build gate, and say so: the status
    /// names who is being waited for, and the IDE counts as busy meanwhile.
    /// Events arrive on a background thread, so they are posted to the
    /// interface thread.
    /// </summary>
    internal void WatchTestService(DotnetTestService service)
    {
        service.WaitingForGate += (_, holder) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            BeginBusy();
            StatusMessage = Localizer.Get(StringKeys.StatusWaitingForBuild, holder);
        });

        service.WaitEnded += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(EndBusy);
    }

    /// <summary>Whether a build is waiting for <see cref="BuildGate"/>.</summary>
    private volatile bool _buildQueued;

    /// <summary>
    /// The built startup project that a line of the Immediate window is
    /// evaluated against, building it first when its sources are newer.
    /// Runs on the background thread the Immediate session evaluates on.
    /// </summary>
    internal Task<ImmediateTarget> ResolveImmediateTargetAsync(CancellationToken ct)
    {
        ImmediateTargetResolver resolver;

        lock (_immediateGate)
        {
            resolver = _immediateTargets ??= new ImmediateTargetResolver(
                () => StartupProject,
                () => Configuration,
                () => IsBusy || _runService.IsRunning || (IsDebuggingProbe?.Invoke() ?? false),
                (project, configuration, token) => _buildService.BuildAsync(project, configuration, token),
                BuildGate);
        }

        return resolver.ResolveAsync(ct);
    }
}
