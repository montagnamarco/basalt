using Avalonia.Threading;
using Basalt.Shell.Controls;

namespace Basalt.Shell;

public partial class MainWindow
{
    private ObjectBrowserPanel? _objectBrowser;

    /// <summary>
    /// Builds the Object Browser and connects it to the solution and to the
    /// editor: a symbol written in the solution opens at its declaration, by
    /// the same path as a go-to-definition.
    /// </summary>
    private void BuildObjectBrowserPanel()
    {
        _objectBrowser = new ObjectBrowserPanel(ViewModel.CreateObjectBrowserLoader());

        _objectBrowser.SourceRequested += (_, target) =>
        {
            RecordCurrentPosition();
            Guarded.Run(
                () => OpenAtAsync(target.FilePath, target.Line, target.Column),
                ViewModel.WriteOutput, "ide");
        };

        _objectBrowser.Failed += (_, message) => ViewModel.WriteOutput($"[object browser] {message}");

        // The libraries are read when the browser is first shown and again
        // when another solution is loaded; reading them while the solution is
        // still loading, or for a browser nobody opened, is work for nothing.
        // Re-read when another solution is loaded and, debounced, when the
        // text of a file changes: a browser that kept the compilations it
        // first read would describe code that no longer exists.
        ViewModel.BrowsableSolutionChanged += (_, _) => Dispatcher.UIThread.Post(
            () => _objectBrowser?.ScheduleRefresh(), DispatcherPriority.Background);

        _contents[_factory.ObjectBrowser] = _objectBrowser;
    }

    /// <summary>Shows the Object Browser, reading the libraries the first time.</summary>
    private void ShowObjectBrowser()
    {
        ShowTool(_factory.ObjectBrowser);

        if (_objectBrowser is not { } browser) return;

        // The libraries are read when it is first shown, and again if the
        // solution changed while it was out of sight.
        if (browser.Libraries.Count == 0)
            Guarded.Run(browser.RefreshAsync, ViewModel.WriteOutput, "ide");
        else
            Guarded.Run(browser.RefreshIfStaleAsync, ViewModel.WriteOutput, "ide");
    }
}
