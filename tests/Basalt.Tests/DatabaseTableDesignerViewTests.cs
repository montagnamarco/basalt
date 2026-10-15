using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Services;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Database;
using Basalt.Workspace.Ai;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The table designer through the real window: the Explorer's menu entries open it, the grid shows the columns,
/// Save shows the script and runs it, and the tree lists a new table. Every connection is a temporary SQLite file.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseTableDesignerViewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-designer-view-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-designer-view-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public DatabaseTableDesignerViewTests() => Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
        try { File.Delete(QueryHistory.PathBeside(_settings)); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task DesignTableOpensADocumentWhoseGridListsTheColumns()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Orders"), StringKeys.DbDesignTable));

        Assert.Equal(["Id", "CustomerId", "Amount"], designer.Rows.Select(r => r.Name));
        Assert.Equal(["INTEGER", "INTEGER", "REAL"], designer.Rows.Select(r => r.DataType));
        Assert.Equal([true, false, false], designer.Rows.Select(r => r.IsPrimaryKey));
        Assert.Equal([false, false, true], designer.Rows.Select(r => r.IsNullable));
        Assert.Equal("Orders", designer.NameBox.Text);
        Assert.True(designer.NameBox.IsReadOnly);

        var document = shell.Window.DatabaseDocumentsForTests.Single(d => d.View == designer);
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerTitle, "Orders"), document.Title);

        // The cells are real controls, with the column's name and its type in them.
        await shell.SettleAsync(3);
        var boxes = designer.Grid.GetVisualDescendants().OfType<TextBox>().Select(t => t.Text).ToList();
        Assert.Contains("CustomerId", boxes);

        var types = designer.Grid.GetVisualDescendants().OfType<ComboBox>().Select(c => c.Text).ToList();
        Assert.Contains("REAL", types);
        Assert.Equal(6, designer.Grid.Columns.Count);

        // Opening the same table again shows the tab it has rather than opening a second.
        await shell.Window.OpenTableDesignerAsync(new DatabaseDesignRequest(connection.ConnectionId, new DbObjectName(null, "Orders")));
        Assert.Single(shell.Window.DatabaseDocumentsForTests, d => d.View is TableDesignerView);
    }

    [AvaloniaFact]
    public async Task NewTableOnTheTablesFolderCreatesTheTableAndTheTreeListsIt()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, tables, StringKeys.DbNewTable));

        Assert.Null(designer.Table);
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerNewTitle), shell.Window.DatabaseDocumentsForTests.Single(d => d.View == designer).Title);
        Assert.False(designer.HasUnsavedChanges);

        designer.NameBox.Text = "Invoices";
        designer.Rows[0].Name = "Number";
        designer.Rows[0].DataType = "INTEGER";
        designer.Rows[0].IsPrimaryKey = true;
        designer.Rows[0].IsIdentity = true;
        Assert.True(designer.HasUnsavedChanges);

        TableScript? shown = null;
        designer.ScriptReviewForTests = script => { shown = script; return Task.FromResult(ScriptChoice.Run); };

        Assert.True(await designer.SaveAsync());

        Assert.Contains("CREATE TABLE \"Invoices\"", shown!.Text);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        var created = await session.GetDetailsAsync(new DbObjectName(null, "Invoices"));
        Assert.Equal(["Number"], created.Columns.Select(c => c.Name));
        Assert.True(created.Columns[0].IsIdentity);

        // The document is now the designer of that table, saved, and the Explorer lists it.
        Assert.False(designer.HasUnsavedChanges);
        Assert.Equal(new DbObjectName(null, "Invoices"), designer.Table);
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerTitle, "Invoices"), shell.Window.DatabaseDocumentsForTests.Single(d => d.View == designer).Title);
        Assert.True(await WaitUntilAsync(shell, () => Group(connection, StringKeys.DbTablesGroup).Children.OfType<ObjectNode>().Any(o => o.Text == "Invoices")),
            "The new table never appeared in the tree.");
    }

    [AvaloniaFact]
    public async Task GenerateScriptOnlyOpensAQueryTabAndChangesNothing()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Products"), StringKeys.DbDesignTable));

        designer.Rows.Single(r => r.Name == "Title").Name = "Heading";
        designer.ScriptReviewForTests = _ => Task.FromResult(ScriptChoice.ScriptOnly);

        Assert.False(await designer.SaveAsync());

        var query = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Single();
        Assert.Contains("new_Products", query.Sql);
        Assert.Contains("\"Heading\"", query.Sql);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        var table = await session.GetDetailsAsync(new DbObjectName(null, "Products"));
        Assert.Equal(["Id", "Title"], table.Columns.Select(c => c.Name));
        Assert.True(designer.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task RenamingAColumnInTheGridSavesARebuildAndKeepsTheRows()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Customers"), StringKeys.DbDesignTable));

        designer.Rows.Single(r => r.Name == "City").Name = "Town";
        designer.ScriptReviewForTests = _ => Task.FromResult(ScriptChoice.Run);

        Assert.True(await designer.SaveAsync(), designer.NoticeText);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        var result = await session.ExecuteAsync("SELECT Name, Town FROM Customers ORDER BY Id");

        Assert.Equal(["Rossi", "Milano"], result.ResultSets[0].Rows[0]);
        Assert.Equal(3, result.ResultSets[0].Rows.Count);

        // The grid was read again: the renamed column is now the stored one.
        Assert.Equal(["Id", "Name", "Town"], designer.Rows.Select(r => r.Name));
        Assert.False(designer.HasUnsavedChanges);
    }

    [AvaloniaFact]
    public async Task AnInvalidDesignIsExplainedAndNoScriptIsShown()
    {
        using var shell = new Shell();
        var (_, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, tables, StringKeys.DbNewTable));

        designer.Rows[0].Name = "";
        var asked = false;
        designer.ScriptReviewForTests = _ => { asked = true; return Task.FromResult(ScriptChoice.Run); };

        Assert.False(await designer.SaveAsync());
        Assert.False(asked);
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerColumnNameRequired, 1), designer.NoticeText);
    }

    [AvaloniaFact]
    public async Task AFailedScriptShowsTheEngineMessageAndKeepsTheEdit()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        // "Orders" exists: creating it again is refused by the database, not by the designer.
        var designer = await OpenAsync(shell, () => Invoke(shell, tables, StringKeys.DbNewTable));

        designer.NameBox.Text = "Orders";
        designer.ScriptReviewForTests = _ => Task.FromResult(ScriptChoice.Run);

        Assert.False(await designer.SaveAsync());
        Assert.Contains("already exists", designer.NoticeText);
        Assert.True(designer.HasUnsavedChanges);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        Assert.Equal(3, (await session.GetDetailsAsync(new DbObjectName(null, "Orders"))).Columns.Count);
    }

    [AvaloniaFact]
    public async Task SaveRefusesToRunOverATableChangedAfterTheDesignerLoadedItAndLosesNothing()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Customers"), StringKeys.DbDesignTable));

        // Somebody else adds a column and a row after the designer read the table.
        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        await session.ExecuteAsync("ALTER TABLE Customers ADD COLUMN Note TEXT; INSERT INTO Customers (Name, Note) VALUES ('Neri', 'late');");

        designer.Rows.Single(r => r.Name == "City").Name = "Town";
        var asked = false;
        designer.ScriptReviewForTests = _ => { asked = true; return Task.FromResult(ScriptChoice.Run); };

        Assert.False(await designer.SaveAsync());

        Assert.Equal(Localizer.Get(StringKeys.TableDesignerStale, "Customers"), designer.NoticeText);

        // The table is as the other session left it: the column, the row, and no half-built table.
        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.Equal(["Id", "Name", "City", "Note"], table.Columns.Select(c => c.Name));
        Assert.Equal(4, (await session.ExecuteAsync("SELECT * FROM Customers")).ResultSets[0].Rows.Count);
        Assert.Empty((await session.ExecuteAsync("SELECT name FROM sqlite_master WHERE name LIKE 'new_%'")).ResultSets[0].Rows);

        // Reading it again, the designer sees the new column and the same edit then goes through.
        await designer.LoadAsync();
        Assert.Equal(["Id", "Name", "City", "Note"], designer.Rows.Select(r => r.Name));

        designer.Rows.Single(r => r.Name == "City").Name = "Town";
        Assert.True(await designer.SaveAsync(), designer.NoticeText);
        Assert.Equal(["Neri", "late"], (await session.ExecuteAsync("SELECT Name, Note FROM Customers WHERE Name = 'Neri'")).ResultSets[0].Rows[0]);
        _ = asked;
    }

    [AvaloniaFact]
    public async Task AChangeTheGeneratorRefusesIsExplainedWithoutShowingAScript()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        await session.ExecuteAsync("CREATE TABLE Log (Note TEXT); CREATE TRIGGER Products_Insert AFTER INSERT ON Products BEGIN INSERT INTO Log VALUES (NEW.Title); END;");

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Products"), StringKeys.DbDesignTable));

        designer.Rows.Single(r => r.Name == "Title").Name = "Heading";
        var asked = false;
        designer.ScriptReviewForTests = _ => { asked = true; return Task.FromResult(ScriptChoice.Run); };

        Assert.False(await designer.SaveAsync());
        Assert.False(asked);
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerTriggerUnsafe, "Products_Insert"), designer.NoticeText);
    }

    [AvaloniaFact]
    public async Task TheScriptDialogCancelAndRunAndScriptOnlyAnswerWithTheirChoice()
    {
        using var shell = new Shell();
        var script = SqliteRebuildScript();

        Assert.Equal(ScriptChoice.Cancel, await AnswerAsync(shell, script, "TableScriptCancel"));
        Assert.Equal(ScriptChoice.Run, await AnswerAsync(shell, script, "TableScriptRun"));
        Assert.Equal(ScriptChoice.ScriptOnly, await AnswerAsync(shell, script, "TableScriptOnly"));
    }

    [AvaloniaFact]
    public async Task CancelIsTheDefaultButtonForAScriptThatRebuildsOrDropsAndRunForOneThatOnlyAdds()
    {
        using var shell = new Shell();

        var rebuild = SqliteRebuildScript();
        Assert.True(rebuild.IsDestructive);

        var buttons = await ButtonsOfAsync(shell, rebuild);
        Assert.True(buttons["TableScriptCancel"].IsDefault);
        Assert.False(buttons["TableScriptRun"].IsDefault);

        var drop = TableScriptGenerator.For(DatabaseKind.PostgreSql).Alter(
            new TableDesign(new DbObjectName("public", "T"), [new("A", "int", true, false, null, false, "A"), new("B", "int", true, false, null, false, "B")], [], [], []),
            new TableDesign(new DbObjectName("public", "T"), [new("A", "int", true, false, null, false, "A")], [], [], []));
        Assert.True(drop.IsDestructive);

        var add = TableScriptGenerator.For(DatabaseKind.Sqlite).Alter(
            new TableDesign(new DbObjectName(null, "T"), [new("A", "TEXT", true, false, null, false, "A")], [], [], []),
            new TableDesign(new DbObjectName(null, "T"), [new("A", "TEXT", true, false, null, false, "A"), new("B", "TEXT", true, false, null, false)], [], [], []));
        Assert.False(add.IsDestructive);

        var safe = await ButtonsOfAsync(shell, add);
        Assert.True(safe["TableScriptRun"].IsDefault);
        Assert.False(safe["TableScriptCancel"].IsDefault);
    }

    [AvaloniaFact]
    public async Task SaveRefusesWhenAnIndexOfTheTableWasRedefinedAfterTheDesignerLoadedIt()
    {
        using var shell = new Shell();
        var (connection, tables) = await ConnectAsync(shell);

        var session = await shell.Workspace.OpenAsync(connection.ConnectionId);
        await session.ExecuteAsync("CREATE UNIQUE INDEX UX_Customers_Name ON Customers (Name) WHERE Id > 0");

        var designer = await OpenAsync(shell, () => Invoke(shell, Table(tables, "Customers"), StringKeys.DbDesignTable));

        await session.ExecuteAsync("DROP INDEX UX_Customers_Name; CREATE UNIQUE INDEX UX_Customers_Name ON Customers (Name) WHERE Id > 1");

        designer.Rows.Single(r => r.Name == "City").Name = "Town";
        designer.ScriptReviewForTests = _ => Task.FromResult(ScriptChoice.Run);

        Assert.False(await designer.SaveAsync());
        Assert.Equal(Localizer.Get(StringKeys.TableDesignerStale, "Customers"), designer.NoticeText);

        var sql = (await session.ExecuteAsync("SELECT sql FROM sqlite_master WHERE name = 'UX_Customers_Name'")).ResultSets[0].Rows[0][0] as string;
        Assert.Contains("Id > 1", sql);
    }

    [Theory]
    [InlineData(DatabaseKind.MySql, "int", "varchar(50)")]
    [InlineData(DatabaseKind.PostgreSql, "integer", "text")]
    [InlineData(DatabaseKind.SqlServer, "int", "nvarchar(50)")]
    [InlineData(DatabaseKind.SqlServer, "int", "int")]
    [InlineData(DatabaseKind.PostgreSql, "integer", "integer")]
    [InlineData(DatabaseKind.MySql, "int", "int")]
    public void ARetypedColumnMakesTheScriptDestructiveAndOtherEditsDoNot(DatabaseKind kind, string from, string to)
    {
        var schema = kind is DatabaseKind.SqlServer ? "dbo" : kind is DatabaseKind.PostgreSql ? "public" : null;
        var name = new DbObjectName(schema, "T");
        var before = new TableDesign(name, [new("A", from, true, false, null, false, "A")], [], [], []);
        var after = before with { Columns = [before.Columns[0] with { DataType = to, IsNullable = from != to }] };

        var script = TableScriptGenerator.For(kind).Alter(before, after);

        Assert.False(script.IsEmpty);
        Assert.Equal(from != to, script.IsDestructive);
    }

    private static TableScript SqliteRebuildScript()
    {
        var original = new TableDesign(new DbObjectName(null, "T"), [new("A", "TEXT", true, false, null, false, "A")], [], [], []);
        var edited = original with { Columns = [original.Columns[0] with { Name = "B" }] };

        return TableScriptGenerator.For(DatabaseKind.Sqlite).Alter(original, edited);
    }

    /// <summary>Shows the real dialog over the window, clicks one of its buttons, and returns what it answered.</summary>
    private static async Task<ScriptChoice> AnswerAsync(Shell shell, TableScript script, string button)
    {
        var answer = TableScriptDialog.AskAsync(shell.Window, "T", script);

        await shell.SettleAsync(3);

        var dialog = Assert.Single(shell.Window.OwnedWindows);
        var target = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.Name == button);

        target.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        return await answer;
    }

    private static async Task<Dictionary<string, Button>> ButtonsOfAsync(Shell shell, TableScript script)
    {
        var answer = TableScriptDialog.AskAsync(shell.Window, "T", script);

        await shell.SettleAsync(3);

        var dialog = Assert.Single(shell.Window.OwnedWindows);
        var buttons = dialog.GetVisualDescendants().OfType<Button>().Where(b => b.Name?.StartsWith("TableScript") == true).ToDictionary(b => b.Name!);

        dialog.Close();
        Assert.Equal(ScriptChoice.Cancel, await answer);

        return buttons;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string CreateShop()
    {
        var path = Path.Combine(_root, "shop.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers (Id), Amount REAL);
            CREATE TABLE Products (Id INTEGER PRIMARY KEY, Title TEXT);
            INSERT INTO Customers (Name, City) VALUES ('Rossi', 'Milano'), ('Bianchi', 'Roma'), ('Verdi', NULL);
            """;
        command.ExecuteNonQuery();

        return path;
    }

    /// <summary>Adds the shop as a connection, lists its tables in the tree, and returns the connection and its Tables folder.</summary>
    private async Task<(ConnectionNode Connection, GroupNode Tables)> ConnectAsync(Shell shell)
    {
        var draft = new DatabaseDraft("Shop", DatabaseKind.Sqlite, CreateShop(), "", "", "", "", "", false, false, false, "Prefer", "");
        var saved = await shell.Workspace.AddAsync(draft);

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.OfType<ConnectionNode>().Any(c => c.ConnectionId == saved.Id)));

        var connection = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.ConnectionId == saved.Id);
        var session = await shell.Workspace.OpenAsync(saved.Id);

        await connection.LoadAsync(session, CancellationToken.None);

        return (connection, Group(connection, StringKeys.DbTablesGroup));
    }

    private static GroupNode Group(ConnectionNode connection, string titleKey) =>
        connection.Children.OfType<GroupNode>().Single(g => g.Text.StartsWith(Localizer.Get(titleKey) + " (", StringComparison.Ordinal));

    private static ObjectNode Table(GroupNode group, string name) => group.Children.OfType<ObjectNode>().Single(o => o.Text == name);

    /// <summary>Chooses an Explorer context menu entry for a node, as a right-click and a click would.</summary>
    private static void Invoke(Shell shell, ExplorerNode node, string headerKey)
    {
        var action = shell.Explorer.MenuActionsForTests(node).Single(a => a.Header == Localizer.Get(headerKey));

        Assert.True(action.IsAvailable?.Invoke() ?? true, $"{action.Header} is not offered on {node.Text}.");
        action.Invoke();
    }

    private static async Task<TableDesignerView> OpenAsync(Shell shell, Action open)
    {
        open();

        Assert.True(await WaitUntilAsync(shell, () => shell.Window.DatabaseDocumentsForTests.Any(d => d.View is TableDesignerView v && v.Rows.Count > 0)),
            "The designer never opened.");

        return shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<TableDesignerView>().Single();
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

    private sealed class Shell : IDisposable
    {
        public Shell()
        {
            Window = new MainWindow(null, new AiAssistantServices(new MemoryKeyStore(), (_, _, _) =>
                throw new InvalidOperationException("The designer tests do not call the assistant.")));

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
