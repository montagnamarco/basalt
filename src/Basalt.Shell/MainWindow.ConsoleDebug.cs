using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Basalt.Workspace.Debugging;
using Basalt.Workspace.Immediate;

namespace Basalt.Shell;

/// <summary>
/// F5 on a console program: run in a Terminal tab, where it can be typed to, with the debugger attached.
/// </summary>
/// <remarks>
/// netcoredbg launches a program with pipes of its own and, under the Debug Adapter Protocol, passes nothing
/// typed on to it, so Console.ReadLine waited for ever. Here the program is started in the terminal, holding at
/// its start through <see cref="DebuggerWaitHook"/>, and the debugger is attached to it before it goes on: the
/// breakpoints in its first lines are hit, and the user types to it like any console program.
/// </remarks>
public partial class MainWindow
{
    /// <summary>The program being debugged in a Terminal tab, stopped with the session.</summary>
    private ProgramTerminal? _debuggedProgram;

    /// <summary>How long a program waits for the debugger to attach before it goes on without it.</summary>
    private const int DebuggerWaitSeconds = 30;

    /// <summary>
    /// Debugs the startup project in a Terminal tab when it is a console program. False when it is not, or
    /// when what that needs (a runtime to build the wait hook for) is not there: the caller then launches it
    /// as before.
    /// </summary>
    private async Task<bool> TryDebugInTerminalAsync(string project, string assembly, LaunchOptions launch)
    {
        string? hook;

        try
        {
            hook = await Task.Run(() =>
            {
                if (!ConsoleProjects.IsConsole(project)) return null;

                var runtime = SharedFrameworkLocator.Find(SharedFrameworkLocator.MajorOf(assembly, project));

                return runtime is null ? null : DebuggerWaitHook.EnsureFor(runtime);
            }).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // No hook (an unwritable ~/.basalt, a compile refused): launched as before, without terminal input.
            ViewModel.WriteOutput($"[debug] {ex.Message}");
            return false;
        }

        if (hook is null) return false;

        // The profile's variables first, the hook's after them: the hook must be the one that holds the program. Hooks
        // the profile itself names are joined to it as the user's own environment's are.
        var environment = new Dictionary<string, string>(launch.Environment, StringComparer.Ordinal);

        foreach (var (name, value) in DebuggerWaitHook.EnvironmentFor(
                     hook, DebuggerWaitSeconds,
                     launch.Environment.GetValueOrDefault(DebuggerWaitHook.StartupHooksVariable)
                     ?? Environment.GetEnvironmentVariable(DebuggerWaitHook.StartupHooksVariable)))
            environment[name] = value;

        ProgramTerminal program;

        try
        {
            // The assembly itself, not "dotnet run": the process attached to must be the program, and "dotnet
            // run" would start it as a child of its own.
            program = await StartProgramInTerminalAsync(
                Path.GetFileNameWithoutExtension(project),
                "dotnet",
                [assembly, .. launch.Arguments],
                launch.WorkingDirectory ?? ViewModel.StartupDirectory ?? Path.GetDirectoryName(assembly)!,
                environment);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ViewModel.WriteOutput($"[debug] {ex.Message}");
            return true;
        }

        // The terminal could not start the program: there is nothing to attach to.
        if (program.ProcessId < 0)
        {
            program.Stop();
            ViewModel.WriteOutput($"[debug] {Localizer.Get(StringKeys.StatusRunInTerminalFailed)}");
            return true;
        }

        _debuggedProgram = program;

        await _debug.AttachAsync(program.ProcessId, assembly);

        // The attach was refused, or the session failed as it started: the program would wait out its hook
        // and then run undebugged, out of reach of Stop. It is ended instead; the failure is already reported.
        if (!_debug.IsRunning)
        {
            // This program, not the field: the session's end may already have cleared it.
            program.Stop();
            if (ReferenceEquals(_debuggedProgram, program)) _debuggedProgram = null;
            return true;
        }

        // The program's tab stays in front while the debug layout opens, so its prompt can be read and typed to,
        // unless it has already stopped at a breakpoint, where the Call Stack is what the user needs.
        if (!_debug.IsPaused) ShowProgramTab(program);

        return true;
    }

    /// <summary>
    /// The arguments, environment and working directory the project's launch profile gives the program, for F5 to
    /// start it with as Ctrl+F5 ("dotnet run") already does. Read off the interface thread; a profile that cannot
    /// be read is reported and the program starts without it, in the project's folder.
    /// </summary>
    private async Task<LaunchOptions> ReadLaunchOptionsAsync(string project)
    {
        var directory = ViewModel.StartupDirectory;

        try
        {
            return await Task.Run(() => DebugLaunchProfile.For(project, directory)).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            ViewModel.WriteOutput($"[debug] {ex.Message}");
            return LaunchOptions.In(directory);
        }
    }

    /// <summary>Ends the program a terminal debug session started, when the session is stopped.</summary>
    private void StopDebuggedProgram()
    {
        _debuggedProgram?.Stop();
        _debuggedProgram = null;
    }
}
