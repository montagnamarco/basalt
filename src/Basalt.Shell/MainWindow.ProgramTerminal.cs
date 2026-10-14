using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

/// <summary>
/// Terminal tabs that run a program rather than a shell.
///
/// The tab is the same panel a shell terminal is, so the person types to the program as they would to a shell and
/// sees its colours. The window only makes the tab; running and stopping the program is the view model's.
/// </summary>
public partial class MainWindow
{
    /// <summary>Lends the view model the way to open a program's tab. See <see cref="RunInTerminalHandler"/>.</summary>
    private void InitializeProgramTerminals() =>
        ViewModel.RunInTerminal = async (title, program, arguments, workingDirectory, environment) =>
            await StartProgramInTerminalAsync(title, program, arguments, workingDirectory, environment);

    /// <summary>
    /// Opens a terminal tab titled <paramref name="title"/> that runs <paramref name="program"/> instead of a shell,
    /// and returns the handle the caller keeps.
    /// </summary>
    /// <remarks>
    /// The tab is made here rather than left to the dock's content factory, so its view exists, with the program
    /// already running, before the tab is shown. The task is already complete when it is returned: the caller
    /// subscribes to the program's exit straight away, which is only safe while no exit can be raised ahead of
    /// it. A failure is returned in the task, as the caller expects from an asynchronous start.
    /// </remarks>
    public Task<ProgramTerminal> StartProgramInTerminalAsync(
        string title,
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        try
        {
            return Task.FromResult(StartProgramTerminal(title, program, arguments, workingDirectory, environment));
        }
        catch (Exception ex)
        {
            return Task.FromException<ProgramTerminal>(ex);
        }
    }

    /// <summary>The tab and view of each program running in a terminal, so its tab can be brought forward again.</summary>
    private readonly Dictionary<ProgramTerminal, (TerminalTool Tool, TerminalView View)> _programTabs = [];

    /// <summary>Puts a program's terminal tab in front, with the keyboard in it.</summary>
    private void ShowProgramTab(ProgramTerminal program)
    {
        if (!_programTabs.TryGetValue(program, out var tab) || _factory.BottomArea is not { } area) return;

        _factory.SetActiveDockable(tab.Tool);
        _factory.SetFocusedDockable(area, tab.Tool);
        Dispatcher.UIThread.Post(() => tab.View.FocusInput(), DispatcherPriority.Background);
    }

    private ProgramTerminal StartProgramTerminal(
        string title,
        string program,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment)
    {
        if (_factory.BottomArea is not { } area)
            throw new InvalidOperationException(Localizer.Get(StringKeys.TerminalNoPanel));

        // A terminal is a panel asked for, as New Terminal's are: the welcome screen would cover it.
        ViewModel.WelcomeDismissed = true;

        var settings = _settingsStore.Load();

        // The factory names new tabs from its working directory, which the shell terminals use. It is put back
        // afterwards, so the program's folder does not become the folder of the next shell.
        var shellDirectory = _factory.TerminalWorkingDirectory;
        TerminalTool tool;
        try
        {
            _factory.TerminalWorkingDirectory = workingDirectory;
            tool = _factory.CreateTerminal();
        }
        finally
        {
            _factory.TerminalWorkingDirectory = shellDirectory;
        }

        // The tab is named for the program, not numbered as a shell's is.
        tool.Title = title;

        // The program starts when the view is built, so the handle is made straight after it: nothing that runs
        // before the tab is shown can lose the exit.
        var view = new TerminalView(workingDirectory,
            () => ProgramTerminal.Connect(program, arguments, workingDirectory, environment));
        var terminal = new ProgramTerminal(view);
        view.ApplySettings(settings);

        // Close Terminal in its menu ends the program and asks for the tab to go, as it does for a shell.
        view.CloseRequested += (_, _) => _factory.CloseDockable(tool);

        // The view is registered before the tab is added, so the dock takes this view rather than building a shell.
        _contents[tool] = view;
        _factory.AddDockable(area, tool);
        _factory.SetActiveDockable(tool);
        _factory.SetFocusedDockable(area, tool);

        // Focus goes to the program, so the first keys typed reach it, as a freshly opened terminal's do.
        Dispatcher.UIThread.Post(() => view.FocusInput(), DispatcherPriority.Background);

        _programTabs[terminal] = (tool, view);
        terminal.Closed += (_, _) => _programTabs.Remove(terminal);

        return terminal;
    }
}
