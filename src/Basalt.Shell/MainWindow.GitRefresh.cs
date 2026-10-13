using Avalonia.Threading;

namespace Basalt.Shell;

public partial class MainWindow
{
    private bool _gitRefreshAfterSaveQueued;

    /// <summary>
    /// Tells the Git panels when there is no repository to show, and why: no
    /// solution, git missing, or a folder that is not a repository. Called on
    /// every RepositoryChanged, which detection raises whatever it finds, and
    /// when the solution changes. Cheap: it reads no Git state, only whether
    /// one was found.
    /// </summary>
    private void ShowNoRepositoryWhenThereIsNone()
    {
        if (ViewModel.Repository is not null) return;

        var absence = ViewModel.SolutionPath is null ? Controls.GitAbsence.NoSolution
            : ViewModel.GitUnavailable ? Controls.GitAbsence.GitNotInstalled
            : Controls.GitAbsence.NotARepository;

        _gitChanges?.ShowNoRepository(absence);
        _branches?.ShowNoRepository(absence);
    }

    /// <summary>Keeps the no-repository state current; called once the Git panels exist.</summary>
    private void WireNoRepositoryState()
    {
        ShowNoRepositoryWhenThereIsNone();

        // Posted: a solution that has just loaded is only detected for Git
        // after the event, and a repository found then reports itself.
        ViewModel.BrowsableSolutionChanged += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(
            ShowNoRepositoryWhenThereIsNone, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// One git refresh for a burst of saves: Save All of twenty files would otherwise start twenty
    /// status, log and branch reads. Saves can finish off the interface thread, so the refresh is
    /// posted to it either way.
    /// </summary>
    private void RefreshGitAfterSave() => Dispatcher.UIThread.Post(() =>
    {
        if (_gitRefreshAfterSaveQueued) return;
        _gitRefreshAfterSaveQueued = true;

        Dispatcher.UIThread.Post(() =>
        {
            _gitRefreshAfterSaveQueued = false;
            Guarded.Run(() => RefreshGitAsync(), ViewModel.WriteOutput, "ide");
        }, DispatcherPriority.Background);
    });
}
