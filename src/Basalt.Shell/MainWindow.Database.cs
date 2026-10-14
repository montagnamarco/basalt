using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Commands;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Shell.Controls;
using Basalt.Shell.Database;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Ai;
using Dock.Model.Core;
using Dock.Model.Core.Events;

namespace Basalt.Shell;

/// <summary>
/// The Database client's wiring: the Database Explorer in its tool slot, Table
/// Data and Query documents in the document area, and the Database menu.
/// </summary>
/// <remarks>
/// What the database client does lives in <c>Basalt.Shell.Database</c>; this
/// file only connects it to the window: where its documents go, what closing
/// one asks, which keys reach a query, and which menu entries open what.
/// </remarks>
public partial class MainWindow
{
    private DatabaseWorkspace _databases = null!;
    private DatabaseExplorerPanel _databaseExplorer = null!;
    private readonly List<DatabaseDocument> _databaseDocuments = [];

    /// <summary>Documents the user has just answered the Save question for, so the close they caused goes through.</summary>
    private readonly HashSet<DatabaseDocument> _databaseCloseConfirmed = [];

    private int _queryCount;

    /// <summary>Set while the query tabs close for a solution change, which records the session itself.</summary>
    private bool _closingQueryTabs;

    /// <summary>
    /// Builds the Database Explorer and its workspace. Passwords go to the platform keychain,
    /// through the store the AI keys use.
    /// </summary>
    private void BuildDatabaseClient()
    {
        _databases = new DatabaseWorkspace(_settingsStore, new KeychainDatabasePasswordStore(new ApiKeyStore()));
        _databaseExplorer = new DatabaseExplorerPanel(_databases);

        _databaseExplorer.DataRequested += (_, request) => Guarded.Run(
            () => OpenTableDataAsync(request), ViewModel.WriteOutput, "database");

        _databaseExplorer.QueryRequested += (_, request) => OpenQueryDocument(request.ConnectionId, request.Sql, null);
        _databaseExplorer.ConnectionRemoved += (_, connectionId) => MarkConnectionRemoved(connectionId);
        _databaseExplorer.ConnectionEdited += (_, connectionId) => MarkConnectionChanged(connectionId);

        _contents[_factory.Database] = _databaseExplorer;

        // A .sql file opens as a query from every path that opens a file: the view model asks here first.
        ViewModel.FileRouter = TryOpenQueryFileAsync;

        // A query saved to a file is part of the solution's session, as code tabs are.
        ViewModel.ExtraOpenTabs = QueryTabsForSession;

        // Leaving a solution asks about the query tabs with changes, then closes them, as it closes the code tabs.
        ViewModel.ConfirmClosingQueryTabs = ConfirmClosingQueryTabsAsync;
        ViewModel.CloseQueryTabs = CloseQueryTabsForSolutionChange;

        // The sessions are closed with the window; a session left open would hold its file or server.
        Closed += (_, _) => _ = ReleaseDatabasesAsync();

        _factory.DockableClosing += OnDatabaseDocumentClosing;
        _factory.DockableClosed += (_, args) => ForgetDatabaseDocument(args.Dockable);
    }

    /// <summary>The Explorer's workspace. For the tests, which give it an in-memory password store first.</summary>
    internal DatabaseWorkspace DatabaseWorkspaceForTests => _databases;

    /// <summary>The Explorer. For the tests.</summary>
    internal DatabaseExplorerPanel DatabaseExplorerForTests => _databaseExplorer;

    /// <summary>Closes a Table Data or Query tab the way its close button does, so the save question can be answered.</summary>
    internal void CloseDatabaseDocumentForTests(DatabaseDocument document) => _factory.CloseDockable(document);

    /// <summary>The Table Data and Query documents open now. For the tests.</summary>
    internal IReadOnlyList<DatabaseDocument> DatabaseDocumentsForTests => _databaseDocuments;

    /// <summary>
    /// Opens the rows of a table or view. A table already open is shown again rather than opened twice.
    /// </summary>
    private async Task OpenTableDataAsync(DatabaseObjectRequest request)
    {
        var open = _databaseDocuments.FirstOrDefault(d =>
            d.View is TableDataView view && !view.IsRemoved && view.ConnectionId == request.ConnectionId && view.Table == request.Name);

        if (open is not null)
        {
            ShowDatabaseDocument(open);
            return;
        }

        var view = new TableDataView(_databases, request.ConnectionId, request.Name, request.Kind);
        var document = new DatabaseDocument(Localizer.Get(StringKeys.DbDataTitle, request.Name.ToString()), view);

        AddDatabaseDocument(document);

        await view.LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Opens a query: a new one with the given SQL, or a saved file of SQL when a path is given.</summary>
    private DatabaseDocument OpenQueryDocument(string? connectionId, string sql, string? filePath)
    {
        var view = new QueryView(_databases, ++_queryCount, connectionId, sql, filePath);
        var document = new DatabaseDocument(view.Title, view);

        view.TitleChanged += (_, _) =>
        {
            document.Title = view.Title;
            ViewModel.RememberSession();
        };

        AddDatabaseDocument(document);
        ViewModel.RememberSession();

        return document;
    }

    /// <summary>
    /// A .sql file opens as a query document, whichever way the file was asked for. Returns false for any
    /// other file, which then opens as it always did. The file is read on the pool, not the interface thread.
    /// </summary>
    private async Task<bool> TryOpenQueryFileAsync(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".sql", StringComparison.OrdinalIgnoreCase)) return false;

        var open = _databaseDocuments.FirstOrDefault(d => d.View is QueryView query && query.FilePath == path);

        if (open is not null)
        {
            ShowDatabaseDocument(open);
            return true;
        }

        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);

        OpenQueryDocument(null, text, path);
        return true;
    }

    /// <summary>The query files open now, for the solution's session.</summary>
    private IEnumerable<OpenDocumentSession> QueryTabsForSession() =>
        _databaseDocuments.Select(d => d.View).OfType<QueryView>()
            .Where(q => q.FilePath is not null)
            .Select(q => new OpenDocumentSession { Path = q.FilePath!, InDesigner = false });

    /// <summary>Whether closing the window has databases to release: a session open, or a batch running.</summary>
    private bool DatabasesNeedReleasing =>
        _databases.HasOpenSessions || _databaseDocuments.Select(d => d.View).OfType<QueryView>().Any(q => q.IsRunning);

    /// <summary>
    /// Stops what runs against the databases and disposes their sessions, awaited, before the window closes.
    /// </summary>
    internal async Task ReleaseDatabasesAsync()
    {
        foreach (var view in _databaseDocuments.Select(d => d.View).OfType<QueryView>()) view.CancelRun();

        try
        {
            await _databases.DisposeAsync().ConfigureAwait(true);
        }
        catch (AggregateException ex)
        {
            // A connection that would not close must not keep the window open: the others were closed, and this is written down.
            ViewModel.WriteOutput(Localizer.Get(StringKeys.DbCloseFailed, string.Join(" ", ex.InnerExceptions.Select(e => e.Message))));
        }
    }

    /// <summary>
    /// Asks about the query tabs with changes before a solution is left, as it asks about a code tab. False when the
    /// user cancelled. Each answer is the same one a query tab's own close asks for.
    /// </summary>
    private async Task<bool> ConfirmClosingQueryTabsAsync()
    {
        var dirty = _databaseDocuments.Where(d => d.View is QueryView && d.Editor.HasUnsavedChanges).ToList();

        return dirty.Count == 0 || await ConfirmClosingDatabaseDocumentsAsync(dirty).ConfigureAwait(true);
    }

    /// <summary>
    /// Closes every query tab, for the solution being left. Their changes were asked about first, so each
    /// one closes without asking again.
    /// </summary>
    private void CloseQueryTabsForSolutionChange()
    {
        var queries = _databaseDocuments.Where(d => d.View is QueryView).ToList();

        // The solution's session is recorded with its query tabs still open, by the solution change itself:
        // each tab's close must not record a session of its own.
        _closingQueryTabs = true;

        try
        {
            foreach (var document in queries)
            {
                // A batch still running would keep its connection busy after the tab that shows it is gone.
                if (document.View is QueryView query) query.CancelRun();

                _databaseCloseConfirmed.Add(document);
                _factory.CloseDockable(document);

                // The dock raises its close event at once, or not at all for a tab it no longer holds: forgetting here covers both.
                _databaseCloseConfirmed.Remove(document);
                ForgetDatabaseDocument(document);
            }
        }
        finally
        {
            _closingQueryTabs = false;
        }
    }

    /// <summary>
    /// Marks the table tabs of an edited connection stale: they read nothing more and write nothing, and say so.
    /// </summary>
    private void MarkConnectionChanged(string connectionId)
    {
        foreach (var view in _databaseDocuments.Select(d => d.View).OfType<TableDataView>())
        {
            if (view.ConnectionId == connectionId && !view.IsRemoved) view.MarkStale();
        }
    }

    /// <summary>
    /// Marks the table tabs of a removed connection, so they say so and read nothing more. A query
    /// tab keeps its text; it can only be run again against a connection that exists.
    /// </summary>
    private void MarkConnectionRemoved(string connectionId)
    {
        foreach (var view in _databaseDocuments.Select(d => d.View).OfType<TableDataView>())
        {
            if (view.ConnectionId == connectionId) view.MarkRemoved();
        }
    }

    /// <summary>
    /// Asks about each query or table tab with changes that the window is closing with, and saves or drops
    /// them as the user answers. Cancel on any of them keeps the window open.
    /// </summary>
    internal async Task<bool> ConfirmClosingDatabaseDocumentsAsync(IReadOnlyList<DatabaseDocument> dirty)
    {
        foreach (var document in dirty)
        {
            var choice = await AskSaveDatabaseChangesAsync(document.Title).ConfigureAwait(true);

            if (choice == DatabaseCloseChoice.Cancel) return false;

            if (choice == DatabaseCloseChoice.Save && !await document.Editor.SaveAsync().ConfigureAwait(true)) return false;
        }

        return true;
    }

    private void AddDatabaseDocument(DatabaseDocument document)
    {
        _contents[document] = document.View;
        _databaseDocuments.Add(document);
        ViewModel.OtherDocumentCount = _databaseDocuments.Count;

        if (_factory.DocumentArea is not { } area) return;

        _factory.AddDockable(area, document);
        ShowDatabaseDocument(document);
    }

    /// <summary>Puts every open Table Data and Query tab into the document area of a new layout.</summary>
    private void RestoreDatabaseDocumentsAfterReset()
    {
        if (_factory.DocumentArea is not { } area) return;

        foreach (var document in _databaseDocuments) _factory.AddDockable(area, document);
    }

    private void ShowDatabaseDocument(DatabaseDocument document)
    {
        if (_factory.DocumentArea is not { } area) return;

        _factory.SetActiveDockable(document);
        _factory.SetFocusedDockable(area, document);
    }

    private void ForgetDatabaseDocument(IDockable? dockable)
    {
        if (dockable is not DatabaseDocument document) return;

        _databaseDocuments.Remove(document);
        _contents.Remove(document);
        ViewModel.OtherDocumentCount = _databaseDocuments.Count;

        if (!_closingQueryTabs) ViewModel.RememberSession();
    }

    /// <summary>
    /// Asks before a Table Data or Query tab with changes is closed, as an unsaved file is asked about.
    /// </summary>
    /// <remarks>
    /// The close is cancelled here and reissued once the user has answered: the dock's own close is a
    /// plain method call with no way to wait for a dialog.
    /// </remarks>
    private void OnDatabaseDocumentClosing(object? sender, DockableClosingEventArgs e)
    {
        if (e.Dockable is not DatabaseDocument document) return;
        if (_databaseCloseConfirmed.Remove(document)) return;
        if (!document.Editor.HasUnsavedChanges) return;

        e.Cancel = true;

        Dispatcher.UIThread.Post(() => Guarded.Run(
            () => AskToCloseDatabaseDocumentAsync(document), ViewModel.WriteOutput, "database"));
    }

    private async Task AskToCloseDatabaseDocumentAsync(DatabaseDocument document)
    {
        var choice = await AskSaveDatabaseChangesAsync(document.Title).ConfigureAwait(true);

        if (choice == DatabaseCloseChoice.Cancel) return;

        if (choice == DatabaseCloseChoice.Save && !await document.Editor.SaveAsync().ConfigureAwait(true)) return;

        _databaseCloseConfirmed.Add(document);
        _factory.CloseDockable(document);
    }

    private enum DatabaseCloseChoice
    {
        Save,
        Discard,
        Cancel,
    }

    /// <summary>The same three answers as an unsaved file, with this document's own sentence.</summary>
    private async Task<DatabaseCloseChoice> AskSaveDatabaseChangesAsync(string title)
    {
        var choice = DatabaseCloseChoice.Cancel;

        var save = new Button
        {
            Content = Localizer.Get(StringKeys.SaveChangesSave),
            Classes = { "primary" },
            IsDefault = true,
        };
        var discard = new Button { Content = Localizer.Get(StringKeys.SaveChangesDontSave) };
        var cancel = new Button { Content = Localizer.Get(StringKeys.ButtonCancel), IsCancel = true };

        var dialog = new Window
        {
            Title = Localizer.Get(StringKeys.SaveChangesTitle),
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
        };

        save.Click += (_, _) => { choice = DatabaseCloseChoice.Save; dialog.Close(); };
        discard.Click += (_, _) => { choice = DatabaseCloseChoice.Discard; dialog.Close(); };
        cancel.Click += (_, _) => dialog.Close();

        dialog.Content = new StackPanel
        {
            Spacing = Spacing.Normal,
            Margin = Spacing.DialogPadding,
            Children =
            {
                new TextBlock
                {
                    Text = Localizer.Get(StringKeys.DbCloseAskMessage, title),
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = Spacing.Normal,
                    Children = { save, discard, cancel },
                },
            },
        };

        await dialog.ShowDialog(this);

        return choice;
    }

    /// <summary>The Database menu: connections, queries, and the Explorer.</summary>
    private IReadOnlyList<MenuEntry> DatabaseMenuEntries() =>
    [
        new(Localizer.Get(StringKeys.DbAddConnection),
            Action: () => Guarded.Run(() => RunDatabaseCommandAsync(IdeCommands.DatabaseAddConnection), ViewModel.WriteOutput, "database")),
        new(Localizer.Get(StringKeys.DbNewQuery),
            Action: () => Guarded.Run(() => RunDatabaseCommandAsync(IdeCommands.DatabaseNewQuery), ViewModel.WriteOutput, "database"),
            Gesture: Shortcut(IdeCommands.DatabaseNewQuery)),
        MenuEntry.Separator,
        new(Localizer.Get(StringKeys.DbExplorerTitle),
            Action: () => Guarded.Run(() => RunDatabaseCommandAsync(IdeCommands.ViewDatabaseExplorer), ViewModel.WriteOutput, "database"),
            Gesture: Shortcut(IdeCommands.ViewDatabaseExplorer)),
    ];

    /// <summary>Carries out a database command chosen from the menu, the palette or a key.</summary>
    private async Task RunDatabaseCommandAsync(string commandId)
    {
        switch (commandId)
        {
            case IdeCommands.ViewDatabaseExplorer:
                ShowTool(_factory.Database);
                break;

            case IdeCommands.DatabaseAddConnection:
                ShowTool(_factory.Database);
                await _databaseExplorer.AddConnectionAsync();
                break;

            case IdeCommands.DatabaseNewQuery:
                OpenQueryDocument(_databaseExplorer.SelectedConnectionId, "", null);
                break;
        }
    }

    /// <summary>
    /// F5 and Ctrl+Enter run a query and Ctrl+S saves it, when the query has the keyboard.
    /// Otherwise F5 is Start Debugging, as everywhere else in the IDE.
    /// </summary>
    private bool TryRunQueryShortcut(KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not Visual focused) return false;
        if (focused.FindAncestorOfType<TableDataView>(includeSelf: true) is { } table)
        {
            // Ctrl+S in a table tab writes that tab's edits; the other keys belong to the grid.
            if (e.Key != Key.S || e.KeyModifiers != KeyModifiers.Control) return false;

            table.SaveFromKeyboard();
            return true;
        }

        if (focused.FindAncestorOfType<QueryView>(includeSelf: true) is not { } query) return false;

        if ((e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
            || (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control))
        {
            query.RunFromKeyboard();
            return true;
        }

        if (e.Key == Key.S && e.KeyModifiers == KeyModifiers.Control)
        {
            query.SaveFromKeyboard();
            return true;
        }

        return false;
    }
}
