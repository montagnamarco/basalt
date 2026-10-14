using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Threading;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Terminal;

namespace Basalt.Shell.Controls;

/// <summary>
/// A program running in a terminal tab.
/// </summary>
/// <remarks>
/// The tab's terminal is the program's console: keystrokes reach the program and its output is drawn there. This
/// class is the handle the run path keeps. It reports how the program ended, ends it on request, and says when the
/// tab was closed. Everything else about the tab is the shell terminal's, which is why the view is reused as it is.
/// </remarks>
public sealed class ProgramTerminal : IProgramRun
{
    private readonly TerminalView _view;
    private readonly Action<int> _killTree;

    // Set before the process is ended by closing the tab. Read on the threadpool thread that reports the exit, so
    // it is volatile: an exit caused by the close is not reported as the program ending on its own.
    private volatile bool _closing;

    /// <param name="view">The tab's terminal, which must already be running the program.</param>
    /// <param name="killTree">
    /// Ends a process and the processes it started. Left out, the platform's tree kill is used; tests pass one that
    /// fails, to see that a failure never reaches the interface thread.
    /// </param>
    public ProgramTerminal(TerminalView view, Action<int>? killTree = null)
    {
        _view = view;
        _killTree = killTree ?? KillProcessTree;

        // The exit arrives on a threadpool thread. Posting it to the interface thread keeps it behind the caller's
        // subscription: the caller subscribes on that thread straight after the tab opens. A posted callback runs
        // only once that code yields. A view that exited before this point replays the exit on subscription.
        view.ProcessExited += (_, code) =>
        {
            if (!_closing) Dispatcher.UIThread.Post(() => Exited?.Invoke(this, code));
        };

        // Closing the tab ends the program and everything it started. Done while the process is still ours to end,
        // so the children are not left running once the terminal lets go of the process.
        view.Closing += (_, _) =>
        {
            _closing = true;
            Stop();
        };

        view.Released += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    public int ProcessId => _view.ProcessId;

    public bool IsRunning => _view.IsRunning;

    public event EventHandler<int>? Exited;

    public event EventHandler? Closed;

    /// <summary>
    /// Ends the program and the programs it started.
    /// </summary>
    /// <remarks>
    /// Killed rather than interrupted: Stop is a request to end, and a program that ignores Ctrl+C would otherwise
    /// run on. The id is the one the terminal started. "dotnet run" starts the program as a child, so the whole tree
    /// is ended. The tab stays, and prints how the program ended, as a program that finishes on its own does.
    /// Never throws: the interface thread asks for this, and a tree that changed while it was being ended is not a
    /// reason to fail the Stop command.
    /// </remarks>
    public void Stop()
    {
        if (!IsRunning) return;

        try
        {
            _killTree(ProcessId);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception
                                       or NotSupportedException or AggregateException)
        {
            // Already gone, or the platform will not end it from here. Either way the tab shows how it ended.
        }
    }

    private static void KillProcessTree(int processId)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree: true);
    }

    /// <summary>
    /// Starts <paramref name="program"/> on a pseudo-terminal of this platform: ConPTY on Windows, openpty elsewhere.
    /// </summary>
    public static IPtyConnection Connect(
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPtyConnection(program, arguments, workingDirectory, environment: environment);

        return new UnixPtyConnection(program, arguments, workingDirectory, environment: environment);
    }
}
