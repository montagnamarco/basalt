using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Dock.Model.Core.Events;

namespace Basalt.Shell;

/// <summary>
/// Asks "Save changes to X?" before a dirty document is lost, the way
/// Visual Basic 6 and Visual Studio do: Save / Don't Save / Cancel.
///
/// Before this, <c>RoslynLanguageService.UpdateDocumentAsync</c> wrote every
/// pause in typing to disk, so nothing ever needed to ask. Now the editor
/// text lives only in memory until the user asks to save it, which means
/// every way of making a dirty tab go away — its own close button, Close
/// All and friends, opening another solution, New Project and quitting —
/// needs to ask first, and Close All with several dirty tabs needs to ask
/// once, not once per tab.
/// </summary>
public partial class MainWindow
{
    private enum SaveChangesChoice
    {
        Save,
        DontSave,
        Cancel
    }

    /// <summary>
    /// Documents a dock close has cancelled, waiting for the batch they
    /// arrived in to be asked about together.
    /// </summary>
    private readonly List<EditorDocumentViewModel> _pendingCloseConfirmation = [];

    private bool _closeBatchScheduled;

    /// <summary>
    /// Documents the save prompt has already settled, so re-issuing their
    /// close through the factory does not ask a second time.
    /// </summary>
    private readonly HashSet<EditorDocumentViewModel> _closeConfirmedBypass = [];

    /// <summary>Set once the window has been told it may really close.</summary>
    private bool _quitConfirmed;

    /// <summary>
    /// Set while a quit's save prompt is awaited, so a second Closing event
    /// raised before the user has answered (another window message, the
    /// user pressing the close button twice) does not open a second dialog
    /// on top of the first.
    /// </summary>
    private bool _closingInProgress;

    /// <summary>
    /// Set by <see cref="MainWindow"/>'s own test host to close the window
    /// without asking, since <see cref="Window.Close()"/> would otherwise
    /// leave the "Save changes?" dialog open and the window never raise its
    /// Closed event.
    /// </summary>
    internal bool SkipSaveChangesPromptForTests { get; set; }

    private void InitializeSaveChanges()
    {
        _factory.DockableClosing += OnDockableClosingAskToSave;
        Closing += OnWindowClosingAskToSave;
    }

    /// <summary>
    /// Catches every close the dock itself carries out — a tab's own close
    /// button, Close, Close Others, Close All, Close to the Right — before
    /// it happens.
    /// </summary>
    /// <remarks>
    /// Cancelled here and reissued once the user has answered, rather than
    /// answered synchronously: the dock's own close is a plain method call
    /// with no way to await a dialog partway through it. A close started by
    /// the view model itself (Solution Explorer deleting the file under a
    /// tab, say) has already taken the document out of
    /// <see cref="ViewModel"/>'s <c>OpenDocuments</c> by the time its visual
    /// tab follows it closed, and is told apart from a dock-started close by
    /// that: nothing here should ask to save text that has nowhere left to
    /// be saved to.
    /// </remarks>
    private void OnDockableClosingAskToSave(object? sender, DockableClosingEventArgs e)
    {
        if (e.Dockable is not IdeDocument ideDocument) return;

        var document = ideDocument.Document;

        if (!ViewModel.OpenDocuments.Contains(document)) return;
        if (_closeConfirmedBypass.Remove(document)) return;
        if (!document.IsModified) return;

        e.Cancel = true;

        if (!_pendingCloseConfirmation.Contains(document))
            _pendingCloseConfirmation.Add(document);

        if (_closeBatchScheduled) return;
        _closeBatchScheduled = true;

        // Close All cancels every one of its tabs in the same synchronous
        // call, each raising this event in turn. Posted rather than awaited
        // here, so every tab Close All touched has already added itself to
        // the batch before the one dialog for all of them is asked.
        Dispatcher.UIThread.Post(() => _ = ProcessPendingCloseBatchAsync(), DispatcherPriority.Background);
    }

    private async Task ProcessPendingCloseBatchAsync()
    {
        _closeBatchScheduled = false;

        var batch = _pendingCloseConfirmation.ToList();
        _pendingCloseConfirmation.Clear();

        await ConfirmAndCloseDirtyDocumentsAsync(batch, document =>
        {
            _closeConfirmedBypass.Add(document);

            if (_documents.TryGetValue(document, out var dockable))
                _factory.CloseDockable(dockable);
        });
    }

    /// <summary>
    /// The window closing — quitting the IDE — asks the same question for
    /// every dirty document still open, in one dialog.
    /// </summary>
    private async void OnWindowClosingAskToSave(object? sender, Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (_quitConfirmed) return;

        // The question below is awaited, not synchronous: a second Closing event while the first is still
        // being answered must not open a second one on top of it.
        if (_closingInProgress)
        {
            e.Cancel = true;
            return;
        }

        var dirty = ViewModel.OpenDocuments.Where(d => d.IsModified).ToList();

        // Table and query tabs with changes the database does not have are asked about too: quitting must not lose them.
        var dirtyDatabase = _databaseDocuments.Where(d => d.Editor.HasUnsavedChanges).ToList();

        // Closing also releases the databases: a running batch is stopped and each session disposed, awaited.
        if (dirty.Count == 0 && dirtyDatabase.Count == 0 && !DatabasesNeedReleasing) return;

        e.Cancel = true;
        _closingInProgress = true;

        try
        {
            // A test that skips the questions still releases the databases, so a test can check that they are.
            if (!SkipSaveChangesPromptForTests)
            {
                if (dirty.Count > 0 && !await ConfirmAndCloseDirtyDocumentsAsync(dirty, _ => { })) return;
                if (!await ConfirmClosingDatabaseDocumentsAsync(dirtyDatabase)) return;
            }
            await ReleaseDatabasesAsync();

            _quitConfirmed = true;
            Close();
        }
        catch (Exception ex)
        {
            // Whatever went wrong, the window must stay closable afterwards
            // rather than refuse to quit forever.
            _quitConfirmed = false;
            ViewModel.WriteOutput($"[close] {ex.Message}");
        }
        finally
        {
            _closingInProgress = false;
        }
    }

    /// <summary>
    /// Whether the current solution may be left: its dirty documents are
    /// saved or discarded first, as Visual Studio asks before opening or
    /// creating another one. False when the user cancelled.
    /// </summary>
    private async Task<bool> ConfirmLeavingSolutionAsync()
    {
        var dirty = ViewModel.OpenDocuments.Where(d => d.IsModified).ToList();

        return dirty.Count == 0 || await ConfirmAndCloseDirtyDocumentsAsync(dirty, _ => { });
    }

    /// <summary>Opens a solution or project once the current one may be left.</summary>
    private async Task OpenSolutionAskingToSaveAsync(string path)
    {
        if (!await ConfirmLeavingSolutionAsync()) return;

        await ViewModel.OpenSolutionAsync(path);
    }

    /// <summary>
    /// IdeCommands.FileClose: closes the active document, asking first when
    /// it is dirty. Bound to the palette entry and, through the shortcut
    /// registry, to Ctrl+F4 / Ctrl+W.
    /// </summary>
    private async Task CloseActiveDocumentWithSaveCheckAsync()
    {
        if (ViewModel.ActiveDocument is not { } document) return;

        if (!document.IsModified)
        {
            ViewModel.CloseActiveDocument();
            return;
        }

        await ConfirmAndCloseDirtyDocumentsAsync([document], _ => ViewModel.CloseActiveDocument());
    }

    /// <summary>
    /// Asks Save / Don't Save / Cancel once for every document in
    /// <paramref name="dirty"/> — all of them already known to be unsaved —
    /// then, unless the user cancels, saves or discards each one and calls
    /// <paramref name="close"/> for it.
    /// </summary>
    /// <returns>
    /// False when the user cancelled, or a save failed, and nothing in the
    /// list changed from that document on.
    /// </returns>
    private async Task<bool> ConfirmAndCloseDirtyDocumentsAsync(
        IReadOnlyList<EditorDocumentViewModel> dirty, Action<EditorDocumentViewModel> close)
    {
        if (dirty.Count == 0) return true;

        var choice = await AskSaveChangesAsync(dirty);
        if (choice == SaveChangesChoice.Cancel) return false;

        foreach (var document in dirty)
        {
            if (choice == SaveChangesChoice.Save)
            {
                try
                {
                    await SaveDocumentForCloseAsync(document).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A read-only file, a volume gone missing, a lock another
                    // program still holds: whatever it is, the document is
                    // not closed, nothing quits, and the text that could not
                    // be written stays in the tab rather than being thrown
                    // away silently or crashing the window's own Closing
                    // handler (that one has nowhere to propagate an
                    // exception to: it is async void).
                    await ReportSaveFailureAsync(document, ex).ConfigureAwait(true);
                    return false;
                }
            }

            close(document);

            // "Don't Save": the language service still holds the discarded
            // edit in memory. Pushing the file's own disk text back in,
            // after the tab is gone, is what keeps another file's
            // diagnostics, completion and refactorings from answering
            // against text the user chose to throw away. Awaited rather
            // than fire-and-forgotten, so a failure here is caught and
            // logged instead of becoming an unobserved task exception.
            if (choice == SaveChangesChoice.DontSave)
            {
                try
                {
                    await ViewModel.ForgetDiscardedEditAsync(document.FilePath).ConfigureAwait(true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    ViewModel.WriteOutput($"[save] {ex.Message}");
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Tells the user a save failed instead of losing the document: the
    /// status bar and Output panel get the short version, a dialog the
    /// full message, and — unlike the close or quit that asked for the
    /// save — nothing here proceeds.
    /// </summary>
    private async Task ReportSaveFailureAsync(EditorDocumentViewModel document, Exception ex)
    {
        var message = Localizer.Get(StringKeys.SaveChangesSaveFailedMessage, document.FileName, ex.Message);

        ViewModel.StatusMessage = message;
        ViewModel.WriteOutput($"[save] {message}");

        var ok = new Button
        {
            Content = Localizer.Get(StringKeys.ButtonOk),
            IsDefault = true,
            IsCancel = true
        };

        var dialog = new Window
        {
            Title = Localizer.Get(StringKeys.SaveChangesSaveFailedTitle),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        ok.Click += (_, _) => dialog.Close();

        var body = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        body.Children.Add(new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { ok }
        });
        dialog.Content = body;

        await dialog.ShowDialog(this).ConfigureAwait(true);
    }

    /// <summary>
    /// Saves one document exactly as the Save command would, whether or not
    /// it is the one currently showing: switches to it first when it is not,
    /// so the designer write-back, format-on-save and the Roslyn update the
    /// Save command already does all apply without a second copy of them
    /// here.
    /// </summary>
    private async Task SaveDocumentForCloseAsync(EditorDocumentViewModel document)
    {
        if (!ReferenceEquals(ViewModel.ActiveDocument, document))
            ViewModel.ActivateFromTabSelection(document);

        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// The Save / Don't Save / Cancel dialog, listing every document asked
    /// about when there is more than one — Visual Studio's "Save changes to
    /// the following items?" — and asking about the one by name when there
    /// is only one.
    /// </summary>
    private async Task<SaveChangesChoice> AskSaveChangesAsync(IReadOnlyList<EditorDocumentViewModel> dirty)
    {
        var choice = SaveChangesChoice.Cancel;

        var save = new Button
        {
            Content = Localizer.Get(StringKeys.SaveChangesSave),
            Classes = { "primary" },
            IsDefault = true
        };
        var dontSave = new Button { Content = Localizer.Get(StringKeys.SaveChangesDontSave) };
        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true };

        var dialog = new Window
        {
            Title = Localizer.Get(StringKeys.SaveChangesTitle),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false
        };

        save.Click += (_, _) => { choice = SaveChangesChoice.Save; dialog.Close(); };
        dontSave.Click += (_, _) => { choice = SaveChangesChoice.DontSave; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        var message = dirty.Count == 1
            ? Localizer.Get(StringKeys.SaveChangesMessageOne, dirty[0].FileName)
            : Localizer.Get(StringKeys.SaveChangesMessageMany);

        var body = new StackPanel { Margin = new Thickness(20), Spacing = 16 };

        body.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });

        if (dirty.Count > 1)
        {
            body.Children.Add(new ItemsControl
            {
                ItemsSource = dirty.Select(d => d.FileName).ToList(),
                MaxHeight = 160
            });
        }

        // Windows order: Save, Don't Save, Cancel.
        body.Children.Add(DialogButtons.Row(save, dontSave, cancel));

        dialog.Content = body;

        await dialog.ShowDialog(this);

        return choice;
    }
}
