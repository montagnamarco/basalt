using Avalonia;
using Avalonia.Threading;
using Basalt.Shell.ViewModels;

namespace Basalt.Shell.Controls;

/// <summary>
/// Keeps the tree in step with the disk while the panel is on screen.
///
/// A file written, renamed or deleted outside the IDE, by another editor, a
/// checkout or a build, appears in or leaves the tree without the user
/// pressing Refresh. The watch belongs to the panel rather than to the view
/// model, so detaching the panel, which closing the window does, disposes it:
/// a watcher left running would keep reading a tree nobody is looking at.
/// </summary>
public sealed partial class SolutionExplorerPanel
{
    private DiskWatch? _disk;
    private string? _diskDirectory;
    private bool _attachedToTree;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attachedToTree = true;
        if (DataContext is MainWindowViewModel viewModel) WatchDisk(viewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attachedToTree = false;
        StopWatchingDisk();
    }

    /// <summary>Watches the folder of the loaded solution or project, unless it already is.</summary>
    private void WatchDisk(MainWindowViewModel viewModel)
    {
        if (!_attachedToTree) return;

        var explorer = viewModel.Explorer;
        var directory = explorer.LoadedPath is { Length: > 0 } loaded
            ? Path.GetDirectoryName(Path.GetFullPath(loaded))
            : null;

        if (_disk is not null && string.Equals(directory, _diskDirectory, StringComparison.Ordinal)) return;

        StopWatchingDisk();
        if (directory is null || !Directory.Exists(directory)) return;

        _diskDirectory = directory;
        _disk = new DiskWatch(directory, explorer.IsListed, explorer.Refresh);
    }

    /// <summary>Test seam: raises the same path a FileSystemWatcher error takes.</summary>
    internal void RaiseWatchErrorForTests() => _disk?.Overflowed();

    private void StopWatchingDisk()
    {
        _disk?.Dispose();
        _disk = null;
        _diskDirectory = null;
    }

    /// <summary>
    /// One folder tree under watch. Events come on thread-pool threads in
    /// bursts, so each listed change restarts a short quiet period, and the
    /// settled callback runs once on the UI thread after the burst.
    /// </summary>
    private sealed class DiskWatch : IDisposable
    {
        /// <summary>Quiet time after the last listed change: a save or a build writes many files at once.</summary>
        private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(300);

        private readonly FileSystemWatcher _watcher;
        private readonly System.Threading.Timer _timer;
        private readonly Func<string, bool> _isListed;
        private readonly Action _settled;
        private readonly object _gate = new();
        private bool _disposed;

        public DiskWatch(string directory, Func<string, bool> isListed, Action settled)
        {
            _isListed = isListed;
            _settled = settled;
            _timer = new System.Threading.Timer(
                _ => Dispatcher.UIThread.Post(Settle), null, Timeout.Infinite, Timeout.Infinite);

            _watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = true,
                // Names only: a write to a file that already exists changes nothing the tree shows.
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };
            _watcher.Created += (_, e) => Noticed(e.FullPath);
            _watcher.Deleted += (_, e) => Noticed(e.FullPath);
            _watcher.Renamed += (_, e) =>
            {
                Noticed(e.OldFullPath);
                Noticed(e.FullPath);
            };
            // An error means events were lost (the watcher's buffer overflowed, most
            // likely under a large build): nothing says what changed, so the whole tree is read again.
            _watcher.Error += (_, _) => Overflowed();
            _watcher.EnableRaisingEvents = true;
        }

        private void Noticed(string path)
        {
            if (!_isListed(path)) return;

            lock (_gate)
            {
                if (!_disposed) _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>Schedules one full re-read of the tree, through the same quiet period as a change.</summary>
        internal void Overflowed()
        {
            lock (_gate)
            {
                if (!_disposed) _timer.Change(Quiet, Timeout.InfiniteTimeSpan);
            }
        }

        private void Settle()
        {
            lock (_gate)
            {
                if (_disposed) return;
            }

            _settled();
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }

            _watcher.Dispose();
            _timer.Dispose();
        }
    }
}
