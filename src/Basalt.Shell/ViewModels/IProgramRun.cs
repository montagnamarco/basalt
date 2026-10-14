namespace Basalt.Shell.ViewModels;

/// <summary>
/// A program the window started in a terminal tab, as the run path sees it.
/// </summary>
/// <remarks>
/// The run path needs three things from a program in a tab: whether it is still going, how it ended, and a way to
/// end it. Naming them here keeps the view model apart from the controls that draw the tab.
/// </remarks>
public interface IProgramRun
{
    /// <summary>The program's process id, or -1 when the tab could not start it.</summary>
    int ProcessId { get; }

    /// <summary>True while the program runs.</summary>
    bool IsRunning { get; }

    /// <summary>
    /// Raised once the program has exited, with its exit code. The sender is the run. It is raised on the interface
    /// thread, and not while the run's own tab is being disposed: that case is <see cref="Closed"/>.
    /// </summary>
    event EventHandler<int>? Exited;

    /// <summary>Raised when the tab has been closed. Closing it ends the program, which then raises no <see cref="Exited"/>.</summary>
    event EventHandler? Closed;

    /// <summary>Ends the program. The tab stays and shows how the program ended.</summary>
    void Stop();
}

/// <summary>
/// Opens a terminal tab titled <paramref name="title"/> that runs <paramref name="program"/> and returns the run.
/// The window sets it on the view model, because only the window can open a tab.
/// </summary>
public delegate Task<IProgramRun> RunInTerminalHandler(
    string title,
    string program,
    IReadOnlyList<string> arguments,
    string workingDirectory,
    IReadOnlyDictionary<string, string>? environment);
