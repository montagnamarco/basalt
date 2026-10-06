using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;

namespace Basalt.Shell;

/// <summary>
/// Solution Explorer's New File, New Folder, Rename and Delete.
///
/// The filesystem work (naming rules, creating, moving, deleting) lives in
/// <see cref="ExplorerFileOperations"/>, which needs no window and no
/// Avalonia control to be tested. What stays here is asking the user for a
/// name or a confirmation, and telling the tree and the editor what
/// happened.
/// </summary>
public partial class MainWindow
{
    /// <summary>The Solution Explorer panel, for a test to focus and select in.</summary>
    internal SolutionExplorerPanel? SolutionExplorerForTests => _solutionExplorer;

    /// <summary>
    /// F2 and Delete, acted on the Solution Explorer's own selection when it
    /// has keyboard focus, rather than on whatever the global shortcut
    /// registry has them bound to (Rename Symbol and, on the editor side,
    /// nothing).
    /// </summary>
    private async Task<bool> TryRunExplorerShortcutAsync(Key key, KeyModifiers modifiers)
    {
        if (modifiers != KeyModifiers.None) return false;
        if (_solutionExplorer is not { IsKeyboardFocusWithin: true } explorer) return false;

        switch (key)
        {
            case Key.F2:
                await RunExplorerCommandAsync(
                    SolutionExplorerPanel.ExplorerCommand.Rename, explorer.SelectedNode);
                return true;

            case Key.Delete:
                await RunExplorerCommandAsync(
                    SolutionExplorerPanel.ExplorerCommand.Delete, explorer.SelectedNode);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Creates a file under the selected folder or project (or under the
    /// solution's own folder when nothing is selected), then opens it.
    /// </summary>
    private async Task NewFileInExplorerAsync(SolutionTreeNode? node)
    {
        if (DirectoryFor(node) is not { Length: > 0 } directory) return;

        var name = await AskForTextAsync(
            Localizer.Get(StringKeys.ExplorerNewFileTitle),
            Localizer.Get(StringKeys.ExplorerNewFilePrompt),
            "NewFile.vb");

        if (name is not { Length: > 0 }) return;

        var problem = ExplorerFileOperations.Validate(name, directory);
        if (problem != ExplorerFileOperations.NameProblem.None)
        {
            ViewModel.WriteOutput(DescribeNameProblem(problem, name));
            return;
        }

        string created;
        try
        {
            created = ExplorerFileOperations.CreateFile(directory, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput(Localizer.Get(StringKeys.ExplorerOperationFailed, ex.Message));
            return;
        }

        ViewModel.Explorer.Refresh();
        await ViewModel.OpenFileAsync(created);
    }

    /// <summary>
    /// Creates a folder under the selected folder or project (or under the
    /// solution's own folder when nothing is selected).
    /// </summary>
    private async Task NewFolderInExplorerAsync(SolutionTreeNode? node)
    {
        if (DirectoryFor(node) is not { Length: > 0 } directory) return;

        var name = await AskForTextAsync(
            Localizer.Get(StringKeys.ExplorerNewFolderTitle),
            Localizer.Get(StringKeys.ExplorerNewFolderPrompt),
            "NewFolder");

        if (name is not { Length: > 0 }) return;

        var problem = ExplorerFileOperations.Validate(name, directory);
        if (problem != ExplorerFileOperations.NameProblem.None)
        {
            ViewModel.WriteOutput(DescribeNameProblem(problem, name));
            return;
        }

        try
        {
            ExplorerFileOperations.CreateFolder(directory, name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput(Localizer.Get(StringKeys.ExplorerOperationFailed, ex.Message));
            return;
        }

        ViewModel.Explorer.Refresh();
    }

    /// <summary>
    /// Renames the selected file or folder on disk, retargets every open
    /// document under it to the new path (keeping unsaved text), reopens
    /// the Roslyn workspace from disk so it sees the move, and refreshes
    /// the tree.
    /// </summary>
    private async Task RenameInExplorerAsync(SolutionTreeNode? node)
    {
        // Defensive: the menu already greys this out and the shortcut
        // already refuses on the explorer's own node (see
        // SolutionTreeNode.CanRenameOrDelete), but this is the one place
        // every path into a rename ends up, so it refuses too rather than
        // trusting every caller to have checked already.
        if (node is not { CanRenameOrDelete: true }) return;

        // Same defensive refusal while a debug session is running: the
        // adapter and the breakpoints it was given are keyed by path, and a
        // rename mid-session would leave both pointing at a file that no
        // longer exists where they think it does, the way Visual Studio
        // refuses to edit a running item rather than leave the debugger
        // confused about where it is.
        if (_debug.IsRunning)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.ExplorerBlockedWhileDebugging);
            return;
        }

        var isDirectory = node.Kind == NodeKind.Folder;
        var oldPath = node.Path;

        if (Path.GetDirectoryName(oldPath) is not { Length: > 0 } directory) return;

        var currentName = Path.GetFileName(oldPath);

        var name = await AskForTextAsync(
            Localizer.Get(StringKeys.ExplorerRenameTitle),
            Localizer.Get(StringKeys.ExplorerRenamePrompt),
            currentName);

        if (name is not { Length: > 0 } || name == currentName) return;

        var problem = ExplorerFileOperations.Validate(name, directory, oldPath);
        if (problem != ExplorerFileOperations.NameProblem.None)
        {
            ViewModel.WriteOutput(DescribeNameProblem(problem, name));
            return;
        }

        // Read before the move: the tabs' own paths are only the real ones
        // up to this point.
        var openDocuments = ViewModel.OpenDocuments
            .Where(d => IsAtOrUnder(d.FilePath, oldPath)).ToList();

        // Same read, for breakpoints: a breakpoint in a file that was never
        // opened in this session is still on disk under the renamed path,
        // and still belongs there after the move. Collected here, before the
        // paths change, from every file under the renamed one rather than
        // only the open tabs.
        var breakpointPaths = _debug.AllBreakpoints
            .Select(b => b.FilePath)
            .Distinct()
            .Where(p => IsAtOrUnder(p, oldPath))
            .ToList();

        string newPath;
        try
        {
            newPath = ExplorerFileOperations.Rename(oldPath, name, isDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput(Localizer.Get(StringKeys.ExplorerOperationFailed, ex.Message));
            return;
        }

        foreach (var document in openDocuments)
        {
            var documentOldPath = document.FilePath;
            var documentNewPath = isDirectory
                ? ExplorerFileOperations.Retarget(documentOldPath, oldPath, newPath)
                : newPath;

            document.Retarget(documentNewPath);
            RetargetMarginFor(documentOldPath, documentNewPath);
        }

        foreach (var breakpointPath in breakpointPaths)
        {
            var retargetedPath = isDirectory
                ? ExplorerFileOperations.Retarget(breakpointPath, oldPath, newPath)
                : newPath;

            _debug.RetargetBreakpoints(breakpointPath, retargetedPath);
        }

        // Reopens the Roslyn workspace from disk rather than asking
        // MSBuildWorkspace to apply the move itself: see
        // RoslynLanguageService.ReloadAsync for why. This also pushes every
        // open tab's current buffer back in, since reopening forgets them.
        await ViewModel.ReloadLanguageServiceAsync();

        ViewModel.Explorer.Refresh();
    }

    /// <summary>
    /// Deletes the selected file or folder after confirming, closes any open
    /// tab under it without a save prompt (there is nothing left on disk to
    /// save to), reopens the Roslyn workspace from disk so it sees the
    /// delete, and refreshes the tree.
    /// </summary>
    private async Task DeleteInExplorerAsync(SolutionTreeNode? node)
    {
        // Same defensive refusal as RenameInExplorerAsync, and for the same
        // node kinds.
        if (node is not { CanRenameOrDelete: true }) return;

        // Same defensive refusal as RenameInExplorerAsync while a debug
        // session is running, and for the same reason.
        if (_debug.IsRunning)
        {
            ViewModel.StatusMessage = Localizer.Get(StringKeys.ExplorerBlockedWhileDebugging);
            return;
        }

        var isDirectory = node.Kind == NodeKind.Folder;
        var path = node.Path;
        var name = Path.GetFileName(path);

        // Read before confirming: the confirmation itself needs to know
        // whether any affected tab has unsaved edits, since those are about
        // to be discarded along with the file and deserve their own warning
        // rather than the plain "this cannot be undone" delete carries when
        // there is nothing open to lose.
        var openDocuments = ViewModel.OpenDocuments
            .Where(d => IsAtOrUnder(d.FilePath, path)).ToList();
        var hasUnsavedChanges = openDocuments.Any(d => d.IsModified);

        var messageKey = (isDirectory, hasUnsavedChanges) switch
        {
            (true, true) => StringKeys.ExplorerDeleteFolderMessageUnsaved,
            (true, false) => StringKeys.ExplorerDeleteFolderMessage,
            (false, true) => StringKeys.ExplorerDeleteFileMessageUnsaved,
            (false, false) => StringKeys.ExplorerDeleteFileMessage
        };

        var confirmed = await ConfirmDeleteAsync(
            Localizer.Get(StringKeys.ExplorerDeleteTitle),
            Localizer.Get(messageKey, name));

        if (!confirmed) return;

        try
        {
            ExplorerFileOperations.Delete(path, isDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ViewModel.WriteOutput(Localizer.Get(StringKeys.ExplorerOperationFailed, ex.Message));
            return;
        }

        foreach (var document in openDocuments) ViewModel.CloseDocument(document);

        // Same reload as a rename, and for the same reason: see
        // RoslynLanguageService.ReloadAsync.
        await ViewModel.ReloadLanguageServiceAsync();

        ViewModel.Explorer.Refresh();
    }

    /// <summary>
    /// Moves a renamed, open document's breakpoint margin from its old path
    /// to its new one.
    ///
    /// Keyed by file path, and watched for by this exact path elsewhere
    /// (<see cref="MainWindow.ConnectDebugging"/>'s subscriptions read
    /// <c>editor.FilePath</c> live, but the dictionary entry does not move
    /// on its own). Without this a breakpoint set before the rename stops
    /// showing in the margin, which is still found by the old path. The
    /// breakpoints themselves move separately in
    /// <see cref="RenameInExplorerAsync"/>, for every file under the
    /// renamed path and not only the open ones, since a margin only exists
    /// for a file that happens to be open.
    /// </summary>
    private void RetargetMarginFor(string oldPath, string newPath)
    {
        if (_margins.Remove(oldPath, out var margin)) _margins[newPath] = margin;
    }

    /// <summary>Whether a path is the given folder itself, or sits anywhere under it.</summary>
    private static bool IsAtOrUnder(string path, string folder)
    {
        var full = Path.GetFullPath(path);
        var fullFolder = Path.GetFullPath(folder);

        if (string.Equals(full, fullFolder, PathComparison)) return true;

        return full.Length > fullFolder.Length
            && full.StartsWith(fullFolder, PathComparison)
            && full[fullFolder.Length] is '/' or '\\';
    }

    /// <summary>On Windows and macOS paths are case-insensitive, on Linux they are not.</summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Asks the user to confirm a delete, with a button that says so rather
    /// than the generic confirmation's "Discard" — wrong verb for a file or
    /// folder that was never a pending change.
    /// </summary>
    private async Task<bool> ConfirmDeleteAsync(string title, string message)
    {
        var answered = false;

        var yes = new Button
        {
            Content = Localizer.Get(StringKeys.ExplorerDeleteConfirm),
            Classes = { "primary" }
        };
        var no = new Button
        {
            Content = Localizer.Get(StringKeys.ButtonCancel),
            IsCancel = true,
            IsDefault = true
        };

        var dialog = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        yes.Click += (_, _) => { answered = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Children = { no, yes }
                }
            }
        };

        await dialog.ShowDialog(this);

        return answered;
    }

    /// <summary>
    /// The folder a New File or New Folder command creates into: the node
    /// itself when it is already a folder, its containing folder otherwise
    /// (a project or source file is a node whose own path is a file), or
    /// the solution's folder when nothing is selected.
    /// </summary>
    private string? DirectoryFor(SolutionTreeNode? node)
    {
        if (node is null)
            return ViewModel.SolutionPath is { Length: > 0 } solution
                ? Path.GetDirectoryName(Path.GetFullPath(solution))
                : null;

        return node.Kind == NodeKind.Folder ? node.Path : Path.GetDirectoryName(node.Path);
    }

    /// <summary>What to tell the user about a name a filesystem check refused.</summary>
    private static string DescribeNameProblem(ExplorerFileOperations.NameProblem problem, string name) =>
        problem switch
        {
            ExplorerFileOperations.NameProblem.Empty => Localizer.Get(StringKeys.ExplorerNameEmpty),
            ExplorerFileOperations.NameProblem.InvalidCharacters =>
                Localizer.Get(StringKeys.ExplorerNameInvalid, name),
            ExplorerFileOperations.NameProblem.AlreadyExists =>
                Localizer.Get(StringKeys.ExplorerNameExists, name),
            _ => Localizer.Get(StringKeys.ExplorerOperationFailed, name)
        };
}
