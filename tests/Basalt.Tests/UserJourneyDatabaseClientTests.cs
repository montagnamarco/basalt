using System.Collections;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Database;
using Basalt.Shell.ViewModels;
using Basalt.Workspace.Ai;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The Database client through the real window: the Explorer's tree and
/// connection dialog, the Table Data document, and the Query document.
/// </summary>
/// <remarks>
/// Every connection is a temporary SQLite file, so the engine that answers is the
/// real one. Passwords go to an in-memory store, never the keychain. The one
/// journey that scripts its session is the one whose answer SQLite does not
/// give yet: the line number of an error (<see cref="ScriptedSession"/>).
///
/// Settings-touching tests share <see cref="SettingsFileCollection"/> because
/// BASALT_SETTINGS is process-wide.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class UserJourneyDatabaseClientTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-journey-database-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-journey-database-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public UserJourneyDatabaseClientTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Journey 1: adding a connection shows what is in it.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AddingASqliteConnectionShowsItsTablesViewsColumnsIndexesAndKeys()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);

        await ShowExplorerAsync(shell);
        Assert.Contains(L(StringKeys.DbNoConnections), shell.Explorer.GetVisualDescendants().OfType<EmptyState>().Single().Message);

        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        Assert.True(await WaitUntilAsync(shell, () => connection.IsLoaded), "The connection's tables were not listed.");

        var tables = Group(connection, StringKeys.DbTablesGroup, 2);
        var views = Group(connection, StringKeys.DbViewsGroup, 1);
        Assert.Equal("ActiveCustomers", Assert.Single(views.Children).Text);

        await ExpandAsync(shell, tables);

        var customers = tables.Children.OfType<ObjectNode>().Single(o => o.Text == "Customers");
        await ExpandAsync(shell, customers);
        Assert.True(await WaitUntilAsync(shell, () => customers.IsLoaded), "The columns were not read.");

        var columns = Category(customers, StringKeys.DbColumnsGroup);
        await ExpandAsync(shell, columns);

        Assert.Equal(
            ["Id  INTEGER  PK  NOT NULL", "Name  TEXT  NOT NULL", "City  TEXT  NULL"],
            columns.Children.Select(c => c.Text).ToArray());

        // The rows of the tree are in the tree, not only in the model.
        Assert.Contains(TreeItems(shell), item => item.DataContext is LeafNode { Text: "City  TEXT  NULL" } && item.IsVisible);

        var indexes = Category(customers, StringKeys.DbIndexesGroup);
        Assert.Equal(["IX_Customers_City (City)"], indexes.Children.Select(c => c.Text).ToArray());

        var orders = tables.Children.OfType<ObjectNode>().Single(o => o.Text == "Orders");
        await ExpandAsync(shell, orders);
        Assert.True(await WaitUntilAsync(shell, () => orders.IsLoaded));

        var keys = Category(orders, StringKeys.DbForeignKeysGroup);
        Assert.Equal(["CustomerId → Customers(Id)"], keys.Children.Select(c => c.Text).ToArray());
    }

    // ------------------------------------------------------------------
    // Journey 2: a table's rows are paged, sorted and filtered on the server.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task OpenDataByDoubleClickPagesSortsAndFiltersTheRows()
    {
        var database = CreateShop(_root);
        AddItems(database, 450);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);

        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        await WaitUntilAsync(shell, () => connection.IsLoaded);

        var tables = Group(connection, StringKeys.DbTablesGroup, 3);
        await ExpandAsync(shell, tables);
        var items = tables.Children.OfType<ObjectNode>().Single(o => o.Text == "Items");

        DoubleClick(shell, items);

        var view = await WaitForTableAsync(shell, "Items");
        Assert.Equal(
            Localizer.Get(StringKeys.DbDataTitle, "Items"),
            shell.Window.DatabaseDocumentsForTests.Single(d => d.View == view).Title);
        Assert.Equal("rows 1–200 of 450", view.RangeText);
        Assert.Equal(200, RowsOf(view).Count);

        Click(ButtonLabelled(view, L(StringKeys.DbNext)));
        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 201–400 of 450"), view.RangeText);

        Click(ButtonLabelled(view, L(StringKeys.DbLast)));
        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 401–450 of 450"), view.RangeText);
        Assert.Equal(50, RowsOf(view).Count);

        Click(ButtonLabelled(view, L(StringKeys.DbFirst)));
        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 1–200 of 450"), view.RangeText);

        // Sorted by the server: the first row of the whole table, not of the page.
        await view.SortByAsync("Label");
        Assert.Equal("item 1", Cell(RowsOf(view)[0], 1));

        await view.SortByAsync("Label");
        Assert.Equal("item 99", Cell(RowsOf(view)[0], 1));

        // Filtered by the server: the condition the user typed decides the rows.
        var filter = view.GetVisualDescendants().OfType<TextBox>().Single(t => t.PlaceholderText == L(StringKeys.DbFilterHint));
        filter.Text = "Label LIKE 'item 44%'";
        Click(ButtonLabelled(view, L(StringKeys.DbApply)));

        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 1–11 of 11"), view.RangeText);
        Assert.Equal(11, RowsOf(view).Count);
    }

    // ------------------------------------------------------------------
    // Journey 3: edits stay pending until Save, which writes them to the file.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EditingAddingAndDeletingRowsThenSavingChangesTheFile()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var grid = GridOf(view);

        await EditCellAsync(shell, grid, 0, 1, "Rossi Srl");

        Click(ButtonLabelled(view, L(StringKeys.DbAddRow)));
        await shell.SettleAsync(2);
        Assert.Equal(4, RowsOf(view).Count);

        await EditCellAsync(shell, grid, 3, 1, "Neri");
        await EditCellAsync(shell, grid, 3, 2, "Torino");

        // Verdi has no orders; Bianchi has one, so the foreign key would refuse his deletion.
        grid.SelectedIndex = 2;
        Click(ButtonLabelled(view, L(StringKeys.DbDeleteRow)));

        Assert.True(view.HasUnsavedChanges, "The edits are not pending.");
        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), view.StatusText);

        Assert.Equal(["Rossi Srl", "Bianchi", "Neri"], FileValues(database, "SELECT Name FROM Customers ORDER BY Id"));
        Assert.Equal("Torino", FileValues(database, "SELECT City FROM Customers WHERE Name = 'Neri'")[0]);
    }

    // ------------------------------------------------------------------
    // Journey 4: Discard drops the edits, and closing with edits asks first.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DiscardDropsThePendingEditsAndClosingWithThemAsks()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var grid = GridOf(view);

        await EditCellAsync(shell, grid, 0, 1, "Changed");
        Assert.True(view.HasUnsavedChanges);

        Click(ButtonLabelled(view, L(StringKeys.DbDiscard)));
        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges));

        Assert.Equal("Rossi", Cell(RowsOf(view)[0], 1));
        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);

        // Edit again, then close the tab: the question is asked, and "Don't save" closes it without writing.
        await EditCellAsync(shell, grid, 0, 1, "Pending");
        var document = shell.Window.DatabaseDocumentsForTests.Single(d => d.View == view);

        shell.Window.CloseDatabaseDocumentForTests(document);

        var question = await WaitForDialogAsync(shell);
        Assert.NotNull(question);
        Click(ButtonLabelled(question!, L(StringKeys.SaveChangesDontSave)));

        Assert.True(await WaitUntilAsync(shell, () => !shell.Window.DatabaseDocumentsForTests.Contains(document)),
            "The tab did not close after the answer.");
        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    // ------------------------------------------------------------------
    // Journey 5: a view, and a table without a key, are read-only and say why.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AViewOpensReadOnlyAndAnEditIsRefused()
    {
        var database = CreateShop(_root);
        ChangeOutside(database, "CREATE TABLE Log (Message TEXT)");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenFromExplorerAsync(shell, "ActiveCustomers", inViews: true);
        var grid = GridOf(view);

        Assert.True(view.IsReadOnly);
        Assert.True(grid.IsReadOnly);
        Assert.Contains(
            view.GetVisualDescendants().OfType<TextBlock>(),
            t => t.IsVisible && t.Text == Localizer.Get(StringKeys.DatabaseNotEditable, "ActiveCustomers"));
        Assert.False(ButtonLabelled(view, L(StringKeys.DbSave)).IsEnabled);

        // A table without a primary key is no better: an update could not say which row it means.
        var log = await OpenFromExplorerAsync(shell, "Log", inViews: false);
        Assert.True(log.IsReadOnly);
        Assert.True(GridOf(log).IsReadOnly);

        shell.Window.CloseDatabaseDocumentForTests(shell.Window.DatabaseDocumentsForTests.Single(d => d.View == view));
    }

    // ------------------------------------------------------------------
    // Journey 6: a constraint the database refuses selects the row it refused.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AConstraintErrorSelectsTheRowItRefusedAndChangesNothing()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Orders");
        var grid = GridOf(view);

        Click(ButtonLabelled(view, L(StringKeys.DbAddRow)));
        await shell.SettleAsync(2);

        await EditCellAsync(shell, grid, 2, 1, "1");
        await EditCellAsync(shell, grid, 2, 2, "-5");

        // Move the selection away first: the refused row must be selected by the save, not left selected by the edit.
        grid.SelectedIndex = 0;
        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => view.StatusText.StartsWith("Save was refused", StringComparison.Ordinal)),
            view.StatusText);

        // The row the engine refused is the one selected, and it is named by its number.
        Assert.Same(RowsOf(view)[2], grid.SelectedItem);
        Assert.Contains("Row 3", view.StatusText, StringComparison.Ordinal);

        // Nothing was written, and the edit stays pending for the user to correct.
        Assert.Equal(2, FileValues(database, "SELECT Id FROM Orders").Count);
        Assert.True(view.HasUnsavedChanges);
    }

    // ------------------------------------------------------------------
    // Journey 7: a new query runs, shows each result set, and says what else happened.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ANewQueryShowsEachResultSetInItsOwnTabAndTheMessagesInLast()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);

        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
        Assert.Equal(Localizer.Get(StringKeys.DbQueryTitle, 1), shell.Window.DatabaseDocumentsForTests[0].Title);

        query.Sql = "SELECT Name, City FROM Customers ORDER BY Id;\nSELECT 'x' AS Marker;";
        Click(Named<Button>(query, "QueryRun"));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.Results.Items.Count == 3), query.StatusText);

        var headers = query.Results.Items.Cast<TabItem>().Select(t => t.Header?.ToString() ?? "").ToArray();
        Assert.Equal(
            [Localizer.Get(StringKeys.DbResultTab, 1), Localizer.Get(StringKeys.DbResultTab, 2), L(StringKeys.DbMessagesTab)],
            headers);

        Assert.StartsWith(L(StringKeys.DbElapsed).Split('{')[0].TrimEnd(), query.StatusText, StringComparison.Ordinal);

        // A batch that fails says so in the Messages tab, in red, and the results before it still show.
        query.Sql = "SELECT 1 AS One;\nSELEC 2;";
        Click(Named<Button>(query, "QueryRun"));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.StatusText.StartsWith("Failed", StringComparison.Ordinal)),
            query.StatusText);

        await shell.SettleAsync(3);
        var messages = query.Results.Items.Cast<TabItem>().Last();
        Assert.Same(messages, query.Results.SelectedItem);

        // A tab's content is presented by the tab control, not beneath the tab item.
        var error = query.Results.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text!.Contains("syntax error", StringComparison.OrdinalIgnoreCase));

        // Red, whichever theme is on: the error colour's red channel dominates the others.
        var colour = Assert.IsType<SolidColorBrush>(error.Foreground).Color;
        Assert.True(colour.R > colour.G + 60 && colour.R > colour.B + 60, $"The error is not red: {colour}");
    }

    // ------------------------------------------------------------------
    // Journey 8: an error with a line moves the caret to that line.
    // ------------------------------------------------------------------

    /// <remarks>
    /// Scripted session: the SQLite provider does not yet report the line an error is on
    /// (it does not override ErrorLine), so the answer is given here. Everything after the
    /// answer, the red message and the caret, is the real window.
    /// </remarks>
    [AvaloniaFact]
    public async Task AnErrorWithALineMovesTheCaretToThatLine()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        shell.Window.DatabaseWorkspaceForTests.AdoptSessionForTests(connection.ConnectionId, new ScriptedSession(
            new QueryBatchResult([], [new QueryMessage("near \"SELEC\": syntax error", true, 3)], TimeSpan.FromMilliseconds(4))));

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = "SELECT 1;\nSELECT 2;\nSELEC 3;";
        query.Editor.CaretOffset = 0;
        Click(Named<Button>(query, "QueryRun"));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.Results.Items.Count == 1), query.StatusText);

        await shell.SettleAsync(3);
        var error = query.Results.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text!.Contains("syntax error", StringComparison.Ordinal));

        error.RaiseEvent(new TappedEventArgs(Control.TappedEvent, null!));

        Assert.Equal(3, query.Editor.Document.GetLineByOffset(query.Editor.CaretOffset).LineNumber);
    }

    // ------------------------------------------------------------------
    // Journey 9: Stop cancels a batch that would run for minutes; Disconnect stops it too.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task StopCancelsASlowBatchAndDisconnectStopsOneThatRuns()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = SlowCountQuery;
        Click(Named<Button>(query, "QueryRun"));

        Assert.True(await WaitUntilAsync(shell, () => query.IsRunning), "The slow batch did not start.");
        Assert.True(Named<Button>(query, "QueryCancel").IsVisible);
        Assert.False(Named<Button>(query, "QueryRun").IsEnabled);

        var started = Stopwatch.StartNew();
        Click(Named<Button>(query, "QueryCancel"));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning, attempts: 300), "Cancel did not end the batch.");
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(20), "The stop took as long as the batch would.");
        Assert.StartsWith(L(StringKeys.DatabaseStopped), query.StatusText, StringComparison.Ordinal);
        Assert.True(Named<Button>(query, "QueryRun").IsEnabled);

        // Disconnect while a batch runs: the batch stops, and the connection closes.
        query.Sql = SlowCountQuery;
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => query.IsRunning));

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        SelectItem(shell, connection);
        Click(ButtonLabelled(shell.Explorer, L(StringKeys.DatabaseDisconnect)));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && !shell.Workspace.IsOpen(connection.ConnectionId), attempts: 300),
            "Disconnect did not stop the batch and close the connection.");
        Assert.False(connection.IsLoaded);
    }

    // ------------------------------------------------------------------
    // Journey 10: export writes the result with its header; a NULL is an empty field.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ExportCsvWritesTheResultWithItsHeadersAndANullAsAnEmptyField()
    {
        var database = CreateShop(_root);
        var exported = Path.Combine(_root, "customers.csv");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = "SELECT Name, City FROM Customers ORDER BY Id";
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.Results.Items.Count == 2));

        Assert.Equal(3, await query.ExportActiveResultAsync(exported));

        var lines = File.ReadAllLines(exported);
        Assert.Equal(["Name,City", "Rossi,Milano", "Bianchi,Roma", "Verdi,"], lines);
    }

    // ------------------------------------------------------------------
    // Journey 11: the Explorer's refresh and disconnect.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task RefreshListsATableMadeOutsideAndDisconnectEmptiesTheNode()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        await WaitUntilAsync(shell, () => connection.IsLoaded);
        Group(connection, StringKeys.DbTablesGroup, 2);

        ChangeOutside(database, "CREATE TABLE Invoices (Id INTEGER PRIMARY KEY, Amount REAL)");

        SelectItem(shell, connection);
        Click(ButtonLabelled(shell.Explorer, L(StringKeys.DatabaseRefresh)));

        Assert.True(await WaitUntilAsync(shell,
            () => connection.Children.OfType<GroupNode>().Any(g => g.Text == $"{L(StringKeys.DbTablesGroup)} (3)")),
            "Refresh did not list the table created outside.");

        Click(ButtonLabelled(shell.Explorer, L(StringKeys.DatabaseDisconnect)));

        Assert.True(await WaitUntilAsync(shell, () => !shell.Workspace.IsOpen(connection.ConnectionId)));
        Assert.False(connection.IsLoaded);
        Assert.Equal(L(StringKeys.DbNotConnected), Assert.Single(connection.Children).Text);
        Assert.Equal(Localizer.Get(StringKeys.DbDisconnected, connection.Text), shell.Explorer.StatusText);

        // Expanding the node again connects, and lists the tables again.
        var item = ItemFor(shell, connection);
        item.IsExpanded = false;
        await shell.SettleAsync(2);
        item.IsExpanded = true;

        Assert.True(await WaitUntilAsync(shell, () => connection.IsLoaded), "Re-expanding did not list the tables.");
        Group(connection, StringKeys.DbTablesGroup, 3);
    }

    // ------------------------------------------------------------------
    // Journey 12: a file that is not a database, and a missing one, are refused by Test Connection.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TestConnectionRefusesAFileThatIsNotADatabaseOrIsMissingAndTheExplorerStaysUsable()
    {
        var notes = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(notes, "just some text");
        var missing = Path.Combine(_root, "missing.db");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);

        var dialog = await OpenAddDialogAsync(shell);

        Named<TextBox>(dialog, "DatabaseName").Text = "Notes";
        Named<TextBox>(dialog, "DatabaseFile").Text = notes;
        Click(Named<Button>(dialog, "DatabaseTest"));

        Assert.True(await WaitUntilAsync(shell, () => ShowsText(dialog, Localizer.Get(StringKeys.DbTestFailed, ""))),
            "Test Connection did not say why it failed.");

        Named<TextBox>(dialog, "DatabaseFile").Text = missing;
        Click(Named<Button>(dialog, "DatabaseTest"));

        Assert.True(await WaitUntilAsync(shell, () => ShowsText(dialog, Localizer.Get(StringKeys.DatabaseNoFile, missing))),
            "A missing file was not named in the engine's words.");

        Click(Named<Button>(dialog, "DatabaseCancel"));
        await shell.SettleAsync(3);

        // Nothing was saved, and the explorer still offers the way to add one.
        Assert.Empty(shell.Explorer.Roots);
        Assert.True(ButtonLabelled(shell.Explorer, L(StringKeys.DbAddConnection)).IsEnabled);
        Assert.Empty(new SettingsStore(_settings).Load().Database.Connections);
    }

    // ------------------------------------------------------------------
    // Journey 13: a connection is saved and reopened; its password is not in the settings file.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheConnectionIsSavedAndReopenedAndItsPasswordIsNotInTheSettingsFile()
    {
        const string secret = "dummy-secret-7431";
        var passwords = new MemoryDatabasePasswordStore();

        using (var shell = new JourneyShell(passwords))
        {
            await shell.SettleAsync(5);
            await ShowExplorerAsync(shell);

            // A server kind: the dialog shows its host, port, database and login.
            var dialog = await OpenAddDialogAsync(shell);
            Named<TextBox>(dialog, "DatabaseName").Text = "Warehouse";
            ChooseKind(dialog, DatabaseKind.SqlServer);
            Named<TextBox>(dialog, "DatabaseServer").Text = "db.example.test";
            Named<TextBox>(dialog, "DatabasePort").Text = "1433";
            Named<TextBox>(dialog, "DatabaseDatabase").Text = "Stock";
            Named<TextBox>(dialog, "DatabaseUser").Text = "sa";
            Named<TextBox>(dialog, "DatabasePassword").Text = secret;

            Click(Named<Button>(dialog, "DatabaseOk"));
            Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 1));
        }

        var settingsText = File.ReadAllText(_settings);
        Assert.DoesNotContain(secret, settingsText);
        Assert.DoesNotContain("password", settingsText, StringComparison.OrdinalIgnoreCase);

        var saved = Assert.Single(new SettingsStore(_settings).Load().Database.Connections);
        Assert.Equal(nameof(DatabaseKind.SqlServer), saved.Kind);
        Assert.Contains("db.example.test", saved.ConnectionString, StringComparison.Ordinal);
        Assert.Equal(secret, passwords.Get(saved.Id));

        // Reopened: the connection is listed again, with the same settings.
        using var reopened = new JourneyShell(passwords);
        await reopened.SettleAsync(5);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(reopened.Explorer.Roots));
        Assert.Equal("Warehouse", connection.Text);
        Assert.Equal(saved.Id, connection.ConnectionId);
    }

    // ------------------------------------------------------------------
    // Review round 2: data loss, NULL, duplicate writes, stale sessions.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task QuittingWithPendingEditsAskedAndSaveWritesThem()
        => await QuitWithPendingEditAsync(L(StringKeys.SaveChangesSave), written: true, closes: true);

    [AvaloniaFact]
    public async Task QuittingWithPendingEditsAskedAndDontSaveDropsThem()
        => await QuitWithPendingEditAsync(L(StringKeys.SaveChangesDontSave), written: false, closes: true);

    [AvaloniaFact]
    public async Task QuittingWithPendingEditsAskedAndCancelKeepsTheWindowAndTheEdits()
        => await QuitWithPendingEditAsync(L(StringKeys.ButtonCancel), written: false, closes: false);

    private async Task QuitWithPendingEditAsync(string answer, bool written, bool closes)
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Quit Edit");
        Assert.True(view.HasUnsavedChanges);

        shell.Window.Close();

        var question = await WaitForDialogAsync(shell);
        Assert.NotNull(question);
        Click(ButtonLabelled(question!, answer));

        await shell.SettleAsync(5);

        Assert.Equal(
            written ? "Quit Edit" : "Rossi",
            FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);

        if (closes)
        {
            Assert.True(await WaitUntilAsync(shell, () => !shell.Window.IsVisible), "The window did not close.");
        }
        else
        {
            Assert.True(shell.Window.IsVisible, "Cancel closed the window.");
            Assert.True(view.HasUnsavedChanges, "Cancel dropped the edit.");
        }
    }

    [AvaloniaFact]
    public async Task ANullCellStaysNullUnlessItsTextIsTyped()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var grid = GridOf(view);

        // Verdi's city is NULL: opened and left without a keystroke, it is still NULL, and nothing is pending.
        grid.SelectedIndex = 2;
        grid.CurrentColumn = grid.Columns[2];
        await shell.SettleAsync(2);
        Assert.True(grid.BeginEdit());
        await shell.SettleAsync(2);
        Assert.True(grid.CommitEdit());
        await shell.SettleAsync(2);

        Assert.True(RowsOf(view)[2].Cells[2].IsNull, "Leaving the editor turned NULL into an empty text.");
        Assert.False(view.HasUnsavedChanges);

        // Set to NULL, from the cell's menu, takes a text cell to NULL.
        grid.SelectedIndex = 0;
        grid.CurrentColumn = grid.Columns[2];
        await shell.SettleAsync(2);

        var setNull = ContextItems(grid.ContextMenu!).Single(i => i.Header as string == L(StringKeys.DbSetNull));
        setNull.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.True(RowsOf(view)[0].Cells[2].IsNull);

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), view.StatusText);

        Assert.Equal(["2"], FileValues(database, "SELECT COUNT(*) FROM Customers WHERE City IS NULL"));
        Assert.Equal(["0"], FileValues(database, "SELECT COUNT(*) FROM Customers WHERE City = ''"));
    }

    [AvaloniaFact]
    public async Task ASecondSaveWhileTheFirstIsInFlightWritesOnce()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var grid = GridOf(view);

        Click(ButtonLabelled(view, L(StringKeys.DbAddRow)));
        await shell.SettleAsync(2);
        await EditCellAsync(shell, grid, 3, 1, "Once");

        // Two saves started together: the second is refused while the first is still writing.
        var first = view.SaveAsync();
        var second = view.SaveAsync();

        Assert.False(await second);
        Assert.True(await first);

        Assert.Equal(["1"], FileValues(database, "SELECT COUNT(*) FROM Customers WHERE Name = 'Once'"));
    }

    [AvaloniaFact]
    public async Task AFailedReloadAfterASaveDoesNotKeepTheEditsForAnotherTry()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Written");

        // The write goes through; the reload that follows it fails.
        relay.FailNextRead = true;
        Click(ButtonLabelled(view, L(StringKeys.DbSave)));

        Assert.True(await WaitUntilAsync(shell, () => view.StatusText.Contains("read failed on purpose", StringComparison.Ordinal)),
            view.StatusText);

        Assert.False(view.HasUnsavedChanges, "The written edits are still pending for a retry.");
        Assert.Equal("Written", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    [AvaloniaFact]
    public async Task ASaveThatEmptiesTheLastPageStepsBackToAPageThatHasRows()
    {
        var database = CreateShop(_root);
        AddItems(database, 450);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Items");
        var grid = GridOf(view);

        Click(ButtonLabelled(view, L(StringKeys.DbLast)));
        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 401–450 of 450"), view.RangeText);

        // The fifty rows of the last page are marked deleted and saved: the page they were on is now empty.
        for (var i = 0; i < 50; i++)
        {
            grid.SelectedIndex = i;
            Click(ButtonLabelled(view, L(StringKeys.DbDeleteRow)));
        }

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));

        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 201–400 of 400"), view.RangeText);
        Assert.Equal(200, RowsOf(view).Count);
    }

    [AvaloniaFact]
    public async Task AReadWhileASaveIsInFlightOnTheConnectionIsSerialized()
    {
        var database = CreateShop(_root);
        var exported = Path.Combine(_root, "during-save.csv");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);
        relay.BeforeApply = () => Task.Delay(400);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Slow");

        // The save is held up on the connection while an export reads the same table.
        var saving = view.SaveAsync();
        var reading = view.ExportCsvAsync(exported);

        Assert.True(await saving);
        Assert.Equal(3, await reading);

        Assert.Equal("Slow", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
        Assert.DoesNotContain("failed", view.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public async Task ReconnectAfterDisconnectReadsTheNextPageAgain()
    {
        var database = CreateShop(_root);
        AddItems(database, 450);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Items");
        Click(ButtonLabelled(view, L(StringKeys.DbNext)));
        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 201–400 of 450"));

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        SelectItem(shell, connection);
        Click(ButtonLabelled(shell.Explorer, L(StringKeys.DatabaseDisconnect)));
        Assert.True(await WaitUntilAsync(shell, () => !shell.Workspace.IsOpen(connection.ConnectionId)));

        // The open tab connects again, with the connection's settings, when it reads.
        Click(ButtonLabelled(view, L(StringKeys.DbNext)));

        Assert.True(await WaitUntilAsync(shell, () => view.RangeText == "rows 401–450 of 450"), view.StatusText);
        Assert.True(shell.Workspace.IsOpen(connection.ConnectionId));
        Assert.Equal(string.Empty, view.StatusText);
    }

    [AvaloniaFact]
    public async Task AConnectionWhoseFileIsGoneShowsAReadableErrorAndTheIdeStaysUsable()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        SqliteConnection.ClearAllPools();
        File.Delete(database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);

        var expected = Localizer.Get(StringKeys.DbOpenFailed, "Shop", Localizer.Get(StringKeys.DatabaseNoFile, database));
        Assert.True(await WaitUntilAsync(shell, () => connection.Children.Count == 1 && connection.Children[0].Text == expected),
            connection.Children.Count > 0 ? connection.Children[0].Text : "no message");

        Assert.Equal(expected, shell.Explorer.StatusText);

        // Nothing is stuck: the explorer still adds connections.
        Assert.True(ButtonLabelled(shell.Explorer, L(StringKeys.DbAddConnection)).IsEnabled);
    }

    [AvaloniaFact]
    public async Task ABadQueryIsRecoveredByTheNextQuery()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = "SELEC 1";
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.StatusText.StartsWith("Failed", StringComparison.Ordinal)));

        query.Sql = "SELECT 2 AS Two";
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.Results.Items.Count == 2), query.StatusText);

        await shell.SettleAsync(3);
        var grid = query.Results.GetVisualDescendants().OfType<DataGrid>().Single();
        Assert.Equal("2", ((IEnumerable<object>)grid.ItemsSource!).Cast<IReadOnlyList<object?>>().Single()[0]?.ToString());
    }

    [AvaloniaFact]
    public async Task TableCsvExportQuotesCommasQuotesNewlinesAndNulls()
    {
        var database = CreateShop(_root);
        ChangeOutside(database,
            "CREATE TABLE Notes (Id INTEGER PRIMARY KEY, Text TEXT);"
          + " INSERT INTO Notes (Id, Text) VALUES (1, 'a, b'), (2, 'say \"hi\"'), (3, 'line1' || char(10) || 'line2'), (4, NULL);");
        var exported = Path.Combine(_root, "notes.csv");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Notes");
        Assert.Equal(4, await view.ExportCsvAsync(exported));

        Assert.Equal(
            "Id,Text\r\n1,\"a, b\"\r\n2,\"say \"\"hi\"\"\"\r\n3,\"line1\nline2\"\r\n4,\r\n",
            File.ReadAllText(exported));
    }

    [AvaloniaFact]
    public async Task EditKeepsThePasswordClearRemovesItAndRemoveForgetsIt()
    {
        const string secret = "dummy-secret-7431";
        var passwords = new MemoryDatabasePasswordStore();

        using var shell = new JourneyShell(passwords);
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);

        var dialog = await OpenAddDialogAsync(shell);
        Named<TextBox>(dialog, "DatabaseName").Text = "Warehouse";
        ChooseKind(dialog, DatabaseKind.SqlServer);
        Named<TextBox>(dialog, "DatabaseServer").Text = "db.example.test";
        Named<TextBox>(dialog, "DatabaseUser").Text = "sa";
        Named<TextBox>(dialog, "DatabasePassword").Text = secret;
        Click(Named<Button>(dialog, "DatabaseOk"));
        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 1));

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var id = connection.ConnectionId;
        Assert.Equal(secret, passwords.Get(id));

        // Edit with the password field left empty: the stored password is kept.
        await EditThroughMenuAsync(shell, connection, name: "Renamed", clear: false);
        Assert.Equal("Renamed", Assert.Single(shell.Explorer.Roots).Text);
        Assert.Equal(secret, passwords.Get(id));

        // Edit with the clear box ticked: the stored password goes.
        await EditThroughMenuAsync(shell, connection, name: null, clear: true);
        Assert.Null(passwords.Get(id));

        // Remove: after confirmation the connection and anything it kept are gone.
        passwords.Set(id, secret);
        await RemoveThroughMenuAsync(shell, connection);

        Assert.Empty(shell.Explorer.Roots);
        Assert.Null(passwords.Get(id));
        Assert.Empty(new SettingsStore(_settings).Load().Database.Connections);
    }

    [AvaloniaFact]
    public async Task RemovingAConnectionMarksItsOpenTableTabAsRemoved()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));

        await RemoveThroughMenuAsync(shell, connection);

        Assert.True(view.IsRemoved);
        Assert.False(ButtonLabelled(view, L(StringKeys.DbSave)).IsEnabled);
        Assert.True(ShowsText(view, L(StringKeys.DbConnectionRemoved)));

        // Connected again, the same table opens in a new tab: the dead one is not refocused.
        await AddSqliteConnectionAsync(shell, "Shop", database);
        var reopened = await OpenTableFromExplorerAsync(shell, "Customers");
        Assert.NotSame(view, reopened);
    }

    [AvaloniaFact]
    public async Task SelectTopAndNewQueryFromTheExplorerUseTheConnection()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        await WaitUntilAsync(shell, () => connection.IsLoaded);

        var tables = Group(connection, StringKeys.DbTablesGroup, 2);
        await ExpandAsync(shell, tables);
        var customers = tables.Children.OfType<ObjectNode>().Single(o => o.Text == "Customers");

        // Select Top 1000: a new query, prefilled with the engine's own SELECT TOP.
        ContextAction(shell, customers, StringKeys.DbSelectTop).Invoke();
        Assert.True(await WaitUntilAsync(shell, () => shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Any()));

        var top = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Single();
        Assert.Equal(customers.Session.Dialect.SelectTop(customers.Name, 1000), top.Sql);

        // New Query from the connection: an empty query on that connection.
        ContextAction(shell, connection, StringKeys.DbNewQuery).Invoke();
        Assert.True(await WaitUntilAsync(shell, () => shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Count() == 2));

        var fresh = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Last();
        Assert.Equal(string.Empty, fresh.Sql);
        Assert.Equal("Shop", Named<ComboBox>(fresh, "QueryConnection").SelectedItem?.ToString());
    }

    [AvaloniaFact]
    public async Task SaveAsWritesTheFileCtrlSSavesItAndF5AndCtrlEnterRun()
    {
        var database = CreateShop(_root);
        var file = Path.Combine(_root, "saved.sql");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = "SELECT 1 AS One";
        Assert.True(await query.SaveToAsync(file));

        Assert.Equal(file, query.FilePath);
        Assert.Equal(Localizer.Get(StringKeys.DbSavedQuery, "saved.sql"), query.StatusText);
        Assert.Equal("saved.sql", shell.Window.DatabaseDocumentsForTests.Single(d => d.View == query).Title);

        // Ctrl+S in the query writes the file, from the keyboard.
        query.Sql = "SELECT 2 AS Two";
        query.Editor.TextArea.Focus();
        await shell.SettleAsync(2);
        PressKey(query.Editor.TextArea, Key.S, KeyModifiers.Control);
        Assert.True(await WaitUntilAsync(shell, () => File.ReadAllText(file) == "SELECT 2 AS Two"));

        // Ctrl+Enter runs it, and F5 runs it again.
        PressKey(query.Editor.TextArea, Key.Enter, KeyModifiers.Control);
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && query.Results.Items.Count == 2), query.StatusText);

        // A different query, so the result the test waits for can only come from F5.
        query.Sql = "SELECT 3 AS Three";
        query.Results.SelectedIndex = 0;
        PressKey(query.Editor.TextArea, Key.F5, KeyModifiers.None);
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning
            && query.Results.GetVisualDescendants().OfType<DataGrid>().Any(g => g.Columns.Any(c => c.Header as string == "Three"))),
            query.StatusText);
    }

    [AvaloniaFact]
    public async Task CtrlSInATableTabSavesItsEdits()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        var grid = GridOf(view);
        await EditCellAsync(shell, grid, 0, 1, "Keyed");

        grid.Focus();
        await shell.SettleAsync(2);
        PressKey(grid, Key.S, KeyModifiers.Control);

        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), view.StatusText);
        Assert.Equal("Keyed", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    [AvaloniaFact]
    public async Task ASqlFileOpenedAnywhereIsAQueryDocumentAndAnotherFileIsNot()
    {
        var sql = Path.Combine(_root, "report.sql");
        await File.WriteAllTextAsync(sql, "SELECT 42 AS Answer");
        var text = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(text, "plain");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        var model = (MainWindowViewModel)shell.Window.DataContext!;

        // Open the way Solution Explorer and File > Open open a file.
        await model.OpenFileAsync(sql);
        await shell.SettleAsync(3);

        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
        Assert.Equal(sql, query.FilePath);
        Assert.Equal("SELECT 42 AS Answer", query.Sql);
        Assert.DoesNotContain(model.OpenDocuments, d => d.FilePath == sql);

        await model.OpenFileAsync(text);
        Assert.Contains(model.OpenDocuments, d => d.FilePath == text);
        Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
    }

    [AvaloniaFact]
    public async Task AMalformedConnectionStringKeepsTheDialogOpenAndSaysWhy()
    {
        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);

        var dialog = await OpenAddDialogAsync(shell);
        Named<TextBox>(dialog, "DatabaseName").Text = "Broken";
        Named<Expander>(dialog, "DatabaseAdvancedExpander").IsExpanded = true;
        await shell.SettleAsync(2);
        Named<TextBox>(dialog, "DatabaseAdvanced").Text = "Data Source=\"unterminated";
        Click(Named<Button>(dialog, "DatabaseOk"));

        await shell.SettleAsync(3);
        Assert.True(dialog.IsVisible, "The dialog closed on a string it could not read.");
        Assert.True(ShowsText(dialog, Localizer.Get(StringKeys.DbBadConnectionString, "")));
        Assert.Empty(shell.Explorer.Roots);
    }

    [Fact]
    public void TheKeychainKeepsEachConnectionsPasswordUnderItsOwnAccount()
    {
        Assert.Equal("database.abc", KeychainDatabasePasswordStore.AccountOf("abc"));
        Assert.NotEqual(KeychainDatabasePasswordStore.AccountOf("abc"), KeychainDatabasePasswordStore.AccountOf("abd"));
    }

    /// <summary>The context menu's items, as the window built them.</summary>
    private static IEnumerable<MenuItem> ContextItems(ContextMenu menu) =>
        ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>();

    /// <summary>The explorer's context action for a node, found by its header, as the menu would invoke it.</summary>
    private static MenuAction ContextAction(JourneyShell shell, ExplorerNode node, string key) =>
        shell.Explorer.MenuActionsForTests(node).Single(a => a.Header == L(key));

    private static async Task EditThroughMenuAsync(JourneyShell shell, ConnectionNode connection, string? name, bool clear)
    {
        ContextAction(shell, connection, StringKeys.DbEditConnection).Invoke();

        var dialog = await WaitForDialogAsync(shell);
        Assert.NotNull(dialog);

        if (name is not null) Named<TextBox>(dialog!, "DatabaseName").Text = name;
        Named<CheckBox>(dialog!, "DatabaseClearPassword").IsChecked = clear;

        Click(Named<Button>(dialog!, "DatabaseOk"));
        await shell.SettleAsync(5);
    }

    private static async Task RemoveThroughMenuAsync(JourneyShell shell, ConnectionNode connection)
    {
        ContextAction(shell, connection, StringKeys.DbRemoveConnection).Invoke();

        var question = await WaitForDialogAsync(shell);
        Assert.NotNull(question);
        Click(Named<Button>(question!, "DatabaseConfirm"));

        await shell.SettleAsync(5);
    }

    /// <summary>Gives a connection a session the test can make fail, wrapping a real one.</summary>
    private static async Task<RelaySession> RelayAsync(JourneyShell shell, string database, ConnectionNode connection)
    {
        var real = await DatabaseProviders.Get(DatabaseKind.Sqlite).OpenAsync($"Data Source={database}");
        var relay = new RelaySession(real);

        shell.Workspace.AdoptSessionForTests(connection.ConnectionId, relay);

        return relay;
    }

    /// <summary>Presses a key on a control, as the keyboard would, from the top of the route down.</summary>
    private static void PressKey(Interactive target, Key key, KeyModifiers modifiers) =>
        target.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    /// <summary>A session that passes everything to a real one, and can be made to fail or to wait.</summary>
    private sealed class RelaySession(IDatabaseSession inner) : IDatabaseSession
    {
        /// <summary>The next page read fails, as a broken connection does.</summary>
        public bool FailNextRead { get; set; }

        /// <summary>Runs before each write; a test holds the write up here.</summary>
        public Func<Task>? BeforeApply { get; set; }

        /// <summary>For each page read, whether it was made on the interface thread.</summary>
        public List<bool> PageReadsOnInterfaceThread { get; } = [];

        /// <summary>How many times the session was disposed.</summary>
        public int Disposals { get; private set; }

        /// <summary>When set, the session fails to close after the engine's session is closed.</summary>
        public Exception? DisposeFailure { get; set; }

        public DatabaseKind Kind => inner.Kind;

        public string DisplayName => inner.DisplayName;

        public ISqlDialect Dialect => inner.Dialect;

        public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default) => inner.GetObjectsAsync(ct);

        public Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default) => inner.GetDetailsAsync(name, ct);

        public Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10_000, CancellationToken ct = default) =>
            inner.ExecuteAsync(sql, maxRows, ct);

        public Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default)
        {
            PageReadsOnInterfaceThread.Add(Dispatcher.UIThread.CheckAccess());

            if (!FailNextRead) return inner.ReadPageAsync(request, ct);

            FailNextRead = false;

            return Task.FromException<DataPage>(new IOException("The read failed on purpose."));
        }

        public async Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default)
        {
            if (BeforeApply is { } wait) await wait();

            return await inner.ApplyEditsAsync(table, edits, ct);
        }

        public async ValueTask DisposeAsync()
        {
            Disposals++;

            await inner.DisposeAsync();

            if (DisposeFailure is { } failure) throw failure;
        }
    }

    // ------------------------------------------------------------------
    // Review round 3: reconnect keeps edits, stale tabs, per-connection gates,
    // close releases the databases, passwords out of messages, session tabs.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ReconnectAfterAFailedSaveKeepsThePendingEditsAndSaveWritesThem()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Kept");

        // The connection drops during the write, once.
        relay.BeforeApply = () =>
        {
            relay.BeforeApply = null;
            return Task.FromException(new IOException("The connection was lost."));
        };

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));

        Assert.True(await WaitUntilAsync(shell, () => view.StatusText.Contains("connection was lost", StringComparison.Ordinal)),
            view.StatusText);
        Assert.True(view.HasUnsavedChanges);
        Assert.True(ButtonLabelled(view, L(StringKeys.DbReconnect)).IsVisible);

        Click(ButtonLabelled(view, L(StringKeys.DbReconnect)));

        Assert.True(await WaitUntilAsync(shell, () => view.StatusText == L(StringKeys.DbReconnectedKeepsEdits)), view.StatusText);
        Assert.True(view.HasUnsavedChanges, "Reconnect dropped the pending edits.");
        Assert.Equal("Kept", Cell(RowsOf(view)[0], 1));

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), view.StatusText);
        Assert.Equal("Kept", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    [AvaloniaFact]
    public async Task EditingAConnectionStopsItsOpenTableFromWritingToTheOldRows()
    {
        var database = CreateShop(_root);
        var other = Path.Combine(_root, "other.db");
        ChangeOutside(other, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Customers (Name) VALUES ('Elsewhere');");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Pending");

        // The connection now points at another file, while the tab still shows the old one's rows.
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        ContextAction(shell, connection, StringKeys.DbEditConnection).Invoke();

        var dialog = await WaitForDialogAsync(shell);
        Assert.NotNull(dialog);
        Named<TextBox>(dialog!, "DatabaseFile").Text = other;
        Click(Named<Button>(dialog!, "DatabaseOk"));

        Assert.True(await WaitUntilAsync(shell, () => view.IsStale), "The open table was not marked stale.");
        Assert.False(ButtonLabelled(view, L(StringKeys.DbSave)).IsEnabled);
        Assert.True(ShowsText(view, L(StringKeys.DbConnectionChanged)));

        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
        Assert.Equal("Elsewhere", FileValues(other, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    [AvaloniaFact]
    public async Task ASlowConnectToOneConnectionDoesNotStallAnother()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var dialog = await OpenAddDialogAsync(shell);
        Named<TextBox>(dialog, "DatabaseName").Text = "Slow";
        ChooseKind(dialog, DatabaseKind.SqlServer);
        Named<TextBox>(dialog, "DatabaseServer").Text = "slow.example.test";
        Click(Named<Button>(dialog, "DatabaseOk"));
        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 2));

        var slow = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.Text == "Slow");
        var shop = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.Text == "Shop");

        // The server's provider never answers; SQLite's answers as usual.
        shell.Workspace.UseProvidersForTests(kind => kind == DatabaseKind.SqlServer
            ? new NeverAnsweringProvider()
            : DatabaseProviders.Get(kind));

        using var cancel = new CancellationTokenSource();
        var hung = shell.Workspace.OpenAsync(slow.ConnectionId, cancel.Token);
        var fine = shell.Workspace.OpenAsync(shop.ConnectionId);

        var finished = await Task.WhenAny(fine, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.Same(fine, finished);
        Assert.False(hung.IsCompleted, "The slow connect ended before it was cancelled.");

        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hung);
    }

    [AvaloniaFact]
    public async Task AFailedServerLoginDoesNotShowThePassword()
    {
        const string secret = "dummy-secret-7431";

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);

        var dialog = await OpenAddDialogAsync(shell);
        Named<TextBox>(dialog, "DatabaseName").Text = "Warehouse";
        ChooseKind(dialog, DatabaseKind.SqlServer);
        Named<TextBox>(dialog, "DatabaseServer").Text = "db.example.test";
        Named<TextBox>(dialog, "DatabaseUser").Text = "sa";
        Named<TextBox>(dialog, "DatabasePassword").Text = secret;
        Click(Named<Button>(dialog, "DatabaseOk"));
        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 1));

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));

        // The server rejects the password, and its message quotes it.
        shell.Workspace.UseProvidersForTests(kind => kind == DatabaseKind.SqlServer
            ? new RejectingProvider(secret)
            : DatabaseProviders.Get(kind));

        await ExpandAsync(shell, connection);
        Assert.True(await WaitUntilAsync(shell, () => connection.Children.Count == 1
            && connection.Children[0].Text.Contains("Login failed", StringComparison.Ordinal)),
            connection.Children.Count > 0 ? connection.Children[0].Text : "no message");

        Assert.DoesNotContain(secret, connection.Children[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, shell.Explorer.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task ClosingTheWindowCancelsARunningQueryAndDisposesTheSession()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = SlowCountQuery;
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => query.IsRunning), "The slow batch did not start.");

        // The batch is marked running before its connection opens: wait until the session exists.
        var id = ((ConnectionNode)Assert.Single(shell.Explorer.Roots)).ConnectionId;
        Assert.True(await WaitUntilAsync(shell, () => shell.Workspace.IsOpen(id)), "The session never opened.");

        shell.Window.Close();

        // The untitled query has text the file does not have, so the window asks first: Don't save keeps the text out of it.
        var question = await WaitForDialogAsync(shell);
        Assert.NotNull(question);
        Click(ButtonLabelled(question!, L(StringKeys.SaveChangesDontSave)));

        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning && !shell.Workspace.IsOpen(id), attempts: 300),
            "Closing the window did not stop the batch and dispose the session. Running: " + query.IsRunning
            + ", open: " + shell.Workspace.IsOpen(id) + ", visible: " + shell.Window.IsVisible
            + ", status: " + query.StatusText);
        Assert.True(await WaitUntilAsync(shell, () => !shell.Window.IsVisible), "The window did not close.");

        // Cancelled, not cut off: a session disposed under the batch would report an engine error instead.
        Assert.StartsWith(L(StringKeys.DatabaseStopped), query.StatusText, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AQueryFileOpenedAsAQueryIsRecordedInTheSolutionSession()
    {
        var sql = Path.Combine(_root, "recorded.sql");
        await File.WriteAllTextAsync(sql, "SELECT 7 AS Seven");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        var model = (MainWindowViewModel)shell.Window.DataContext!;

        await model.OpenFileAsync(sql);
        await shell.SettleAsync(3);

        Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
        Assert.Contains(model.CaptureSession().OpenTabs, tab => tab.Path == sql);
    }

    // ------------------------------------------------------------------
    // Review round 4: a stale tab writes nothing, reconnect really connects,
    // a left solution takes its query tabs, exports leave the interface
    // thread and never leave a half file, a change holds its connection,
    // a failed change keeps the stored password, a close waits for nothing.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task AStaleTableTabWritesNothingAndDiscardDropsItsEdits()
    {
        var database = CreateShop(_root);
        var other = Path.Combine(_root, "stale-other.db");
        ChangeOutside(other, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Customers (Name) VALUES ('Elsewhere');");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Pending");

        // The connection now points at the other file: the tab is stale, and its edit is still pending.
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        ContextAction(shell, connection, StringKeys.DbEditConnection).Invoke();

        var dialog = await WaitForDialogAsync(shell);
        Assert.NotNull(dialog);
        Named<TextBox>(dialog!, "DatabaseFile").Text = other;
        Click(Named<Button>(dialog!, "DatabaseOk"));
        Assert.True(await WaitUntilAsync(shell, () => view.IsStale), "The open table was not marked stale.");
        Assert.True(view.HasUnsavedChanges);

        // Ctrl+S makes this call: the save is refused, and neither file is written.
        view.SaveFromKeyboard();
        await shell.SettleAsync(3);

        Assert.True(view.HasUnsavedChanges, "A stale tab wrote its pending edit.");
        Assert.Equal(L(StringKeys.DbConnectionChanged), view.StatusText);
        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
        Assert.Equal("Elsewhere", FileValues(other, "SELECT Name FROM Customers ORDER BY Id")[0]);

        // Discard still drops the edit, from the rows the tab shows.
        Click(ButtonLabelled(view, L(StringKeys.DbDiscard)));

        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), "Discard left the pending edit in place.");
        Assert.Equal("Rossi", Cell(RowsOf(view)[0], 1));
    }

    [AvaloniaFact]
    public async Task ASaveThatRacesAnEditOfItsConnectionWritesNothing()
    {
        // The edit is saved while the tab still believes its connection unchanged: the explorer marks tabs
        // stale only after the edit returns, and a Save pressed meanwhile passed that check already. The save
        // must still see the edit, or the old table's keys are written to the database the connection now names.
        var database = CreateShop(_root);
        var other = Path.Combine(_root, "race-other.db");
        var exported = Path.Combine(_root, "race-export.csv");
        ChangeOutside(other, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Customers (Name) VALUES ('Elsewhere');");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Pending");

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await shell.Window.DatabaseWorkspaceForTests.UpdateAsync(
            connection.ConnectionId, DatabaseDraft.Empty(DatabaseKind.Sqlite) with { Name = "Shop", FilePath = other });
        Assert.False(view.IsStale, "The test needs a tab not yet told about the edit.");

        // Neither the export nor the save may read or write the database the connection now names.
        Assert.Equal(0, await view.ExportCsvAsync(exported));
        Assert.False(File.Exists(exported), "An export under an edited connection wrote the other database's rows.");

        Assert.False(await view.SaveAsync(), "A save under an edited connection reported success.");

        Assert.True(view.IsStale);
        Assert.True(view.HasUnsavedChanges);
        Assert.Equal("Rossi", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
        Assert.Equal("Elsewhere", FileValues(other, "SELECT Name FROM Customers ORDER BY Id")[0]);

        // Once stale, the flag alone refuses the export too.
        Assert.Equal(0, await view.ExportCsvAsync(exported));
        Assert.False(File.Exists(exported), "A stale tab exported rows.");
    }

    [AvaloniaFact]
    public async Task ReconnectOpensANewSessionAndKeepsThePendingEdits()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await EditCellAsync(shell, GridOf(view), 0, 1, "Kept");

        relay.BeforeApply = () =>
        {
            relay.BeforeApply = null;
            return Task.FromException(new IOException("The connection was lost."));
        };

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => view.StatusText.Contains("connection was lost", StringComparison.Ordinal)), view.StatusText);
        Assert.True(ButtonLabelled(view, L(StringKeys.DbReconnect)).IsVisible);

        // Reconnect has to open a session of its own: the cached one is the connection that failed.
        var provider = new CountingProvider(DatabaseProviders.Get(DatabaseKind.Sqlite));
        shell.Workspace.UseProvidersForTests(_ => provider);

        Click(ButtonLabelled(view, L(StringKeys.DbReconnect)));

        Assert.True(await WaitUntilAsync(shell, () => view.StatusText == L(StringKeys.DbReconnectedKeepsEdits)), view.StatusText);
        Assert.Equal(1, provider.Opens);
        Assert.True(view.HasUnsavedChanges, "Reconnect dropped the pending edits.");
        Assert.Equal("Kept", Cell(RowsOf(view)[0], 1));

        Click(ButtonLabelled(view, L(StringKeys.DbSave)));
        Assert.True(await WaitUntilAsync(shell, () => !view.HasUnsavedChanges), view.StatusText);
        Assert.Equal("Kept", FileValues(database, "SELECT Name FROM Customers ORDER BY Id")[0]);
    }

    [AvaloniaFact]
    public async Task ASolutionLeftTakesItsQueryTabsWithIt()
    {
        var sql = Path.Combine(_root, "solution-a.sql");
        await File.WriteAllTextAsync(sql, "SELECT 1 AS One");
        var first = WriteProject(Path.Combine(_root, "first"));
        var second = WriteProject(Path.Combine(_root, "second"));

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        var model = (MainWindowViewModel)shell.Window.DataContext!;

        await model.OpenSolutionAsync(first);
        await shell.SettleAsync(3);
        await model.OpenFileAsync(sql);
        await shell.SettleAsync(3);
        Assert.Contains(model.CaptureSession().OpenTabs, tab => tab.Path == sql);

        await model.OpenSolutionAsync(second);
        await shell.SettleAsync(3);

        Assert.Empty(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
        Assert.DoesNotContain(model.CaptureSession().OpenTabs, tab => tab.Path == sql);
    }

    [AvaloniaFact]
    public async Task ASolutionLeftStopsTheBatchItsQueryTabWasRunning()
    {
        // The tab goes with the solution; its batch must go too, or it holds the connection, unseen, until
        // it ends on its own. Reintroduced once (the tab closed without CancelRun): the batch ran on.
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);
        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());

        query.Sql = SlowCountQuery;
        Click(Named<Button>(query, "QueryRun"));
        Assert.True(await WaitUntilAsync(shell, () => query.IsRunning), "The slow batch did not start.");

        // What leaving a solution does once its query tabs with changes have been answered for.
        var started = Stopwatch.StartNew();
        ((MainWindowViewModel)shell.Window.DataContext!).CloseAllDocuments();

        Assert.Empty(shell.Window.DatabaseDocumentsForTests);
        Assert.True(await WaitUntilAsync(shell, () => !query.IsRunning, attempts: 300), "The closed tab's batch is still running.");
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(20), "The batch ran on after its tab closed.");
    }

    [AvaloniaFact]
    public async Task ATableExportReadsItsPagesOffTheInterfaceThread()
    {
        var database = CreateShop(_root);
        var exported = Path.Combine(_root, "export-off-thread.csv");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);
        var view = await OpenTableFromExplorerAsync(shell, "Customers");

        relay.PageReadsOnInterfaceThread.Clear();

        Assert.Equal(3, await view.ExportCsvAsync(exported));

        Assert.NotEmpty(relay.PageReadsOnInterfaceThread);
        Assert.DoesNotContain(true, relay.PageReadsOnInterfaceThread);

        // Whole: the header and three rows, each ended by its line break.
        Assert.Equal(4, File.ReadAllLines(exported).Length);
        Assert.EndsWith("\r\n", File.ReadAllText(exported), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task AFailedExportLeavesTheFileItWouldReplaceWholeAndNoTemporaryFile()
    {
        var database = CreateShop(_root);
        var folder = Path.Combine(_root, "exports");
        Directory.CreateDirectory(folder);
        var exported = Path.Combine(folder, "customers.csv");
        await File.WriteAllTextAsync(exported, "previous export");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var relay = await RelayAsync(shell, database, connection);
        var view = await OpenTableFromExplorerAsync(shell, "Customers");

        // The read that follows the header fails: the export has written part of the file by then.
        relay.FailNextRead = true;

        await Assert.ThrowsAsync<IOException>(() => view.ExportCsvAsync(exported));

        Assert.Equal("previous export", File.ReadAllText(exported));
        Assert.Equal([exported], Directory.GetFiles(folder));
    }

    [AvaloniaFact]
    public async Task AViewThatHasReadNothingExportsNothing()
    {
        var database = CreateShop(_root);
        var exported = Path.Combine(_root, "unread.csv");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        var unread = new TableDataView(shell.Workspace, connection.ConnectionId, new DbObjectName(null, "Customers"), DbObjectKind.Table);

        Assert.Equal(0, await unread.ExportCsvAsync(exported));
        Assert.False(File.Exists(exported));
    }

    [AvaloniaFact]
    public async Task AConnectWaitsForAnEditOfItsConnectionAndOpensTheNewSettings()
    {
        var database = CreateShop(_root);
        var other = Path.Combine(_root, "edited-other.db");
        ChangeOutside(other, "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Customers (Name) VALUES ('Elsewhere');");

        var passwords = new HeldPasswords();

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        shell.Workspace.UsePasswords(passwords);
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var id = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots)).ConnectionId;

        // The edit stops at its password change: its connection is closed by then, its new settings not yet in place.
        passwords.HoldNextChange();
        var edit = shell.Workspace.UpdateAsync(id, DatabaseDraft.Empty(DatabaseKind.Sqlite) with { Name = "Shop", FilePath = other });
        Assert.True(await Task.Run(() => passwords.Reached.Wait(TimeSpan.FromSeconds(20))), "The edit never reached the password store.");

        // A connect started now waits for the edit: it must not open the file the connection pointed at before.
        var connect = shell.Workspace.OpenAsync(id);
        await Task.Delay(300);
        Assert.False(connect.IsCompleted, "A connect ran while its connection was being changed.");

        passwords.Release.Set();
        await edit;

        var session = await connect;
        var result = await session.ExecuteAsync("SELECT Name FROM Customers ORDER BY Id");

        Assert.Equal("Elsewhere", result.ResultSets.Single().Rows.Single()[0]?.ToString());
    }

    [AvaloniaFact]
    public async Task AnEditTheSettingsCannotHoldKeepsTheStoredPassword()
    {
        var passwords = new RecordingPasswords();

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        shell.Workspace.UsePasswords(passwords);
        await shell.SettleAsync(5);

        var saved = await shell.Workspace.AddAsync(DatabaseDraft.Empty(DatabaseKind.SqlServer) with
        {
            Name = "Warehouse",
            Server = "db.example.test",
            User = "sa",
            Password = "first-password",
        });
        Assert.Equal("first-password", passwords.Get(saved.Id));

        shell.Workspace.UseSettingsSaveForTests(_ => false);

        var edit = DatabaseDraft.Empty(DatabaseKind.SqlServer) with
        {
            Name = "Renamed",
            Server = "db.example.test",
            User = "sa",
            Password = "second-password",
        };

        var failure = await Assert.ThrowsAsync<IOException>(() => shell.Workspace.UpdateAsync(saved.Id, edit));

        Assert.Equal(L(StringKeys.DbSettingsNotSaved), failure.Message);
        Assert.Equal("first-password", passwords.Get(saved.Id));
        Assert.Equal("Warehouse", shell.Workspace.Find(saved.Id)!.Name);
    }

    [AvaloniaFact]
    public async Task AnAddTheSettingsCannotHoldLeavesNoPasswordBehind()
    {
        var passwords = new RecordingPasswords();

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        shell.Workspace.UsePasswords(passwords);
        await shell.SettleAsync(5);

        shell.Workspace.UseSettingsSaveForTests(_ => false);

        var draft = DatabaseDraft.Empty(DatabaseKind.SqlServer) with
        {
            Name = "Orphan",
            Server = "db.example.test",
            User = "sa",
            Password = "orphan-password",
        };

        var failure = await Assert.ThrowsAsync<IOException>(() => shell.Workspace.AddAsync(draft));

        Assert.Equal(L(StringKeys.DbSettingsNotSaved), failure.Message);
        Assert.Equal(0, passwords.Count);
        Assert.Empty(shell.Workspace.Connections);
    }

    [AvaloniaFact]
    public async Task ASessionThatFinishesOpeningAfterTheWorkspaceClosedIsClosedNotKept()
    {
        var database = CreateShop(_root);

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var id = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots)).ConnectionId;

        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new GatedProvider(DatabaseProviders.Get(DatabaseKind.Sqlite), release.Task);
        shell.Workspace.UseProvidersForTests(_ => provider);

        var opening = shell.Workspace.OpenAsync(id);
        Assert.True(await WaitUntilAsync(shell, () => provider.Entered), "The connect never started.");

        // The window closes while the connect is still running, then the connect finishes.
        await shell.Workspace.DisposeAsync();
        release.SetResult();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => opening);
        Assert.False(shell.Workspace.IsOpen(id));
        Assert.Equal(1, provider.Opened!.Disposals);
    }

    [AvaloniaFact]
    public async Task ASessionThatWillNotCloseDoesNotKeepTheOthersOpen()
    {
        var database = CreateShop(_root);
        var second = Path.Combine(_root, "second-open.db");
        ChangeOutside(second, "CREATE TABLE Notes (Id INTEGER PRIMARY KEY);");

        using var shell = new JourneyShell(new MemoryDatabasePasswordStore());
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);
        await AddSqliteConnectionAsync(shell, "Second", second);
        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 2), "The second connection was not listed.");

        var shop = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.Text == "Shop");
        var other = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.Text == "Second");

        var stuck = await RelayAsync(shell, database, shop);
        stuck.DisposeFailure = new IOException("The close failed on purpose.");
        var fine = await RelayAsync(shell, second, other);

        var failure = await Assert.ThrowsAsync<AggregateException>(() => shell.Workspace.DisposeAsync().AsTask());

        Assert.Single(failure.InnerExceptions);
        Assert.Equal(1, stuck.Disposals);
        Assert.Equal(1, fine.Disposals);
    }

    [Fact]
    public async Task ATestOfAConnectionDoesNotShowThePasswordTypedInTheAdvancedText()
    {
        const string secret = "dummy-advanced-4417";

        var draft = DatabaseDraft.Empty(DatabaseKind.SqlServer) with
        {
            Name = "Advanced",
            Advanced = "Server=db.example.test;User Id=sa;Password=" + secret,
        };

        // The password field is empty: the password is only in the text, and the server's message quotes it.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DatabaseWorkspace.TestAsync(draft, new RejectingProvider(secret), CancellationToken.None));

        Assert.Contains("Login failed", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(secret, failure.Message, StringComparison.Ordinal);
    }

    /// <summary>A provider whose connect finishes when the test releases it, to hold a connect open across a close.</summary>
    private sealed class GatedProvider(IDatabaseProvider inner, Task release) : IDatabaseProvider
    {
        private volatile bool _entered;

        public DatabaseKind Kind => inner.Kind;

        /// <summary>Whether a connect has started.</summary>
        public bool Entered => _entered;

        /// <summary>The session the connect produced, once it has.</summary>
        public RelaySession? Opened { get; private set; }

        public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
        {
            _entered = true;

            var session = new RelaySession(await inner.OpenAsync(connectionString, ct));
            Opened = session;

            await release.WaitAsync(ct);

            return session;
        }
    }

    /// <summary>A provider that passes each connect to a real one, and counts them.</summary>
    private sealed class CountingProvider(IDatabaseProvider inner) : IDatabaseProvider
    {
        private int _opens;

        public int Opens => Volatile.Read(ref _opens);

        public DatabaseKind Kind => inner.Kind;

        public Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _opens);

            return inner.OpenAsync(connectionString, ct);
        }
    }

    /// <summary>A password store that holds one change at the point it is made, so a test can see what runs meanwhile.</summary>
    private sealed class HeldPasswords : IDatabasePasswordStore
    {
        private readonly MemoryDatabasePasswordStore _inner = new();
        private volatile bool _holdNext;

        /// <summary>Set when the held change has been reached.</summary>
        public ManualResetEventSlim Reached { get; } = new(false);

        /// <summary>Set by the test to let the held change go on.</summary>
        public ManualResetEventSlim Release { get; } = new(false);

        public void HoldNextChange() => _holdNext = true;

        public string? Get(string connectionId) => _inner.Get(connectionId);

        public bool Set(string connectionId, string password)
        {
            Hold();

            return _inner.Set(connectionId, password);
        }

        public bool Remove(string connectionId)
        {
            Hold();

            return _inner.Remove(connectionId);
        }

        private void Hold()
        {
            if (!_holdNext) return;

            _holdNext = false;
            Reached.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
        }
    }

    /// <summary>A password store that keeps what it is given, so a test can count what is left in it.</summary>
    private sealed class RecordingPasswords : IDatabasePasswordStore
    {
        private readonly Dictionary<string, string> _passwords = new(StringComparer.Ordinal);

        public int Count => _passwords.Count;

        public string? Get(string connectionId) => _passwords.GetValueOrDefault(connectionId);

        public bool Set(string connectionId, string password)
        {
            _passwords[connectionId] = password;

            return true;
        }

        public bool Remove(string connectionId) => _passwords.Remove(connectionId);
    }

    /// <summary>A provider whose connect never completes, until its caller gives up.</summary>
    private sealed class NeverAnsweringProvider : IDatabaseProvider
    {
        public DatabaseKind Kind => DatabaseKind.SqlServer;

        public async Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);

            throw new InvalidOperationException("never reached");
        }
    }

    /// <summary>A provider that refuses the login, and quotes the password in its message, as some engines do.</summary>
    private sealed class RejectingProvider(string secret) : IDatabaseProvider
    {
        public DatabaseKind Kind => DatabaseKind.SqlServer;

        public Task<IDatabaseSession> OpenAsync(string connectionString, CancellationToken ct = default) =>
            Task.FromException<IDatabaseSession>(new InvalidOperationException(
                $"Login failed for user 'sa': the password {secret} was rejected."));
    }

    // ------------------------------------------------------------------
    // Shared setup and helpers
    // ------------------------------------------------------------------

    private static string L(string key) => Localizer.Get(key);

    /// <summary>A folder with one Visual Basic project, which a solution opens from. Returns the project's path.</summary>
    private static string WriteProject(string folder)
    {
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, "App.vbproj");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(folder, "Program.vb"), "Module Program\n    Sub Main()\n    End Sub\nEnd Module\n");

        return path;
    }

    /// <summary>A SQLite file with two tables, a view, an index and a foreign key.</summary>
    private static string CreateShop(string root)
    {
        var path = Path.Combine(root, "shop.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);
            CREATE INDEX IX_Customers_City ON Customers (City);
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY,
                CustomerId INTEGER NOT NULL REFERENCES Customers (Id),
                Amount REAL CHECK (Amount > 0));
            CREATE VIEW ActiveCustomers AS SELECT Id, Name FROM Customers;
            INSERT INTO Customers (Name, City) VALUES ('Rossi', 'Milano'), ('Bianchi', 'Roma'), ('Verdi', NULL);
            INSERT INTO Orders (CustomerId, Amount) VALUES (1, 10.5), (2, 3.0);
            """;
        command.ExecuteNonQuery();

        return path;
    }

    /// <summary>A table of <paramref name="count"/> rows, labelled "item 1" and so on.</summary>
    private static void AddItems(string database, int count)
    {
        ChangeOutside(database,
            "CREATE TABLE Items (Id INTEGER PRIMARY KEY, Label TEXT);"
          + $" INSERT INTO Items (Label) WITH RECURSIVE c(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM c WHERE n < {count}) SELECT 'item ' || n FROM c;");
    }

    /// <summary>Runs SQL against the file from outside the IDE, as another program would.</summary>
    private static void ChangeOutside(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    /// <summary>The first column of a file's query, as text.</summary>
    private static List<string> FileValues(string database, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={database}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var values = new List<string>();

        while (reader.Read()) values.Add(reader.IsDBNull(0) ? "NULL" : reader.GetValue(0).ToString()!);

        return values;
    }

    /// <summary>A very slow count, which only Stop or Disconnect will end.</summary>
    private const string SlowCountQuery =
        "SELECT COUNT(*) FROM (WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 1000000000) SELECT x FROM c)";

    /// <summary>Shows the Database Explorer in its tool slot, as View &gt; Database Explorer does.</summary>
    private static async Task ShowExplorerAsync(JourneyShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.MenuView), L(StringKeys.DbExplorerTitle));

        // The dock builds a tool's content on its first layout pass, so the tree exists only after the window settles.
        await shell.SettleAsync(5);
    }

    /// <summary>Opens the dialog from the Database menu and fills in a SQLite file.</summary>
    private static async Task AddSqliteConnectionAsync(JourneyShell shell, string name, string path)
    {
        var dialog = await OpenAddDialogAsync(shell);

        Named<TextBox>(dialog, "DatabaseName").Text = name;
        Named<TextBox>(dialog, "DatabaseFile").Text = path;
        Click(Named<Button>(dialog, "DatabaseOk"));

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count > 0), "The connection was not saved.");
    }

    private static async Task<DatabaseConnectionDialog> OpenAddDialogAsync(JourneyShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbAddConnection));
        await shell.SettleAsync(3);

        var dialog = await WaitForDialogAsync(shell);

        // A dialog that does not open says what the explorer last reported, which is where a failure lands.
        Assert.True(dialog is DatabaseConnectionDialog, "The connection dialog did not open. Explorer: " + shell.Explorer.StatusText);

        return (DatabaseConnectionDialog)dialog!;
    }

    private static void ChooseKind(DatabaseConnectionDialog dialog, DatabaseKind kind)
    {
        var combo = Named<ComboBox>(dialog, "DatabaseKind");
        combo.SelectedItem = combo.Items.Cast<object>().Single(item => item.ToString() == DatabaseKinds.Label(kind));
    }

    /// <summary>Opens a table or view by double-clicking it in the Explorer, and waits for its rows.</summary>
    private static async Task<TableDataView> OpenTableFromExplorerAsync(JourneyShell shell, string table) =>
        await OpenFromExplorerAsync(shell, table, inViews: false);

    private static async Task<TableDataView> OpenFromExplorerAsync(JourneyShell shell, string name, bool inViews)
    {
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        await WaitUntilAsync(shell, () => connection.IsLoaded);

        var group = Group(connection, inViews ? StringKeys.DbViewsGroup : StringKeys.DbTablesGroup, inViews ? 1 : -1);
        await ExpandAsync(shell, group);

        DoubleClick(shell, group.Children.OfType<ObjectNode>().Single(o => o.Text == name));

        return await WaitForTableAsync(shell, name);
    }

    private static async Task<TableDataView> WaitForTableAsync(JourneyShell shell, string name)
    {
        TableDataView? view = null;

        await WaitUntilAsync(shell, () =>
        {
            view = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<TableDataView>()
                .FirstOrDefault(v => v.Table.Name == name && !v.IsRemoved);

            return view is not null && !view.IsRemoved && view.RangeText.Length > 0;
        });

        var open = string.Join("; ", shell.Window.DatabaseDocumentsForTests.Select(d => d.Title + " -> " +
            (d.View as TableDataView)?.StatusText));

        return view ?? throw new InvalidOperationException($"The rows of {name} were not shown. Documents: {open}");
    }

    /// <summary>The Explorer's group under a connection, found by its title (and its count, when given).</summary>
    private static GroupNode Group(ConnectionNode connection, string titleKey, int count)
    {
        var title = L(titleKey);

        return connection.Children.OfType<GroupNode>().Single(g => count < 0
            ? g.Text.StartsWith(title + " (", StringComparison.Ordinal)
            : g.Text == $"{title} ({count})");
    }

    private static CategoryNode Category(ObjectNode table, string titleKey)
    {
        var title = L(titleKey);

        return table.Children.OfType<CategoryNode>().Single(c => c.Text.StartsWith(title + " (", StringComparison.Ordinal));
    }

    /// <summary>Expands a node in the tree the way a click on its arrow does.</summary>
    private static async Task ExpandAsync(JourneyShell shell, ExplorerNode node)
    {
        ItemFor(shell, node).IsExpanded = true;
        await shell.SettleAsync(3);
    }

    /// <summary>Double-clicks a node: its row is selected and the double tap reaches the tree.</summary>
    private static void DoubleClick(JourneyShell shell, ExplorerNode node)
    {
        var item = ItemFor(shell, node);
        item.IsSelected = true;

        // Raised on the row itself, as a real double-click's source is: the tree finds the node from it.
        item.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
    }

    private static void SelectItem(JourneyShell shell, ExplorerNode node) => ItemFor(shell, node).IsSelected = true;

    private static TreeView TreeOf(JourneyShell shell) => shell.Explorer.GetVisualDescendants().OfType<TreeView>().Single();

    private static TreeViewItem ItemFor(JourneyShell shell, ExplorerNode node) =>
        TreeItems(shell).Single(item => ReferenceEquals(item.DataContext, node));

    private static IEnumerable<TreeViewItem> TreeItems(JourneyShell shell) =>
        TreeOf(shell).GetVisualDescendants().OfType<TreeViewItem>();

    /// <summary>Edits one cell the way a user does: select it, begin the edit, type, and commit.</summary>
    private static async Task EditCellAsync(JourneyShell shell, DataGrid grid, int rowIndex, int columnIndex, string text)
    {
        grid.SelectedIndex = rowIndex;
        grid.CurrentColumn = grid.Columns[columnIndex];
        await shell.SettleAsync(2);

        Assert.True(grid.BeginEdit(), "The cell did not open for editing.");
        await shell.SettleAsync(2);

        var box = grid.GetVisualDescendants().OfType<TextBox>().Single();
        box.Text = text;

        Assert.True(grid.CommitEdit(), "The edit was not committed.");
        await shell.SettleAsync(2);
    }

    private static DataGrid GridOf(Visual scope) => scope.GetVisualDescendants().OfType<DataGrid>().Single();

    private static List<TableRow> RowsOf(TableDataView view) =>
        ((IEnumerable)GridOf(view).ItemsSource!).Cast<TableRow>().ToList();

    private static string? Cell(TableRow row, int column) => row.Cells[column].Text;

    private static Button ButtonLabelled(Visual scope, string label) =>
        scope.GetVisualDescendants().OfType<Button>().Last(b => b.Content as string == label);

    private static T Named<T>(Visual scope, string name) where T : Control =>
        scope.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static bool ShowsText(Visual scope, string text) =>
        scope.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException($"No menu entry named {header}.");

            level = hit.Children ?? [];
        }

        hit!.Action!.Invoke();
    }

    private static async Task<Window?> WaitForDialogAsync(JourneyShell shell)
    {
        Window? dialog = null;

        for (var i = 0; i < 80 && dialog is null; i++)
        {
            await shell.SettleAsync(1);
            dialog = shell.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static async Task<bool> WaitUntilAsync(JourneyShell shell, Func<bool> condition, int attempts = 120)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return true;

            await shell.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>A window with the IDE in it, and the in-memory stores the database client needs.</summary>
    private sealed class JourneyShell : IDisposable
    {
        public JourneyShell(MemoryDatabasePasswordStore passwords)
        {
            Window = new MainWindow(null, new AiAssistantServices(new MemoryKeyStore(), (_, _, _) =>
                throw new InvalidOperationException("The database journeys do not call the assistant.")));

            Window.DatabaseWorkspaceForTests.UsePasswords(passwords);
            Window.Show();
        }

        public MainWindow Window { get; }

        public DatabaseExplorerPanel Explorer => Window.DatabaseExplorerForTests;

        public DatabaseWorkspace Workspace => Window.DatabaseWorkspaceForTests;

        /// <summary>Several layout passes with short pauses, so panels built over more than one pass exist.</summary>
        public async Task SettleAsync(int passes = 5)
        {
            for (var i = 0; i < passes; i++)
            {
                Window.UpdateLayout();
                await Task.Delay(60);
            }

            Window.UpdateLayout();
        }

        public void Dispose()
        {
            Window.SkipSaveChangesPromptForTests = true;
            Window.Close();
        }
    }

    /// <summary>An in-memory key store: nothing reaches the keychain or the key file.</summary>
    private sealed class MemoryKeyStore : IApiKeyStore
    {
        public bool UsesPlatformKeychain => false;

        public string? Get(AiVendor vendor) => null;

        public bool Set(AiVendor vendor, string apiKey) => true;

        public bool Remove(AiVendor vendor) => true;

        public bool Has(AiVendor vendor) => false;
    }

    /// <summary>
    /// A session that answers a batch with a fixed answer. Used only where the engine gives no
    /// answer yet: every other member is refused, so a test cannot quietly depend on it.
    /// </summary>
    private sealed class ScriptedSession(QueryBatchResult answer) : IDatabaseSession
    {
        public DatabaseKind Kind => DatabaseKind.Sqlite;

        public string DisplayName => "scripted";

        public ISqlDialect Dialect => throw new NotSupportedException();

        public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10_000, CancellationToken ct = default) =>
            Task.FromResult(answer);

        public Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
