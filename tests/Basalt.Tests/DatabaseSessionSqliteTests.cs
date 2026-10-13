using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The SQLite session of the database engine, on real files.
/// </summary>
/// <remarks>
/// Every test opens a temporary file through SqliteProvider, as the IDE does, so
/// the catalogue, the batch and the edits are what the data view will run on.
/// </remarks>
public sealed class DatabaseSessionSqliteTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-session", Guid.NewGuid().ToString("N"));

    public DatabaseSessionSqliteTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A database file, made by running the script.</summary>
    private string Database(string script, string name = "data.sqlite")
    {
        var path = Path.Combine(_root, name);

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = script;
        command.ExecuteNonQuery();

        return path;
    }

    private static Task<IDatabaseSession> OpenAsync(string path) =>
        new SqliteProvider().OpenAsync(new SqliteConnectionStringBuilder { DataSource = path }.ToString());

    private static ColumnInfo Column(TableDetails table, string name) =>
        table.Columns.Single(c => c.Name == name);

    private static object? Cell(ResultSet set, int row, int column) => set.Rows[row][column];

    [Fact]
    public async Task ObjectsAreTablesAndViewsOrderedByName()
    {
        var path = Database("""
            CREATE TABLE Zeta (Id INTEGER PRIMARY KEY AUTOINCREMENT);
            CREATE TABLE Alpha (Id INTEGER PRIMARY KEY, Name TEXT);
            CREATE VIEW AlphaNames AS SELECT Name FROM Alpha;
            """);

        await using var session = await OpenAsync(path);

        var objects = await session.GetObjectsAsync();

        // Zeta's AUTOINCREMENT makes sqlite_sequence, which is the engine's and stays out of the list.
        Assert.Equal(new[] { "Alpha", "AlphaNames", "Zeta" }, objects.Select(o => o.Name.Name));
        Assert.Equal(DbObjectKind.View, objects.Single(o => o.Name.Name == "AlphaNames").Kind);
        Assert.Equal(DbObjectKind.Table, objects.Single(o => o.Name.Name == "Alpha").Kind);
        Assert.All(objects, o => Assert.Null(o.Name.Schema));
    }

    [Fact]
    public async Task DetailsDescribeColumnsKeysIndexesAndForeignKeys()
    {
        var path = Database("""
            CREATE TABLE Customers (
                Id INTEGER PRIMARY KEY,
                Name TEXT NOT NULL,
                Code VARCHAR(20) UNIQUE DEFAULT 'none',
                Credit NUMERIC
            );
            CREATE INDEX IX_Customers_Name ON Customers (Name);
            CREATE TABLE Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CustomerId INTEGER REFERENCES Customers (Id) ON DELETE CASCADE,
                Note TEXT
            );
            CREATE TABLE Pairs (A INTEGER, B TEXT, PRIMARY KEY (A, B));
            CREATE VIEW Totals AS SELECT Id FROM Customers;
            """);

        await using var session = await OpenAsync(path);

        var customers = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));

        Assert.Equal(DbObjectKind.Table, customers.Kind);
        Assert.True(customers.IsEditable);
        Assert.Equal(new[] { "Id" }, customers.KeyColumns.Select(c => c.Name));

        var id = Column(customers, "Id");
        Assert.Equal("INTEGER", id.DataType);
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsIdentity);
        Assert.False(id.IsNullable);
        Assert.Equal(1, id.Ordinal);

        var name = Column(customers, "Name");
        Assert.Equal("TEXT", name.DataType);
        Assert.False(name.IsNullable);
        Assert.False(name.IsPrimaryKey);
        Assert.False(name.IsIdentity);
        Assert.Null(name.DefaultValue);

        var code = Column(customers, "Code");
        Assert.Equal("VARCHAR(20)", code.DataType);
        Assert.True(code.IsNullable);
        Assert.Equal("'none'", code.DefaultValue);

        Assert.True(Column(customers, "Credit").IsNullable);

        Assert.Contains(customers.Indexes, i => i.Name == "IX_Customers_Name" && !i.IsUnique
            && i.Columns.SequenceEqual(new[] { "Name" }));
        Assert.Contains(customers.Indexes, i => i.IsUnique && !i.IsPrimaryKey
            && i.Columns.SequenceEqual(new[] { "Code" }));

        var orders = await session.GetDetailsAsync(new DbObjectName(null, "Orders"));
        Assert.True(Column(orders, "Id").IsIdentity);

        var key = Assert.Single(orders.ForeignKeys);
        Assert.Equal(new[] { "CustomerId" }, key.Columns);
        Assert.Equal(new DbObjectName(null, "Customers"), key.ReferencedTable);
        Assert.Equal(new[] { "Id" }, key.ReferencedColumns);

        // A composite key is a key of two columns, and neither is an identity.
        var pairs = await session.GetDetailsAsync(new DbObjectName(null, "Pairs"));
        Assert.Equal(new[] { "A", "B" }, pairs.KeyColumns.Select(c => c.Name));
        Assert.False(Column(pairs, "A").IsIdentity);
        Assert.Contains(pairs.Indexes, i => i.IsPrimaryKey && i.Columns.SequenceEqual(new[] { "A", "B" }));

        // A view has columns but no rows of its own, so the data view cannot edit it.
        var totals = await session.GetDetailsAsync(new DbObjectName(null, "Totals"));
        Assert.Equal(DbObjectKind.View, totals.Kind);
        Assert.False(totals.IsEditable);
        Assert.Empty(totals.KeyColumns);
        Assert.Equal(new[] { "Id" }, totals.Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task ABatchReturnsEverySetAndTheRowsItChanged()
    {
        var path = Database("CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO T (Name) VALUES ('a'), ('b');");

        await using var session = await OpenAsync(path);

        var result = await session.ExecuteAsync("SELECT Id FROM T; SELECT Name FROM T ORDER BY Id; UPDATE T SET Name = 'x';");

        Assert.Equal(2, result.ResultSets.Count);
        Assert.Equal(new[] { "Id" }, result.ResultSets[0].Columns.Select(c => c.Name));
        Assert.Equal(2, result.ResultSets[0].Rows.Count);
        Assert.Equal(2, result.ResultSets[1].Rows.Count);

        var message = Assert.Single(result.Messages);
        Assert.Equal("2 rows affected", message.Text);
        Assert.False(message.IsError);
        Assert.False(result.Failed);
        Assert.True(result.Took >= TimeSpan.Zero);
    }

    [Fact]
    public async Task AnErrorKeepsTheSetsBeforeItAndReportsItsMessage()
    {
        var path = Database("CREATE TABLE T (Id INTEGER);");

        await using var session = await OpenAsync(path);

        var result = await session.ExecuteAsync("SELECT 1 AS A; SELECT * FROM NoSuchTable; SELECT 2 AS B;");

        // The set before the error is kept; the statement after it is never run.
        var set = Assert.Single(result.ResultSets);
        Assert.Equal("A", set.Columns[0].Name);
        Assert.Equal(1L, Convert.ToInt64(Cell(set, 0, 0)));

        var error = Assert.Single(result.Messages, m => m.IsError);
        Assert.Contains("NoSuchTable", error.Text);
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task AnErrorTakesTheLineTheEngineGivesForIt()
    {
        var path = Database("CREATE TABLE T (Id INTEGER);");

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();

        // SQLite does not say which line failed, so the line comes from the
        // engine-specific hook. This session supplies one, to prove the message carries it.
        await using var session = new LineReportingSession(connection);

        var result = await session.ExecuteAsync("SELECT 1;\nSELECT * FROM NoSuchTable;");

        var error = Assert.Single(result.Messages);
        Assert.True(error.IsError);
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public async Task MaxRowsCapsASetAndMarksItTruncated()
    {
        var path = Database("CREATE TABLE N (V INTEGER); INSERT INTO N VALUES (1), (2), (3), (4), (5);");

        await using var session = await OpenAsync(path);

        var capped = await session.ExecuteAsync("SELECT V FROM N ORDER BY V", maxRows: 3);
        var set = Assert.Single(capped.ResultSets);
        Assert.Equal(3, set.Rows.Count);
        Assert.True(set.Truncated);

        // A set of exactly the cap is complete, not truncated.
        var whole = await session.ExecuteAsync("SELECT V FROM N", maxRows: 5);
        Assert.Equal(5, whole.ResultSets[0].Rows.Count);
        Assert.False(whole.ResultSets[0].Truncated);
    }

    [Fact]
    public async Task NullStaysDistinctFromTheEmptyString()
    {
        var path = Database("CREATE TABLE T (V TEXT); INSERT INTO T (rowid, V) VALUES (1, NULL), (2, ''), (3, 'x');");

        await using var session = await OpenAsync(path);

        var set = (await session.ExecuteAsync("SELECT V FROM T ORDER BY rowid")).ResultSets[0];

        Assert.Null(Cell(set, 0, 0));
        Assert.Equal("", Cell(set, 1, 0));
        Assert.Null(CellText.Of(Cell(set, 0, 0)));
        Assert.Equal("", CellText.Of(Cell(set, 1, 0)));
    }

    [Fact]
    public async Task PagesAreSortedAndFilteredOnTheServerAndCountedWithTheSameFilter()
    {
        var path = Database("""
            CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT, Amount INTEGER);
            INSERT INTO Items (Name, Amount) VALUES
                ('a', 1), ('b', 2), ('c', 3), ('d', 4), ('e', 5),
                ('f', 6), ('g', 7), ('h', 8), ('i', 9), ('j', 10);
            """);

        await using var session = await OpenAsync(path);

        // Amount above 2 leaves eight rows: 10 down to 3. Offset 2 of that, in pages of three, is 8, 7, 6.
        var page = await session.ReadPageAsync(new DataPageRequest(
            new DbObjectName(null, "Items"), 2, 3, [new SortSpec("Amount", true)], "Amount > 2"));

        Assert.Equal("Items", page.Table.Name.Name);
        Assert.Equal(8L, page.TotalRows);
        Assert.Equal(new long[] { 8, 7, 6 }, page.Rows.Select(r => Convert.ToInt64(r[2])));

        // Without a sort, the page is taken by the key, so the first page is the first two rows.
        var unsorted = await session.ReadPageAsync(new DataPageRequest(
            new DbObjectName(null, "Items"), 0, 2, [], null));

        Assert.Equal(10L, unsorted.TotalRows);
        Assert.Equal(new long[] { 1, 2 }, unsorted.Rows.Select(r => Convert.ToInt64(r[0])));
    }

    [Fact]
    public async Task EditsInsertUpdateAndDeleteByKeyInOneTransaction()
    {
        var path = Database("""
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Credit REAL);
            INSERT INTO Customers (Id, Name, Credit) VALUES (1, 'Rossi', 10.5), (2, 'Bianchi', NULL);
            """);

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Customers");

        var changed = await session.ApplyEditsAsync(table, [
            new RowInsert(new Dictionary<string, string?> { ["Name"] = "Verdi", ["Credit"] = "3.25" }),
            // The key column itself changes: the update still finds row 1 by its original key.
            new RowUpdate(
                new Dictionary<string, object?> { ["Id"] = 1L },
                new Dictionary<string, string?> { ["Id"] = "10", ["Name"] = "Rossi M" }),
            new RowDelete(new Dictionary<string, object?> { ["Id"] = 2L }),
        ]);

        Assert.Equal(3, changed);

        var rows = (await session.ExecuteAsync("SELECT Id, Name, Credit FROM Customers ORDER BY Id")).ResultSets[0];
        Assert.Equal(2, rows.Rows.Count);
        Assert.Equal(3L, Convert.ToInt64(Cell(rows, 0, 0)));
        Assert.Equal("Verdi", Cell(rows, 0, 1));
        Assert.Equal(3.25, Convert.ToDouble(Cell(rows, 0, 2)));
        Assert.Equal(10L, Convert.ToInt64(Cell(rows, 1, 0)));
        Assert.Equal("Rossi M", Cell(rows, 1, 1));
    }

    [Fact]
    public async Task TypedTextBecomesTheColumnsTypeAndTextStaysText()
    {
        var path = Database("""
            CREATE TABLE Typed (
                Id INTEGER PRIMARY KEY,
                Count INTEGER,
                Ratio REAL,
                Flag BOOLEAN,
                Day DATE,
                Data BLOB,
                Label TEXT
            );
            """);

        await using var session = await OpenAsync(path);

        await session.ApplyEditsAsync(new DbObjectName(null, "Typed"), [
            new RowInsert(new Dictionary<string, string?>
            {
                ["Count"] = "42",
                ["Ratio"] = "2.5",
                ["Flag"] = "true",
                ["Day"] = "2024-03-05",
                ["Data"] = "0xDEAD",
                ["Label"] = "0x12",
            }),
            new RowInsert(new Dictionary<string, string?> { ["Count"] = null, ["Flag"] = "0" }),
        ]);

        var set = (await session.ExecuteAsync("SELECT Count, Ratio, Flag, Day, Data, Label FROM Typed WHERE Id = 1")).ResultSets[0];

        Assert.Equal(42L, Convert.ToInt64(Cell(set, 0, 0)));
        Assert.Equal(2.5, Convert.ToDouble(Cell(set, 0, 1)));
        Assert.Equal(1L, Convert.ToInt64(Cell(set, 0, 2)));
        // SQLite has no date type, so the DATE column holds the ISO text the user typed,
        // not the "2024-03-05 00:00:00" a DateTime would be stored as.
        Assert.Equal("2024-03-05", Cell(set, 0, 3));
        Assert.Equal(new byte[] { 0xDE, 0xAD }, Assert.IsType<byte[]>(Cell(set, 0, 4)));

        // Text that happens to look like hex is still text: only a column of a binary type reads it so.
        Assert.Equal("0x12", Cell(set, 0, 5));

        var second = (await session.ExecuteAsync("SELECT Count, Flag FROM Typed WHERE Id = 2")).ResultSets[0];
        Assert.Null(Cell(second, 0, 0));
        Assert.Equal(0L, Convert.ToInt64(Cell(second, 0, 1)));
    }

    [Fact]
    public async Task AConstraintViolationRollsBackEverythingAndNamesTheEdit()
    {
        var path = Database("""
            CREATE TABLE Items (Id INTEGER PRIMARY KEY, Code TEXT UNIQUE NOT NULL, Count INTEGER);
            INSERT INTO Items (Code, Count) VALUES ('a', 1);
            """);

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Items");

        var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
            new RowInsert(new Dictionary<string, string?> { ["Code"] = "b", ["Count"] = "2" }),
            new RowInsert(new Dictionary<string, string?> { ["Code"] = "a", ["Count"] = "3" }),
        ]));

        Assert.Equal(1, error.EditIndex);
        Assert.IsType<SqliteException>(error.InnerException);

        // The first insert went in the same transaction, so it is undone with the second.
        var count = (await session.ExecuteAsync("SELECT COUNT(*) FROM Items")).ResultSets[0];
        Assert.Equal(1L, Convert.ToInt64(Cell(count, 0, 0)));
    }

    [Fact]
    public async Task AValueThatDoesNotFitItsColumnIsRefusedAsThatEdit()
    {
        var path = Database("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Count INTEGER);");

        await using var session = await OpenAsync(path);

        var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(
            new DbObjectName(null, "Items"),
            [new RowInsert(new Dictionary<string, string?> { ["Count"] = "many" })]));

        Assert.Equal(0, error.EditIndex);
        Assert.IsType<FormatException>(error.InnerException);
    }

    [Fact]
    public async Task AnUpdateOrDeleteOfAVanishedRowFails()
    {
        var path = Database("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO Items VALUES (1, 'a');");

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Items");

        // Row 99 is not there: someone else removed it after the grid read it.
        var update = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
            new RowUpdate(new Dictionary<string, object?> { ["Id"] = 99L }, new Dictionary<string, string?> { ["Name"] = "x" }),
        ]));
        Assert.Equal(0, update.EditIndex);
        Assert.IsType<InvalidOperationException>(update.InnerException);

        var delete = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
            new RowDelete(new Dictionary<string, object?> { ["Id"] = 99L }),
        ]));
        Assert.Equal(0, delete.EditIndex);
    }

    [Fact]
    public async Task CancellingALongStatementStopsItQuickly()
    {
        var path = Database("CREATE TABLE T (Id INTEGER);");

        await using var session = await OpenAsync(path);

        // Uncancelled, this recursive CTE runs for many seconds: only the interrupt stops it early.
        const string slow = "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 100000000) SELECT count(*) FROM c";

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAsync<OperationCanceledException>(() => session.ExecuteAsync(slow, ct: cancel.Token));

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Cancel took {clock.Elapsed}.");

        // The connection is free again after the stop.
        var after = await session.ExecuteAsync("SELECT 1 AS One");
        Assert.Equal(1L, Convert.ToInt64(Cell(Assert.Single(after.ResultSets), 0, 0)));
    }

    [Fact]
    public void TheDialectQuotesNamesAndSpellsPagesAndTheTopSelect()
    {
        var dialect = new SqliteDialect();

        Assert.Equal("\"we\"\"ird name\"", dialect.Quote("we\"ird name"));
        Assert.Equal("\"Order Lines\"", dialect.QualifiedName(new DbObjectName(null, "Order Lines")));
        Assert.Equal("\"main\".\"Order Lines\"", dialect.QualifiedName(new DbObjectName("main", "Order Lines")));

        var orders = new TableDetails(
            new DbObjectName(null, "Orders"),
            DbObjectKind.Table,
            [new ColumnInfo("Id", "INTEGER", false, true, true, null, 1), new ColumnInfo("Total", "REAL", true, false, false, null, 2)],
            [],
            []);

        Assert.Equal(
            "SELECT * FROM \"Orders\" WHERE (Total > 10\n) ORDER BY \"Total\" DESC LIMIT 25 OFFSET 50",
            dialect.SelectPage(orders, 50, 25, [new SortSpec("Total", true)], "Total > 10"));

        // With no sort the key orders the page; with no filter there is no WHERE.
        Assert.Equal(
            "SELECT * FROM \"Orders\" ORDER BY \"Id\" LIMIT 25 OFFSET 0",
            dialect.SelectPage(orders, 0, 25, [], null));

        Assert.Equal("SELECT * FROM \"Orders\" LIMIT 5", dialect.SelectTop(new DbObjectName(null, "Orders"), 5));
    }

    [Fact]
    public async Task NamesWithQuotesAndSpacesWorkThroughAPageAndAnEdit()
    {
        var path = Database(""""
            CREATE TABLE "Odd ""Name""" ("Col ""x""" INTEGER PRIMARY KEY, "Plain Col" TEXT);
            INSERT INTO "Odd ""Name""" VALUES (1, 'a');
            """");

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Odd \"Name\"");

        var details = await session.GetDetailsAsync(table);
        Assert.Equal(new[] { "Col \"x\"", "Plain Col" }, details.Columns.Select(c => c.Name));

        var page = await session.ReadPageAsync(new DataPageRequest(table, 0, 10, [new SortSpec("Plain Col", false)], null));
        Assert.Equal(1L, page.TotalRows);

        var changed = await session.ApplyEditsAsync(table, [
            new RowUpdate(new Dictionary<string, object?> { ["Col \"x\""] = 1L }, new Dictionary<string, string?> { ["Plain Col"] = "b" }),
        ]);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void CellTextFormatsEachKindOfValue()
    {
        Assert.Null(CellText.Of(null));
        Assert.Equal("", CellText.Of(""));
        Assert.Equal("42", CellText.Of(42L));
        Assert.Equal("2.5", CellText.Of(2.5));
        Assert.Equal("true", CellText.Of(true));
        Assert.Equal("2024-03-05", CellText.Of(new DateTime(2024, 3, 5)));
        Assert.Equal("2024-03-05 10:20:30", CellText.Of(new DateTime(2024, 3, 5, 10, 20, 30)));
        Assert.Equal("0xDEAD", CellText.Of(new byte[] { 0xDE, 0xAD }));

        // A large value shows its first 64 bytes and says there is more.
        var text = CellText.Of(new byte[70]);
        Assert.Equal(2 + 128 + 1, text!.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public async Task AMissingFileFailsAndIsNotCreated()
    {
        var path = Path.Combine(_root, "missing.sqlite");

        var error = await Assert.ThrowsAsync<FileNotFoundException>(() => OpenAsync(path));

        Assert.Equal(Localizer.Get(StringKeys.DatabaseNoFile, path), error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AFileThatIsNotADatabaseFailsToOpen()
    {
        var path = Path.Combine(_root, "notes.txt");
        File.WriteAllText(path, "This is not a database.\n");

        await Assert.ThrowsAsync<InvalidDataException>(() => OpenAsync(path));
    }

    [Fact]
    public void ProvidersAreFoundByEngine()
    {
        Assert.IsType<SqliteProvider>(DatabaseProviders.Get(DatabaseKind.Sqlite));
        Assert.IsType<SqlServerProvider>(DatabaseProviders.Get(DatabaseKind.SqlServer));
        Assert.IsType<PostgreSqlProvider>(DatabaseProviders.Get(DatabaseKind.PostgreSql));
        Assert.IsType<MySqlProvider>(DatabaseProviders.Get(DatabaseKind.MySql));

        foreach (var kind in Enum.GetValues<DatabaseKind>())
            Assert.Equal(kind, DatabaseProviders.Get(kind).Kind);
    }

    [Fact]
    public async Task AFilterEndingInACommentKeepsThePageBoundedAndSorted()
    {
        var path = Database("""
            CREATE TABLE Items (Id INTEGER PRIMARY KEY, Amount INTEGER);
            INSERT INTO Items (Amount) VALUES (1), (2), (3), (4), (5), (6), (7), (8);
            """);

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Items");

        // A line comment and a block comment at the end of the filter must not
        // swallow the page's ORDER BY and LIMIT: the page is three rows, largest first.
        foreach (var filter in new[] { "Amount > 2 --", "Amount > 2 /* the rest */" })
        {
            var page = await session.ReadPageAsync(new DataPageRequest(table, 0, 3, [new SortSpec("Amount", true)], filter));

            Assert.Equal(new long[] { 8, 7, 6 }, page.Rows.Select(r => Convert.ToInt64(r[1])));
            Assert.Equal(6L, page.TotalRows);
        }
    }

    [Fact]
    public async Task APageNeedsAPositiveLimitAndANonNegativeOffset()
    {
        var path = Database("CREATE TABLE Items (Id INTEGER PRIMARY KEY);");

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Items");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ReadPageAsync(new DataPageRequest(table, 0, 0, [], null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ReadPageAsync(new DataPageRequest(table, -1, 5, [], null)));

        var empty = new TableDetails(table, DbObjectKind.Table, [], [], []);
        Assert.Throws<ArgumentOutOfRangeException>(() => new SqliteDialect().SelectPage(empty, 0, 0, [], null));
    }

    [Fact]
    public async Task ARefusalAtCommitRollsBackAndIsReportedWithoutAnEditIndex()
    {
        var path = Database("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent (Id) DEFERRABLE INITIALLY DEFERRED);
            """);

        await using var session = await OpenAsync(path);
        await session.ExecuteAsync("PRAGMA foreign_keys = ON;");
        var child = new DbObjectName(null, "Child");

        // The insert passes its own statement. The parent is checked only at commit, and the commit refuses it.
        var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(child, [
            new RowInsert(new Dictionary<string, string?> { ["ParentId"] = "7" }),
        ]));

        Assert.Equal(-1, error.EditIndex);
        Assert.IsType<SqliteException>(error.InnerException);

        var count = (await session.ExecuteAsync("SELECT COUNT(*) FROM Child")).ResultSets[0];
        Assert.Equal(0L, Convert.ToInt64(Cell(count, 0, 0)));
    }

    [Fact]
    public async Task DisposeStopsARunningStatementInsteadOfWaitingForIt()
    {
        var path = Database("CREATE TABLE T (Id INTEGER);");

        // The test owns the connection, so it can register a function the statement calls. The first call is
        // the signal that the statement is running: the close waits for that, not for a sleep.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        connection.Open();
        connection.CreateFunction("started", () =>
        {
            started.TrySetResult();
            return 1L;
        });

        var session = new SqliteSession(connection, "dispose");

        // Uncancelled, this runs for many seconds. The close must stop it, not wait for it.
        const string slow = "WITH RECURSIVE c(x) AS (SELECT started() UNION ALL SELECT x + 1 FROM c WHERE x < 100000000) SELECT count(*) FROM c";
        var running = session.ExecuteAsync(slow);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var clock = Stopwatch.StartNew();
        await session.DisposeAsync();
        clock.Stop();

        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"Dispose took {clock.Elapsed}.");

        // The statement was stopped, so it ends with the engine's message rather than a count.
        var result = await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Failed);
    }

    [Fact]
    public async Task CancellingDuringEditsRollsBackTheEditsBeforeIt()
    {
        var path = Database("""
            CREATE TABLE Log (Id INTEGER PRIMARY KEY, Name TEXT);
            CREATE TRIGGER SlowInsert AFTER INSERT ON Log WHEN NEW.Name = 'slow' BEGIN
                SELECT count(*) FROM (WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 100000000) SELECT x FROM c);
            END;
            """);

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Log");

        // The first insert is quick and goes in; the second runs the slow trigger, which the cancel stops.
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<OperationCanceledException>(() => session.ApplyEditsAsync(table, [
            new RowInsert(new Dictionary<string, string?> { ["Name"] = "fast" }),
            new RowInsert(new Dictionary<string, string?> { ["Name"] = "slow" }),
        ], cancel.Token));

        // The transaction was rolled back, so the quick insert is gone too.
        var count = (await session.ExecuteAsync("SELECT COUNT(*) FROM Log")).ResultSets[0];
        Assert.Equal(0L, Convert.ToInt64(Cell(count, 0, 0)));
    }

    [Fact]
    public async Task ARowWithANullKeyColumnIsFoundWithIsNull()
    {
        // SQLite lets a non-integer primary key column hold NULL, so a row can have one.
        var path = Database("CREATE TABLE Pairs (A INTEGER NOT NULL, B TEXT, Note TEXT, PRIMARY KEY (A, B)); INSERT INTO Pairs (A, B, Note) VALUES (1, NULL, 'before');");

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Pairs");
        var key = new Dictionary<string, object?> { ["A"] = 1L, ["B"] = null };

        var updated = await session.ApplyEditsAsync(table, [new RowUpdate(key, new Dictionary<string, string?> { ["Note"] = "after" })]);
        Assert.Equal(1, updated);

        var note = (await session.ExecuteAsync("SELECT Note FROM Pairs")).ResultSets[0];
        Assert.Equal("after", Cell(note, 0, 0));

        var deleted = await session.ApplyEditsAsync(table, [new RowDelete(key)]);
        Assert.Equal(1, deleted);
    }

    [Fact]
    public async Task NumbersAndDatesAreReadTheSameInAnyCulture()
    {
        CultureInfo? italian = null;

        try
        {
            italian = new CultureInfo("it-IT");
        }
        catch (CultureNotFoundException)
        {
        }

        Assert.SkipWhen(italian is null, "This runtime has no it-IT culture.");

        var path = Database("CREATE TABLE Measure (Id INTEGER PRIMARY KEY, Ratio REAL, Day DATE);");

        await using var session = await OpenAsync(path);
        var table = new DbObjectName(null, "Measure");

        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = italian!;

        try
        {
            await session.ApplyEditsAsync(table, [
                new RowInsert(new Dictionary<string, string?> { ["Ratio"] = "1.5", ["Day"] = "2024-03-05" }),
            ]);

            // In it-IT the comma is the decimal mark, but numbers are read as the invariant culture reads
            // them, so "1,5" is not a number here. The refusal says so instead of storing 15.
            var refused = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
                new RowInsert(new Dictionary<string, string?> { ["Ratio"] = "1,5" }),
            ]));
            Assert.IsType<FormatException>(refused.InnerException);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        var set = (await session.ExecuteAsync("SELECT Ratio, Day FROM Measure WHERE Id = 1")).ResultSets[0];
        Assert.Equal(1.5, Convert.ToDouble(Cell(set, 0, 0), CultureInfo.InvariantCulture));
        Assert.Equal("2024-03-05", Cell(set, 0, 1));
    }

    [Fact]
    public async Task AnIntegerOutOfRangeIsRefusedAsThatEditWithAReadableMessage()
    {
        var path = Database("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Count INTEGER);");

        await using var session = await OpenAsync(path);

        var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(
            new DbObjectName(null, "Items"),
            [new RowInsert(new Dictionary<string, string?> { ["Count"] = "99999999999999999999" })]));

        Assert.Equal(0, error.EditIndex);
        Assert.IsType<FormatException>(error.InnerException);
        Assert.Contains("99999999999999999999", error.Message);
        Assert.DoesNotContain("System.", error.Message);
    }

    [Fact]
    public async Task ANotANumberIsRefusedRatherThanStoredAsNull()
    {
        var path = Database("CREATE TABLE Items (Id INTEGER PRIMARY KEY, Ratio REAL);");

        await using var session = await OpenAsync(path);

        // SQLite keeps a NaN as NULL, so storing it would change the value without a word.
        var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(
            new DbObjectName(null, "Items"),
            [new RowInsert(new Dictionary<string, string?> { ["Ratio"] = "NaN" })]));

        Assert.IsType<FormatException>(error.InnerException);

        var count = (await session.ExecuteAsync("SELECT COUNT(*) FROM Items")).ResultSets[0];
        Assert.Equal(0L, Convert.ToInt64(Cell(count, 0, 0)));
    }

    [Fact]
    public async Task ASingleChangeIsReportedInTheSingular()
    {
        var path = Database("CREATE TABLE T (Id INTEGER PRIMARY KEY, Name TEXT); INSERT INTO T VALUES (1, 'a');");

        await using var session = await OpenAsync(path);

        var result = await session.ExecuteAsync("UPDATE T SET Name = 'b' WHERE Id = 1;");

        var message = Assert.Single(result.Messages);
        Assert.Equal(Localizer.Get(StringKeys.DatabaseRowAffected), message.Text);
        Assert.NotEqual(Localizer.Get(StringKeys.DatabaseRowsAffected, 1), message.Text);
    }

    /// <summary>A session whose engine reports every error on line 2.</summary>
    private sealed class LineReportingSession(DbConnection connection) : SqliteSession(connection, "lines")
    {
        protected override int? ErrorLine(DbException exception) => 2;
    }
}
