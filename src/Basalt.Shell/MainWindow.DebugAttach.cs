using Basalt.Core.Localization;
using Basalt.Workspace.Debugging;

namespace Basalt.Shell;

/// <summary>
/// Debug &gt; Attach to Process: debugging a .NET program that is already running.
/// </summary>
/// <remarks>
/// The session is the one F5 uses, started the other way round: the debugger attaches to the process, the
/// breakpoints are sent, and Call Stack, Break All and the stepping commands work as for a launched program.
/// Stopping it detaches (netcoredbg's "disconnect" with terminateDebuggee false, measured to leave the process
/// running, even when it was stopped at a breakpoint) instead of ending the program, which was not the IDE's to end.
/// </remarks>
public partial class MainWindow
{
    /// <summary>The process attached to by hand, for the message that says it was let go.</summary>
    private int _attachedProcessId;

    /// <summary>Asks which process, then attaches to it.</summary>
    private async Task AttachToProcessAsync()
    {
        // One session at a time: attaching would end the one under way, and a program the IDE launched is
        // not one to kill because another was chosen.
        if (_debug.IsRunning)
        {
            ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.DebugAlreadyDebugging)}");
            return;
        }

        var dialog = new AttachToProcessDialog(() => Task.Run(DotnetProcessLister.List));

        await dialog.ShowDialog(this);

        if (dialog.Result is { } process)
            await AttachToProcessAsync(process.ProcessId, process.Name);
    }

    /// <summary>Attaches the debugger to a running process and shows the session.</summary>
    internal async Task AttachToProcessAsync(int processId, string name)
    {
        if (_debug.IsRunning)
        {
            ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.DebugAlreadyDebugging)}");
            return;
        }

        ShowTool(_factory.Output);

        _debugTarget = name;
        ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.DebugAttaching, name, processId)}");
        ShowStartingBanner();

        _attachedProcessId = processId;

        // Failures come back through the session's Failed event, which says them and resets the banner.
        await _debug.AttachAsync(processId, programPath: null, detachOnStop: true);

        if (!_debug.IsRunning) _attachedProcessId = 0;

        ShowDebugState();
    }

    /// <summary>Says that the session let its process go on running, after Stop detached from it.</summary>
    private void ReportDetached()
    {
        if (_attachedProcessId == 0) return;

        ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.DebugDetached, _attachedProcessId)}");
        _attachedProcessId = 0;
    }
}
