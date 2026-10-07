using Basalt.Shell.ViewModels;

namespace Basalt.Shell;

/// <summary>
/// Keeps the title bar following the solution and the debugger, the way
/// Visual Basic 6 and Visual Studio do (see <see cref="WindowTitle"/>).
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Subscribes to what the title depends on and sets it once up front.
    ///
    /// Called from the constructor, after <see cref="_debug"/> and the view
    /// model both exist: the title needs to react to a solution opening or
    /// closing and to every debugger transition that changes "design" into
    /// "run" or "break" and back.
    /// </summary>
    private void InitializeWindowTitle()
    {
        RefreshTitle();

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SolutionPath)
             || e.PropertyName == nameof(MainWindowViewModel.IsRunning))
            {
                RefreshTitle();
            }
        };

        _debug.Started += (_, _) => RefreshTitle();
        _debug.Paused += (_, _) => RefreshTitle();
        _debug.Resumed += (_, _) => RefreshTitle();
        _debug.Exited += (_, _) => RefreshTitle();
    }

    private void RefreshTitle() =>
        Title = WindowTitle.For(ViewModel.SolutionPath, _debug.IsRunning || ViewModel.IsRunning, _debug.IsPaused);
}
