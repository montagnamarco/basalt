using Avalonia.Threading;

namespace Basalt.Shell;

public partial class MainWindow
{
    private bool _gitRefreshAfterSaveQueued;

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
