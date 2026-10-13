using System.Diagnostics;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;

namespace Basalt.Tests;

/// <summary>
/// The scenarios of <see cref="DatabaseSessionSqliteTests"/>, run against a real SQL Server,
/// PostgreSQL or MySQL/MariaDB.
/// </summary>
/// <remarks>
/// Each engine runs only when its environment variable holds a connection string:
/// BASALT_TEST_SQLSERVER, BASALT_TEST_POSTGRES or BASALT_TEST_MYSQL. Without it the
/// test is skipped, so the suite stays green on a machine with no server. Every test
/// creates tables and views with a unique name and drops them afterwards.
/// </remarks>
public sealed class DatabaseServerSessionTests
{
    public static TheoryData<DatabaseKind> Engines => new()
    {
        DatabaseKind.SqlServer,
        DatabaseKind.PostgreSql,
        DatabaseKind.MySql,
    };

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheCatalogueListsTablesAndViewsAndDescribesKeysIndexesAndForeignKeys(DatabaseKind kind)
    {
        var target = new Target(kind);
        var connection = Connection(kind);
        var items = Unique("cat");
        var child = items + "_c";
        var view = items + "_v";

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(connection);

        try
        {
            await Script(session,
                CreateItems(session, target, items),
                ChildTable(session, target, child, items),
                $"CREATE VIEW {Q(session, target, view)} AS SELECT code FROM {Q(session, target, items)}");

            var objects = await session.GetObjectsAsync();

            Assert.Contains(objects, o => o.Name.Name == items && o.Kind == DbObjectKind.Table);
            Assert.Contains(objects, o => o.Name.Name == view && o.Kind == DbObjectKind.View);

            // The engine's own schemas are left out.
            Assert.DoesNotContain(objects, o => o.Name.Schema is "sys" or "INFORMATION_SCHEMA" or "pg_catalog" or "information_schema");

            var details = await session.GetDetailsAsync(new DbObjectName(target.Schema, items));

            Assert.Equal(DbObjectKind.Table, details.Kind);
            Assert.Equal(new[] { "id" }, details.KeyColumns.Select(c => c.Name));

            var id = Column(details, "id");
            Assert.True(id.IsIdentity);
            Assert.False(id.IsNullable);
            Assert.Equal(1, id.Ordinal);

            Assert.False(Column(details, "code").IsNullable);
            Assert.True(Column(details, "name").IsNullable);
            Assert.Contains(details.Indexes, i => i.IsUnique && !i.IsPrimaryKey && i.Columns.SequenceEqual(new[] { "code" }));

            var viewDetails = await session.GetDetailsAsync(new DbObjectName(target.Schema, view));
            Assert.Equal(DbObjectKind.View, viewDetails.Kind);
            Assert.False(viewDetails.IsEditable);

            var fk = Assert.Single((await session.GetDetailsAsync(new DbObjectName(target.Schema, child))).ForeignKeys);
            Assert.Equal(new[] { "item_id" }, fk.Columns);
            Assert.Equal(new DbObjectName(target.Schema, items), fk.ReferencedTable);
            Assert.Equal(new[] { "id" }, fk.ReferencedColumns);
        }
        finally
        {
            await Cleanup(session, target, (view, true), (child, false), (items, false));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task ABatchReturnsEverySetAndTheRowsItChanged(DatabaseKind kind)
    {
        var target = new Target(kind);
        var items = Unique("bat");

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        try
        {
            await Script(session,
                CreateItems(session, target, items),
                $"INSERT INTO {Q(session, target, items)} (code, name, amount) VALUES ('a', 'x', 1), ('b', 'y', 2)");

            var result = await session.ExecuteAsync(
                $"SELECT id FROM {Q(session, target, items)}; SELECT name FROM {Q(session, target, items)} ORDER BY id; UPDATE {Q(session, target, items)} SET amount = 5 WHERE code = 'a';");

            Assert.False(result.Failed);
            Assert.Equal(2, result.ResultSets.Count);
            Assert.Equal(new[] { "id" }, result.ResultSets[0].Columns.Select(c => c.Name));
            Assert.Equal(2, result.ResultSets[0].Rows.Count);
            Assert.Equal(2, result.ResultSets[1].Rows.Count);

            // The UPDATE changed one row, so the batch says so, on every engine (SQL Server through StatementCompleted).
            Assert.Contains(result.Messages, m => !m.IsError && m.Text == Localizer.Get(StringKeys.DatabaseRowAffected));
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task TheEnginesNoticesComeThroughAsMessages(DatabaseKind kind)
    {
        var target = new Target(kind);

        // MySQL sends no notices of its own: its messages are counts and errors.
        Assert.SkipWhen(target.Notice is null, "MySQL sends no notices of its own.");

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        var result = await session.ExecuteAsync(target.Notice!);

        Assert.False(result.Failed);
        Assert.Contains(result.Messages, m => !m.IsError && m.Text.Contains("hello from the batch", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnErrorKeepsTheSetsBeforeItAndReportsItsMessage(DatabaseKind kind)
    {
        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        var result = await session.ExecuteAsync("SELECT 1 AS a; SELECT * FROM basalt_no_such_table; SELECT 2 AS b;");

        var set = Assert.Single(result.ResultSets);
        Assert.Equal("a", set.Columns[0].Name);
        Assert.Equal(1L, Convert.ToInt64(set.Rows[0][0]));

        var error = Assert.Single(result.Messages, m => m.IsError);
        Assert.Contains("basalt_no_such_table", error.Text);
        Assert.True(result.Failed);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AnErrorTakesTheLineTheEngineGivesForIt(DatabaseKind kind)
    {
        var target = new Target(kind);

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        // The failing statement is the third, on line three. PostgreSQL's position counts from that
        // statement's start, so the line is only right when the session starts there.
        var result = await session.ExecuteAsync("SELECT 1;\nSELECT 2;\nSELECT * FROM basalt_no_such_table;");

        var error = Assert.Single(result.Messages, m => m.IsError);

        // SQL Server gives the line, and PostgreSQL a position that the session turns into one.
        // MySqlException carries no line, so MySQL reports none.
        if (target.ReportsErrorLine)
            Assert.Equal(3, error.Line);
        else
            Assert.Null(error.Line);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task PagesAreSortedFilteredAndCountedOnTheServer(DatabaseKind kind)
    {
        var target = new Target(kind);
        var items = Unique("page");

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));
        var table = new DbObjectName(target.Schema, items);

        try
        {
            var rows = string.Join(", ", Enumerable.Range(1, 10).Select(i => $"('c{i:D2}', 'n{i}', {i})"));

            await Script(session,
                CreateItems(session, target, items),
                $"INSERT INTO {Q(session, target, items)} (code, name, amount) VALUES {rows}");

            // Amount above 2 leaves eight rows, 3 to 10. Largest first, offset 2, pages of three: 8, 7, 6.
            var page = await session.ReadPageAsync(new DataPageRequest(table, 2, 3, [new SortSpec("amount", true)], "amount > 2"));

            Assert.Equal(8L, page.TotalRows);
            Assert.Equal(new long[] { 8, 7, 6 }, page.Rows.Select(r => Convert.ToInt64(r[3])));

            // A filter ending in a comment must not unbound the page, on any engine.
            foreach (var filter in new[] { "amount > 2 --", "amount > 2 /* the rest */" })
            {
                var commented = await session.ReadPageAsync(new DataPageRequest(table, 2, 3, [new SortSpec("amount", true)], filter));

                Assert.Equal(8L, commented.TotalRows);
                Assert.Equal(new long[] { 8, 7, 6 }, commented.Rows.Select(r => Convert.ToInt64(r[3])));
            }

            // Without a sort, the page is taken by the key, so the first page is the first two rows.
            var unsorted = await session.ReadPageAsync(new DataPageRequest(table, 0, 2, [], null));

            Assert.Equal(10L, unsorted.TotalRows);
            Assert.Equal(2, unsorted.Rows.Count);
            Assert.True(Convert.ToInt64(unsorted.Rows[0][0]) < Convert.ToInt64(unsorted.Rows[1][0]));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => session.ReadPageAsync(new DataPageRequest(table, 0, 0, [], null)));
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task EditsInsertUpdateAndDeleteByKeyAndAVanishedRowFails(DatabaseKind kind)
    {
        var target = new Target(kind);
        var items = Unique("edit");
        var table = new DbObjectName(target.Schema, items);

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        try
        {
            await Script(session, CreateItems(session, target, items));

            var inserted = await session.ApplyEditsAsync(table, [
                new RowInsert(new Dictionary<string, string?> { ["code"] = "e1", ["name"] = "First", ["amount"] = "7" }),
            ]);
            Assert.Equal(1, inserted);

            // The key is read back in the engine's own type, as the data view would hold it.
            var key = (await session.ExecuteAsync($"SELECT id FROM {Q(session, target, items)} WHERE code = 'e1'")).ResultSets[0].Rows[0][0];

            var changed = await session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = key }, new Dictionary<string, string?> { ["name"] = "Renamed", ["amount"] = "9" }),
            ]);
            Assert.Equal(1, changed);

            var rows = (await session.ExecuteAsync($"SELECT name, amount FROM {Q(session, target, items)}")).ResultSets[0];
            Assert.Equal("Renamed", rows.Rows[0][0]);
            Assert.Equal(9L, Convert.ToInt64(rows.Rows[0][1]));

            var deleted = await session.ApplyEditsAsync(table, [new RowDelete(new Dictionary<string, object?> { ["id"] = key })]);
            Assert.Equal(1, deleted);

            // A row that is no longer there is refused as that edit, so the user's grid is not silently wrong.
            var vanished = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = 999_999 }, new Dictionary<string, string?> { ["name"] = "x" }),
            ]));
            Assert.Equal(0, vanished.EditIndex);
            Assert.IsType<InvalidOperationException>(vanished.InnerException);
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task AConstraintViolationRollsBackEverythingAndNamesTheEdit(DatabaseKind kind)
    {
        var target = new Target(kind);
        var items = Unique("roll");
        var table = new DbObjectName(target.Schema, items);

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        try
        {
            await Script(session, CreateItems(session, target, items));

            // The second insert repeats the first one's Code, which is UNIQUE. The first insert must go too.
            var error = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
                new RowInsert(new Dictionary<string, string?> { ["code"] = "r1", ["amount"] = "1" }),
                new RowInsert(new Dictionary<string, string?> { ["code"] = "r1", ["amount"] = "2" }),
            ]));

            Assert.Equal(1, error.EditIndex);

            var count = (await session.ExecuteAsync($"SELECT COUNT(*) FROM {Q(session, target, items)}")).ResultSets[0];
            Assert.Equal(0L, Convert.ToInt64(count.Rows[0][0]));
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task CancellingALongStatementStopsItQuickly(DatabaseKind kind)
    {
        var target = new Target(kind);

        await using var session = await DatabaseProviders.Get(kind).OpenAsync(Connection(kind));

        // Uncancelled, the statement runs for thirty seconds: only the engine's own cancel stops it early.
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var clock = Stopwatch.StartNew();

        await Assert.ThrowsAsync<OperationCanceledException>(() => session.ExecuteAsync(target.Slow, ct: cancel.Token));

        clock.Stop();
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"Cancel took {clock.Elapsed}.");

        // The connection is free again after the stop.
        var after = await session.ExecuteAsync("SELECT 1 AS One");
        Assert.False(after.Failed);
    }

    [Fact]
    public async Task APostgreSqlTimestampWithAZoneIsWrittenAtItsUtcInstant()
    {
        // Npgsql takes a zoned timestamp only at offset zero, so the session converts it; text with no zone is local time.
        var target = new Target(DatabaseKind.PostgreSql);
        var items = Unique("tstz");
        var table = new DbObjectName(target.Schema, items);

        await using var session = await DatabaseProviders.Get(DatabaseKind.PostgreSql).OpenAsync(Connection(DatabaseKind.PostgreSql));

        try
        {
            await Script(session, $"CREATE TABLE {Q(session, target, items)} (id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY, at timestamp with time zone NULL)");

            var changed = await session.ApplyEditsAsync(table, [
                new RowInsert(new Dictionary<string, string?> { ["at"] = "2024-03-05 10:20:30+02:00" }),
            ]);
            Assert.Equal(1, changed);

            var stored = (await session.ExecuteAsync($"SELECT at FROM {Q(session, target, items)}")).ResultSets[0];

            // 10:20:30 at +02:00 is 08:20:30 UTC, and it reads back as that instant.
            var read = Assert.IsType<DateTime>(Cell(stored, 0, 0));
            Assert.Equal(new DateTime(2024, 3, 5, 8, 20, 30), read.ToUniversalTime());
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Fact]
    public async Task ASessionAfterOneThatLeftNoCountOnStillCountsItsRows()
    {
        // A session that set NOCOUNT ON and closed returns its connection to the pool. The next session, on the
        // same pool, must count its edits: a count of -1 there would be read as unknown, and a zero as a vanished row.
        var target = new Target(DatabaseKind.SqlServer);
        var items = Unique("pool");
        var table = new DbObjectName(target.Schema, items);
        var connection = Connection(DatabaseKind.SqlServer);
        var provider = DatabaseProviders.Get(DatabaseKind.SqlServer);

        await using (var first = await provider.OpenAsync(connection))
        {
            await first.ExecuteAsync("SET NOCOUNT ON");
        }

        await using var session = await provider.OpenAsync(connection);

        try
        {
            await Script(session,
                CreateItems(session, target, items),
                $"INSERT INTO {Q(session, target, items)} (code, name, amount) VALUES ('p1', 'before', 1)");

            var key = (await session.ExecuteAsync($"SELECT id FROM {Q(session, target, items)}")).ResultSets[0].Rows[0][0];

            var changed = await session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = key }, new Dictionary<string, string?> { ["name"] = "after" }),
            ]);

            Assert.Equal(1, changed);
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Fact]
    public async Task AnEditUnderSqlServerNoCountIsNotTakenForAVanishedRow()
    {
        // SET NOCOUNT ON makes SqlClient report -1 for every statement; the session reads @@ROWCOUNT instead,
        // so the edit is counted, and a row that vanished is still refused (see below). Reintroduced once
        // (ExecuteNonQuery's count): the vanished row was not refused and its edit silently did nothing.
        var target = new Target(DatabaseKind.SqlServer);
        var items = Unique("nocount");
        var table = new DbObjectName(target.Schema, items);

        await using var session = await DatabaseProviders.Get(DatabaseKind.SqlServer).OpenAsync(Connection(DatabaseKind.SqlServer));

        try
        {
            await Script(session,
                CreateItems(session, target, items),
                $"INSERT INTO {Q(session, target, items)} (code, name, amount) VALUES ('n1', 'before', 1)",
                "SET NOCOUNT ON");

            var key = (await session.ExecuteAsync($"SELECT id FROM {Q(session, target, items)}")).ResultSets[0].Rows[0][0];

            // The row exists, so the update must succeed even though the engine did not count it.
            var changed = await session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = key }, new Dictionary<string, string?> { ["name"] = "after" }),
            ]);

            Assert.Equal(1, changed);

            var name = (await session.ExecuteAsync($"SELECT name FROM {Q(session, target, items)}")).ResultSets[0];
            Assert.Equal("after", name.Rows[0][0]);

            // Deleted behind the grid's back, still under NOCOUNT: the edit is refused, not lost.
            await session.ExecuteAsync($"DELETE FROM {Q(session, target, items)}");
            var refused = await Assert.ThrowsAsync<DatabaseEditException>(() => session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = key }, new Dictionary<string, string?> { ["name"] = "gone" }),
            ]));
            Assert.Equal(0, refused.EditIndex);
        }
        finally
        {
            await Cleanup(session, target, (items, false));
        }
    }

    [Fact]
    public async Task ASqlServerTriggerThatReturnsRowsOrChangesOthersDoesNotChangeTheEditsCount()
    {
        // The count is read from the last result set: a trigger that SELECTs a 0 of its own comes first, and
        // its three inserts into a log table are not the edit's rows. Reintroducing ExecuteScalar (the first
        // cell) read the trigger's 0 and refused a row that existed.
        var target = new Target(DatabaseKind.SqlServer);
        var items = Unique("trigger");
        var log = Unique("triggerlog");
        var table = new DbObjectName(target.Schema, items);

        await using var session = await DatabaseProviders.Get(DatabaseKind.SqlServer).OpenAsync(Connection(DatabaseKind.SqlServer));

        try
        {
            await Script(session,
                CreateItems(session, target, items),
                $"CREATE TABLE {Q(session, target, log)} (note int NOT NULL)",
                $"CREATE TRIGGER {Q(session, target, items + "_trg")} ON {Q(session, target, items)} AFTER UPDATE AS " +
                $"BEGIN SELECT 0 AS trigger_says; INSERT INTO {Q(session, target, log)} (note) VALUES (1), (2), (3); END",
                $"INSERT INTO {Q(session, target, items)} (code, name, amount) VALUES ('t1', 'before', 1)");

            var key = (await session.ExecuteAsync($"SELECT id FROM {Q(session, target, items)}")).ResultSets[0].Rows[0][0];

            var changed = await session.ApplyEditsAsync(table, [
                new RowUpdate(new Dictionary<string, object?> { ["id"] = key }, new Dictionary<string, string?> { ["name"] = "after" }),
            ]);

            Assert.Equal(1, changed);
            var logged = (await session.ExecuteAsync($"SELECT COUNT(*) FROM {Q(session, target, log)}")).ResultSets[0];
            Assert.Equal(3, Convert.ToInt32(Cell(logged, 0, 0)));
        }
        finally
        {
            await Cleanup(session, target, (items, false), (log, false));
        }
    }

    // ---- Helpers ----

    /// <summary>The connection string an engine's environment variable gives, or a skip when there is none.</summary>
    private static object? Cell(ResultSet set, int row, int column) => set.Rows[row][column];

    private static string Connection(DatabaseKind kind)
    {
        var variable = new Target(kind).Variable;
        var connection = Environment.GetEnvironmentVariable(variable);

        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), $"Set {variable} to a connection string to run this against a server.");

        return connection!;
    }

    private static string Unique(string scenario) => $"Basalt_{scenario}_{Guid.NewGuid():N}";

    private static string Q(IDatabaseSession session, Target target, string name) =>
        session.Dialect.QualifiedName(new DbObjectName(target.Schema, name));

    /// <summary>A table with an identity key, a UNIQUE code, a nullable name and a nullable amount.</summary>
    private static string CreateItems(IDatabaseSession session, Target target, string name) =>
        $"CREATE TABLE {Q(session, target, name)} ({target.IdColumn}, code {target.Text} NOT NULL UNIQUE, name {target.Text} NULL, amount int NULL)";

    /// <summary>
    /// A child with a foreign key declared at table level. MySQL ignores a REFERENCES written inside a
    /// column definition, so the constraint is named and written as its own clause on every engine.
    /// </summary>
    private static string ChildTable(IDatabaseSession session, Target target, string name, string parent) =>
        $"CREATE TABLE {Q(session, target, name)} ({target.IdColumn}, item_id int NULL, " +
        $"CONSTRAINT {session.Dialect.Quote("fk_" + name)} FOREIGN KEY (item_id) REFERENCES {Q(session, target, parent)} (id))";

    private static ColumnInfo Column(TableDetails details, string name) =>
        details.Columns.Single(c => c.Name == name);

    /// <summary>Runs each statement, and fails the test on the first the engine refuses.</summary>
    private static async Task Script(IDatabaseSession session, params string[] statements)
    {
        foreach (var sql in statements)
        {
            var result = await session.ExecuteAsync(sql);

            Assert.False(result.Failed, string.Join("; ", result.Messages.Where(m => m.IsError).Select(m => m.Text)) + " in: " + sql);
        }
    }

    /// <summary>
    /// Drops what a test made, in the order given (a view before its table, a child before its parent).
    /// The result is not checked: a cleanup that fails must not hide the test's own failure.
    /// </summary>
    private static async Task Cleanup(IDatabaseSession session, Target target, params (string Name, bool IsView)[] objects)
    {
        foreach (var (name, isView) in objects)
            await session.ExecuteAsync(target.Drop(session.Dialect.QualifiedName(new DbObjectName(target.Schema, name)), isView));
    }

    /// <summary>What differs between the engines in the SQL these scenarios write.</summary>
    private sealed class Target(DatabaseKind kind)
    {
        public string Variable => kind switch
        {
            DatabaseKind.SqlServer => "BASALT_TEST_SQLSERVER",
            DatabaseKind.PostgreSql => "BASALT_TEST_POSTGRES",
            _ => "BASALT_TEST_MYSQL",
        };

        /// <summary>The schema the scenarios use: dbo, public, or none, because MySQL's schema is the database.</summary>
        public string? Schema => kind switch
        {
            DatabaseKind.SqlServer => "dbo",
            DatabaseKind.PostgreSql => "public",
            _ => null,
        };

        public string IdColumn => kind switch
        {
            DatabaseKind.SqlServer => "id int IDENTITY(1,1) PRIMARY KEY",
            DatabaseKind.PostgreSql => "id int GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY",
            _ => "id int AUTO_INCREMENT PRIMARY KEY",
        };

        public string Text => kind == DatabaseKind.SqlServer ? "nvarchar(50)" : "varchar(50)";

        public string? Notice => kind switch
        {
            DatabaseKind.SqlServer => "PRINT 'hello from the batch'",
            DatabaseKind.PostgreSql => "DO $$ BEGIN RAISE NOTICE 'hello from the batch'; END $$",
            _ => null,
        };

        /// <summary>
        /// A statement that runs for a long time on the engine, for the cancel test. MySQL's SLEEP is not
        /// used: a KILL QUERY makes it return 1 with no error, so the cancel would not be seen.
        /// </summary>
        public string Slow => kind switch
        {
            DatabaseKind.SqlServer => "WAITFOR DELAY '00:00:30'",
            DatabaseKind.PostgreSql => "SELECT pg_sleep(30)",
            // A billion-row cross join of a thousand-row sequence: the recursion stays under MySQL's default
            // cte_max_recursion_depth, which a longer recursive count would hit at once with an error.
            _ => "SELECT COUNT(*) FROM (WITH RECURSIVE n(v) AS (SELECT 1 UNION ALL SELECT v + 1 FROM n WHERE v < 1000) SELECT 1 FROM n a CROSS JOIN n b CROSS JOIN n c) t",
        };

        /// <summary>MySqlException carries no line number, so only the other two engines give one.</summary>
        public bool ReportsErrorLine => kind != DatabaseKind.MySql;

        public string Drop(string qualified, bool isView) => (kind, isView) switch
        {
            (DatabaseKind.PostgreSql, false) => $"DROP TABLE IF EXISTS {qualified} CASCADE",
            (_, true) => $"DROP VIEW IF EXISTS {qualified}",
            _ => $"DROP TABLE IF EXISTS {qualified}",
        };
    }
}
