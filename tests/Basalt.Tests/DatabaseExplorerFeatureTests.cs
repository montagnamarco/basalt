using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
/// The Database Explorer's tree through the real window: the icon on every node, the columns with their key
/// marks, the filter box, "Script as", and Properties. Every connection is a temporary SQLite file.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseExplorerFeatureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-explorer-features-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-explorer-features-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public DatabaseExplorerFeatureTests() => Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
        try { File.Delete(QueryHistory.PathBeside(_settings)); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // Icons
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EveryNodeOfTheTreeWearsItsIcon()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);

        Assert.Equal(IconKind.DatabaseSqlite, IconOf(shell, connection));
        Assert.Equal(IconKind.FolderTables, IconOf(shell, tables));
        Assert.Equal(IconKind.FolderViews, IconOf(shell, Group(connection, StringKeys.DbViewsGroup)));

        var orders = Table(tables, "Orders");
        Assert.Equal(IconKind.Table, IconOf(shell, orders));

        await ExpandAsync(shell, orders);
        await WaitUntilAsync(shell, () => orders.IsLoaded);

        var columns = Category(orders, StringKeys.DbColumnsGroup);
        Assert.Equal(IconKind.Folder, IconOf(shell, columns));

        await ExpandAsync(shell, columns);

        // A key for the column that identifies the row, a link for the one that points elsewhere.
        Assert.Equal(
            [IconKind.PrimaryKeyColumn, IconKind.ForeignKeyColumn, IconKind.Column],
            columns.Children.Select(c => IconOf(shell, c)).ToArray());

        var foreignKeys = Category(orders, StringKeys.DbForeignKeysGroup);
        await ExpandAsync(shell, foreignKeys);
        Assert.Equal(IconKind.ForeignKeyColumn, IconOf(shell, Assert.Single(foreignKeys.Children)));

        var customers = Table(tables, "Customers");
        await ExpandAsync(shell, customers);
        await WaitUntilAsync(shell, () => customers.IsLoaded);

        var indexes = Category(customers, StringKeys.DbIndexesGroup);
        await ExpandAsync(shell, indexes);
        Assert.Equal(IconKind.Index, IconOf(shell, Assert.Single(indexes.Children)));

        var views = Group(connection, StringKeys.DbViewsGroup);
        await ExpandAsync(shell, views);
        Assert.Equal(IconKind.View, IconOf(shell, Assert.Single(views.Children)));
    }

    [AvaloniaFact]
    public async Task EachEngineHasItsOwnConnectionIcon()
    {
        foreach (var (kind, expected) in new[]
        {
            (DatabaseKind.Sqlite, IconKind.DatabaseSqlite), (DatabaseKind.SqlServer, IconKind.DatabaseSqlServer),
            (DatabaseKind.PostgreSql, IconKind.DatabasePostgreSql), (DatabaseKind.MySql, IconKind.DatabaseMySql),
        })
        {
            var saved = new DatabaseConnectionSettings { Id = "x", Name = "n", Kind = kind.ToString() };

            Assert.Equal(expected, new ConnectionNode(saved).Icon);
        }

        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task MessagesWearAnIconToo_TheFailureAnErrorAndTheEmptyListAnInformationMark()
    {
        using var shell = new Shell();
        var shop = await AddShopAsync(shell);

        // A connection that cannot be opened: its tree is one error line.
        var broken = await shell.Workspace.AddAsync(Draft("Broken", Path.Combine(_root, "no-such-folder", "x.db")));
        await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count == 2);
        await shell.SettleAsync(3);

        // Adding a connection rebuilds the tree from the saved list, so the nodes are new ones.
        var connection = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.ConnectionId == shop.ConnectionId);
        var brokenNode = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.ConnectionId == broken.Id);

        await ExpandAsync(shell, brokenNode);
        Assert.True(await WaitUntilAsync(shell, () => brokenNode.Children.OfType<MessageNode>().Any(m => m.IsError)),
            "The failed connection did not show its error.");

        var error = brokenNode.Children.OfType<MessageNode>().Single();
        Assert.Equal(IconKind.Error, IconOf(shell, error));

        // A filter that matches nothing leaves one line saying so.
        await OpenTablesAsync(shell, connection);
        await TypeFilterAsync(shell, "zzz-nothing");

        var empty = Assert.IsType<MessageNode>(Assert.Single(connection.Children));
        Assert.False(empty.IsError);
        Assert.Equal(IconKind.Information, IconOf(shell, empty));
        Assert.Contains("zzz-nothing", empty.Text);
    }

    // ------------------------------------------------------------------
    // Columns
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ColumnsShowTheirTypeAndKeyMarksForTablesAndViewsAndAreReadOnlyOnExpand()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);

        var orders = Table(tables, "Orders");

        // Nothing about a table is read before it is opened.
        Assert.False(orders.IsLoaded);

        await ExpandAsync(shell, orders);
        Assert.True(await WaitUntilAsync(shell, () => orders.IsLoaded));

        Assert.Equal(
            ["Id  INTEGER  PK  NOT NULL", "CustomerId  INTEGER  FK  NOT NULL", "Amount  REAL  NULL"],
            Category(orders, StringKeys.DbColumnsGroup).Children.Select(c => c.Text).ToArray());

        var views = Group(connection, StringKeys.DbViewsGroup);
        await ExpandAsync(shell, views);

        var view = Assert.IsType<ObjectNode>(Assert.Single(views.Children));
        await ExpandAsync(shell, view);
        Assert.True(await WaitUntilAsync(shell, () => view.IsLoaded));

        Assert.Equal(
            ["Id  INTEGER  NULL", "Name  TEXT  NULL"],
            Category(view, StringKeys.DbColumnsGroup).Children.Select(c => c.Text).ToArray());
    }

    [AvaloniaFact]
    public async Task TheInterfaceStaysResponsiveWhileASlowQueryRunsAndStopsWhenThePanelCancelsIt()
    {
        // The real SQLite session, and a view whose count is a recursive query that would run for minutes.
        using var shell = new Shell();
        var connection = await AddShopAsync(shell, slowView: true);
        var tables = await OpenTablesAsync(shell, connection);

        var views = Group(connection, StringKeys.DbViewsGroup);
        await ExpandAsync(shell, views);
        var slow = views.Children.OfType<ObjectNode>().Single(v => v.Text == "Slow");

        var counting = shell.Explorer.DescribeAsync(slow);

        // A job posted to the interface thread runs while the count is still going: the count is not on it.
        var ran = false;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => ran = true);
        await shell.SettleAsync(3);

        Assert.True(ran);
        Assert.False(counting.IsCompleted, "The slow count finished at once, so it proves nothing.");

        var started = System.Diagnostics.Stopwatch.StartNew();
        shell.Explorer.CancelOperations();

        await counting.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(30));

        // Stopped, so the count is not reported.
        Assert.DoesNotContain("rows", shell.Explorer.StatusText);
    }

    [AvaloniaFact]
    public async Task ExpandingANodeTwiceWhileItIsBeingReadReadsItOnce()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);

        await using var inner = await new SqliteProvider().OpenAsync($"Data Source={Path.Combine(_root, "shop.db")}");
        var counting = new CountingSession(inner);

        var node = new ConnectionNode(connection.Saved);
        await node.LoadAsync(counting, CancellationToken.None);

        var table = node.Children.OfType<GroupNode>().First().Children.OfType<ObjectNode>().First(t => t.Text == "Orders");

        // Listing the connection read no details: they wait for the expand.
        Assert.Equal(0, counting.DetailCalls);

        // Folded and opened again before the first read returned.
        var first = table.LoadDetailsAsync(CancellationToken.None);
        var second = table.LoadDetailsAsync(CancellationToken.None);

        await Task.WhenAll(first, second);

        Assert.True(table.IsLoaded);
        Assert.Equal(1, counting.DetailCalls);

        // Read once: opening it again asks nothing.
        await table.LoadDetailsAsync(CancellationToken.None);
        Assert.Equal(1, counting.DetailCalls);
    }

    // ------------------------------------------------------------------
    // Filter
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheFilterNarrowsObjectsByNameIgnoringCaseAndKeepsTheParentsOfMatches()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);

        Assert.Equal(["Customers", "OrderLines", "Orders", "Products"], tables.Children.Select(c => c.Text).Order());

        await TypeFilterAsync(shell, "ORD");

        // The Tables group stays, as the parent of two matches; Views holds none and goes.
        var shown = Assert.Single(connection.Children.OfType<GroupNode>());
        Assert.Equal($"{L(StringKeys.DbTablesGroup)} (2 of 4)", shown.Text);
        Assert.Equal(["OrderLines", "Orders"], shown.Children.Select(c => c.Text).Order());

        // And the tree shows exactly that: the group open, two rows under it, no row for Customers.
        var rows = TreeItems(shell).Select(i => i.DataContext).OfType<ObjectNode>().Select(o => o.Text).Order().ToArray();
        Assert.Equal(["OrderLines", "Orders"], rows);

        await TypeFilterAsync(shell, "");

        Assert.Equal(2, connection.Children.OfType<GroupNode>().Count());
        Assert.Equal($"{L(StringKeys.DbTablesGroup)} (4)", Group(connection, StringKeys.DbTablesGroup).Text);
        Assert.Equal(4, Group(connection, StringKeys.DbTablesGroup).Children.Count);
    }

    [AvaloniaFact]
    public async Task TheFilterWaitsForAPauseInTheTypingBeforeItNarrowsTheTree()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        await OpenTablesAsync(shell, connection);

        // Three keys in quick succession: the tree is not rebuilt for each of them.
        foreach (var typed in new[] { "o", "or", "ord" }) shell.Explorer.FilterBox.Text = typed;

        Assert.Equal("", shell.Explorer.AppliedFilter);
        Assert.Equal(2, connection.Children.OfType<GroupNode>().Count());

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.AppliedFilter == "ord"));
        await shell.SettleAsync(5);

        Assert.Single(connection.Children.OfType<GroupNode>());

        // One rebuild for the three keys, not one for each.
        Assert.Equal(1, shell.Explorer.FilterRuns);
    }

    [AvaloniaFact]
    public async Task TheFilterAppliesToAConnectionListedAfterItWasTyped()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);

        await TypeFilterAsync(shell, "cust");

        await ExpandAsync(shell, connection);
        Assert.True(await WaitUntilAsync(shell, () => connection.IsLoaded));

        // Customers, and the view ActiveCustomers: "cust" is in both names.
        Assert.Equal(["Customers"], Group(connection, StringKeys.DbTablesGroup).Children.Select(c => c.Text));
        Assert.Equal(["ActiveCustomers"], Group(connection, StringKeys.DbViewsGroup).Children.Select(c => c.Text));
    }

    // ------------------------------------------------------------------
    // Script as
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ScriptAsOpensANewQueryTabWithTheTextQuotedAsTheDialectQuotes()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);
        var orders = Table(tables, "Orders");

        var select = await ScriptAsync(shell, orders, StringKeys.DbScriptSelect);
        Assert.Equal("SELECT\n    \"Id\",\n    \"CustomerId\",\n    \"Amount\"\nFROM \"Orders\";", select.Sql);
        Assert.Equal(connection.ConnectionId, select.ConnectionId);

        Assert.Equal(
            "INSERT INTO \"Orders\" (\"CustomerId\", \"Amount\")\nVALUES (<CustomerId>, <Amount>);",
            (await ScriptAsync(shell, orders, StringKeys.DbScriptInsert)).Sql);

        Assert.Equal(
            "UPDATE \"Orders\"\nSET\n    \"CustomerId\" = <CustomerId>,\n    \"Amount\" = <Amount>\nWHERE \"Id\" = <Id>;",
            (await ScriptAsync(shell, orders, StringKeys.DbScriptUpdate)).Sql);

        Assert.Equal("DELETE FROM \"Orders\"\nWHERE \"Id\" = <Id>;", (await ScriptAsync(shell, orders, StringKeys.DbScriptDelete)).Sql);

        // SQLite keeps the statement the table was made with.
        Assert.StartsWith("CREATE TABLE Orders (", (await ScriptAsync(shell, orders, StringKeys.DbScriptCreate)).Sql);

        // Each went to a tab of its own.
        Assert.Equal(5, shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Count());
    }

    [AvaloniaFact]
    public async Task AViewCanBeScriptedAsSelectAndCreateButNotAsInsertUpdateOrDelete()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        await OpenTablesAsync(shell, connection);

        var views = Group(connection, StringKeys.DbViewsGroup);
        await ExpandAsync(shell, views);
        var view = Assert.IsType<ObjectNode>(Assert.Single(views.Children));

        var actions = shell.Explorer.ScriptActionsForTests(view);

        Assert.True(actions.Single(a => a.Header == L(StringKeys.DbScriptSelect)).IsAvailable!());
        Assert.True(actions.Single(a => a.Header == L(StringKeys.DbScriptCreate)).IsAvailable!());

        foreach (var key in new[] { StringKeys.DbScriptInsert, StringKeys.DbScriptUpdate, StringKeys.DbScriptDelete })
            Assert.False(actions.Single(a => a.Header == L(key)).IsAvailable!(), key);

        Assert.Equal(
            "CREATE VIEW ActiveCustomers AS SELECT Id, Name FROM Customers;",
            (await ScriptAsync(shell, view, StringKeys.DbScriptCreate)).Sql);
    }

    [AvaloniaFact]
    public async Task ScriptAsIsASubmenuOfTheContextMenuWithFiveEntriesAndTheyWork()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);
        var customers = Table(tables, "Customers");

        var menu = shell.Explorer.TreeMenu!;
        var scriptAs = ((IEnumerable<object>)menu.ItemsSource!).OfType<MenuItem>().Single(i => i.Header as string == L(StringKeys.DbScriptAs));

        Assert.Equal(
            [L(StringKeys.DbScriptSelect), L(StringKeys.DbScriptInsert), L(StringKeys.DbScriptUpdate), L(StringKeys.DbScriptDelete), L(StringKeys.DbScriptCreate)],
            scriptAs.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray());

        // Chosen as a click on the menu item does, on the row the menu was opened on.
        shell.Explorer.MenuActionsForTests(customers);
        scriptAs.Items.OfType<MenuItem>().First().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        var query = await WaitForQueryAsync(shell, q => q.Sql.StartsWith("SELECT", StringComparison.Ordinal));
        Assert.Contains("\"Name\"", query.Sql);
    }

    // ------------------------------------------------------------------
    // Properties
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task PropertiesReportTheRowAndColumnCountsInTheOutputAndTheStatusLine()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);
        var tables = await OpenTablesAsync(shell, connection);

        shell.Explorer.MenuActionsForTests(Table(tables, "Customers"))
            .Single(a => a.Header == L(StringKeys.DbObjectProperties)).Invoke();

        var expected = Localizer.Get(StringKeys.DbObjectPropertiesText, "Customers", 3, 3, 1, 0);

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.StatusText == expected), shell.Explorer.StatusText);

        var output = ((MainWindowViewModel)shell.Window.DataContext!).BuildOutput;
        Assert.Contains(expected, output);
    }

    [AvaloniaFact]
    public async Task PropertiesAreOfferedOnlyForTablesAndViews()
    {
        using var shell = new Shell();
        var connection = await AddShopAsync(shell);

        var properties = shell.Explorer.MenuActionsForTests(connection).Single(a => a.Header == L(StringKeys.DbObjectProperties));

        Assert.False(properties.IsAvailable!());

        var tables = await OpenTablesAsync(shell, connection);
        properties = shell.Explorer.MenuActionsForTests(Table(tables, "Orders")).Single(a => a.Header == L(StringKeys.DbObjectProperties));

        Assert.True(properties.IsAvailable!());
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Types into the filter box and waits out the pause after which the tree is narrowed.</summary>
    private static async Task TypeFilterAsync(Shell shell, string text)
    {
        shell.Explorer.FilterBox.Text = text;

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.AppliedFilter == text.Trim()), "The filter was never applied.");
        await shell.SettleAsync(2);
    }

    private static string L(string key) => Localizer.Get(key);

    private static DatabaseDraft Draft(string name, string path) =>
        new(name, DatabaseKind.Sqlite, path, "", "", "", "", "", false, false, false, "Prefer", "");

    private string CreateShop(bool slowView = false)
    {
        var path = Path.Combine(_root, "shop.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);
            CREATE INDEX IX_Customers_City ON Customers (City);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers (Id), Amount REAL);
            CREATE TABLE OrderLines (Id INTEGER PRIMARY KEY, OrderId INTEGER);
            CREATE TABLE Products (Id INTEGER PRIMARY KEY, Title TEXT);
            CREATE VIEW ActiveCustomers AS SELECT Id, Name FROM Customers;
            INSERT INTO Customers (Name, City) VALUES ('Rossi', 'Milano'), ('Bianchi', 'Roma'), ('Verdi', NULL);
            """;
        command.ExecuteNonQuery();

        if (slowView)
        {
            // A count over this runs for minutes: the stand-in for a large table on a slow server.
            command.CommandText =
                "CREATE VIEW Slow AS WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 1000000000) SELECT x FROM c;";
            command.ExecuteNonQuery();
        }

        return path;
    }

    private async Task<ConnectionNode> AddShopAsync(Shell shell, bool slowView = false)
    {
        // The tool is in its slot only once the View menu has shown it and the dock has laid it out.
        InvokeMenu(shell.Window, L(StringKeys.MenuView), L(StringKeys.DbExplorerTitle));
        await shell.SettleAsync(5);

        var saved = await shell.Workspace.AddAsync(Draft("Shop", CreateShop(slowView)));

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.OfType<ConnectionNode>().Any(c => c.ConnectionId == saved.Id)));

        // The row exists once the tree has been laid out with the new root in it.
        await shell.SettleAsync(3);

        return shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.ConnectionId == saved.Id);
    }

    private static async Task<GroupNode> OpenTablesAsync(Shell shell, ConnectionNode connection)
    {
        await ExpandAsync(shell, connection);
        Assert.True(await WaitUntilAsync(shell, () => connection.IsLoaded), "The connection's tables were not listed.");

        var tables = Group(connection, StringKeys.DbTablesGroup);
        await ExpandAsync(shell, tables);

        return tables;
    }

    private static GroupNode Group(ConnectionNode connection, string titleKey) =>
        connection.Children.OfType<GroupNode>().Single(g => g.Text.StartsWith(L(titleKey) + " (", StringComparison.Ordinal));

    private static ObjectNode Table(GroupNode group, string name) => group.Children.OfType<ObjectNode>().Single(o => o.Text == name);

    private static CategoryNode Category(ObjectNode table, string titleKey) =>
        table.Children.OfType<CategoryNode>().Single(c => c.Text.StartsWith(L(titleKey) + " (", StringComparison.Ordinal)
            || c.Text == L(titleKey));

    private static async Task ExpandAsync(Shell shell, ExplorerNode node)
    {
        ItemFor(shell, node).IsExpanded = true;
        await shell.SettleAsync(3);
    }

    private static TreeViewItem ItemFor(Shell shell, ExplorerNode node) =>
        TreeItems(shell).SingleOrDefault(item => ReferenceEquals(item.DataContext, node))
        ?? throw new InvalidOperationException($"No row for {node.Text}. Tree visible {shell.Explorer.Tree.IsEffectivelyVisible} bounds {shell.Explorer.Tree.Bounds} roots {shell.Explorer.Roots.Count} explorerVisible {shell.Explorer.IsEffectivelyVisible} items {shell.Explorer.Tree.ItemCount}. Rows: " + string.Join(" | ", TreeItems(shell).Select(i => (i.DataContext as ExplorerNode)?.Text + (i.IsExpanded ? "+" : ""))));

    private static IEnumerable<TreeViewItem> TreeItems(Shell shell) =>
        shell.Explorer.Tree.GetVisualDescendants().OfType<TreeViewItem>();

    /// <summary>The icon the tree drew on a node's own row: the first in the item, before its children's.</summary>
    private static IconKind IconOf(Shell shell, ExplorerNode node) =>
        ItemFor(shell, node).GetVisualDescendants().OfType<IconView>().First().Kind;

    /// <summary>Chooses a "Script as" entry for a node, and returns the query tab it opened.</summary>
    private static async Task<(string Sql, string ConnectionId)> ScriptAsync(Shell shell, ObjectNode node, string key)
    {
        var before = shell.Window.DatabaseDocumentsForTests.Count;

        shell.Explorer.ScriptActionsForTests(node).Single(a => a.Header == L(key)).Invoke();

        Assert.True(
            await WaitUntilAsync(shell, () => shell.Window.DatabaseDocumentsForTests.Count > before),
            "No query tab opened. Explorer: " + shell.Explorer.StatusText);

        var query = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Last();

        return (query.Sql, query.SelectedConnectionId ?? "");
    }

    private static async Task<QueryView> WaitForQueryAsync(Shell shell, Func<QueryView, bool> matches)
    {
        QueryView? found = null;

        await WaitUntilAsync(shell, () =>
        {
            found = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().FirstOrDefault(matches);

            return found is not null;
        });

        return found ?? throw new InvalidOperationException("No query tab matched.");
    }

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header) ?? throw new InvalidOperationException($"No menu entry named {header}.");
            level = hit.Children ?? [];
        }

        hit!.Action!.Invoke();
    }

    private static async Task<bool> WaitUntilAsync(Shell shell, Func<bool> condition, int attempts = 120)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return true;

            await shell.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>A real session that counts how many times a table's details were read.</summary>
    private sealed class CountingSession(IDatabaseSession inner) : IDatabaseSession
    {
        private int _detailCalls;

        public int DetailCalls => _detailCalls;

        public DatabaseKind Kind => inner.Kind;
        public string DisplayName => inner.DisplayName;
        public ISqlDialect Dialect => inner.Dialect;

        public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default) => inner.GetObjectsAsync(ct);

        public Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _detailCalls);

            return inner.GetDetailsAsync(name, ct);
        }

        public Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10000, CancellationToken ct = default) => inner.ExecuteAsync(sql, maxRows, ct);
        public Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default) => inner.ReadPageAsync(request, ct);
        public Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default) => inner.ApplyEditsAsync(table, edits, ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Shell : IDisposable
    {
        public Shell()
        {
            Window = new MainWindow(null, new AiAssistantServices(new MemoryKeyStore(), (_, _, _) =>
                throw new InvalidOperationException("The explorer tests do not call the assistant.")));

            Window.DatabaseWorkspaceForTests.UsePasswords(new MemoryDatabasePasswordStore());
            Window.Show();
        }

        public MainWindow Window { get; }

        public DatabaseExplorerPanel Explorer => Window.DatabaseExplorerForTests;

        public DatabaseWorkspace Workspace => Window.DatabaseWorkspaceForTests;

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

    private sealed class MemoryKeyStore : IApiKeyStore
    {
        public bool UsesPlatformKeychain => false;
        public string? Get(AiVendor vendor) => null;
        public bool Set(AiVendor vendor, string apiKey) => true;
        public bool Remove(AiVendor vendor) => true;
        public bool Has(AiVendor vendor) => false;
    }
}
