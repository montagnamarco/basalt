using Basalt.Data.Sessions;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// "Script as" and the table properties, on the dialects and on a real SQLite file: the text a user starts a
/// query from, quoted as each engine quotes, and the counts the Properties entry reports.
/// </summary>
public sealed class DatabaseScriptTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-script-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<IDatabaseSession> ShopAsync()
    {
        var path = Path.Combine(_root, "shop.db");

        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, "Home City" TEXT);
                CREATE TABLE Orders (
                    OrderId INTEGER NOT NULL, LineNo INTEGER NOT NULL, CustomerId INTEGER NOT NULL REFERENCES Customers (Id),
                    Amount REAL, PRIMARY KEY (OrderId, LineNo));
                CREATE INDEX IX_Orders_Customer ON Orders (CustomerId);
                CREATE VIEW Names AS SELECT Name FROM Customers;
                CREATE TABLE Loose (A TEXT, B TEXT);
                INSERT INTO Customers (Name) VALUES ('a'), ('b'), ('c');
                """;
            command.ExecuteNonQuery();
        }

        return await new SqliteProvider().OpenAsync($"Data Source={path}");
    }

    /// <summary>A table named after a keyword, with a column that has a space in it, for the quoting of each engine.</summary>
    private static TableDetails Awkward(string? schema) => new(
        new DbObjectName(schema, "select"),
        DbObjectKind.Table,
        [
            new ColumnInfo("Id", "int", false, true, true, null, 1),
            new ColumnInfo("Order Date", "date", true, false, false, null, 2),
            new ColumnInfo("Note", "text", true, false, false, null, 3),
        ],
        [],
        []);

    public static TheoryData<DatabaseKind, ISqlDialect, string, string?> Dialects => new()
    {
        { DatabaseKind.Sqlite, new SqliteDialect(), "\"select\"", "\"Order Date\"" },
        { DatabaseKind.SqlServer, new SqlServerDialect(), "[dbo].[select]", "[Order Date]" },
        { DatabaseKind.PostgreSql, new PostgreSqlDialect(), "\"public\".\"select\"", "\"Order Date\"" },
        { DatabaseKind.MySql, new MySqlDialect(), "`select`", "`Order Date`" },
    };

    [Theory]
    [MemberData(nameof(Dialects))]
    public void EveryScriptIsQuotedAsTheEngineQuotes(DatabaseKind kind, ISqlDialect dialect, string table, string column)
    {
        var schema = kind switch { DatabaseKind.SqlServer => "dbo", DatabaseKind.PostgreSql => "public", _ => null };
        var details = Awkward(schema);

        var select = SqlScripts.Select(dialect, details);
        var insert = SqlScripts.Insert(dialect, details);
        var update = SqlScripts.Update(dialect, details);
        var delete = SqlScripts.Delete(dialect, details);

        Assert.Equal($"SELECT\n    {dialect.Quote("Id")},\n    {column},\n    {dialect.Quote("Note")}\nFROM {table};", select);

        // The identity column is the engine's to fill; the others are placeholders.
        Assert.Equal($"INSERT INTO {table} ({column}, {dialect.Quote("Note")})\nVALUES (<Order Date>, <Note>);", insert);

        Assert.Equal(
            $"UPDATE {table}\nSET\n    {column} = <Order Date>,\n    {dialect.Quote("Note")} = <Note>\nWHERE {dialect.Quote("Id")} = <Id>;",
            update);

        Assert.Equal($"DELETE FROM {table}\nWHERE {dialect.Quote("Id")} = <Id>;", delete);
    }

    [Fact]
    public void ACompositeKeyIsAllInTheWhereAndATableWithoutOneGetsAConditionPlaceholder()
    {
        var dialect = new SqliteDialect();

        var composite = new TableDetails(
            new DbObjectName(null, "Orders"), DbObjectKind.Table,
            [
                new ColumnInfo("OrderId", "INTEGER", false, true, false, null, 1),
                new ColumnInfo("LineNo", "INTEGER", false, true, false, null, 2),
                new ColumnInfo("Amount", "REAL", true, false, false, null, 3),
            ], [], []);

        Assert.Contains("WHERE \"OrderId\" = <OrderId> AND \"LineNo\" = <LineNo>;", SqlScripts.Update(dialect, composite));
        Assert.Contains("WHERE \"OrderId\" = <OrderId> AND \"LineNo\" = <LineNo>;", SqlScripts.Delete(dialect, composite));

        var keyless = composite with { Columns = [.. composite.Columns.Select(c => c with { IsPrimaryKey = false })] };

        // A DELETE with no WHERE would empty the table: the template asks for a condition instead.
        Assert.Contains("WHERE <condition>;", SqlScripts.Delete(dialect, keyless));
        Assert.Contains("WHERE <condition>;", SqlScripts.Update(dialect, keyless));
    }

    [Fact]
    public async Task ScriptsFromARealTableCanBeCompletedAndRun()
    {
        await using var session = await ShopAsync();

        var details = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        var dialect = session.Dialect;

        // The names come from the catalogue, so "Home City" is quoted and the statements run as they are once
        // the placeholders are replaced.
        var select = SqlScripts.Select(dialect, details);
        var set = (await session.ExecuteAsync(select)).ResultSets.Single();

        Assert.Equal(["Id", "Name", "Home City"], set.Columns.Select(c => c.Name));
        Assert.Equal(3, set.Rows.Count);

        var insert = SqlScripts.Insert(dialect, details).Replace("<Name>", "'d'").Replace("<Home City>", "'Rome'");
        Assert.False((await session.ExecuteAsync(insert)).Failed, insert);

        var update = SqlScripts.Update(dialect, details)
            .Replace("<Name>", "'z'").Replace("<Home City>", "NULL").Replace("<Id>", "1");
        Assert.False((await session.ExecuteAsync(update)).Failed, update);

        var delete = SqlScripts.Delete(dialect, details).Replace("<Id>", "2");
        Assert.False((await session.ExecuteAsync(delete)).Failed, delete);

        var names = (await session.ExecuteAsync("SELECT Name FROM Customers ORDER BY Id")).ResultSets.Single().Rows
            .Select(r => (string?)r[0]);

        Assert.Equal(["z", "c", "d"], names);
    }

    [Fact]
    public async Task CreateOnSqliteIsTheStatementTheTableWasMadeWith()
    {
        await using var session = await ShopAsync();

        var details = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));

        var create = await SqlScripts.CreateAsync(session, details);

        // sqlite_master.sql keeps the user's own spelling, which a script written from the catalogue would lose.
        Assert.Equal("CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, \"Home City\" TEXT);", create);

        var view = await SqlScripts.CreateAsync(session, await session.GetDetailsAsync(new DbObjectName(null, "Names")));

        Assert.Equal("CREATE VIEW Names AS SELECT Name FROM Customers;", view);
    }

    [Fact]
    public void CreateForOtherEnginesIsWrittenFromTheCatalogueWithKeysAndForeignKeys()
    {
        var dialect = new PostgreSqlDialect();

        var table = new TableDetails(
            new DbObjectName("public", "Orders"), DbObjectKind.Table,
            [
                new ColumnInfo("Id", "integer", false, true, false, null, 1),
                new ColumnInfo("CustomerId", "integer", false, false, false, null, 2),
                new ColumnInfo("Note", "text", true, false, false, "'none'", 3),
            ],
            [],
            [new ForeignKeyInfo("fk", ["CustomerId"], new DbObjectName("public", "Customers"), ["Id"])]);

        Assert.Equal(
            "-- Approximate: written from the catalogue. Check constraints, collations, referential actions\n"
            + "-- and storage options are not included; review it before running.\n"
            + "CREATE TABLE \"public\".\"Orders\" (\n"
            + "    \"Id\" integer NOT NULL,\n"
            + "    \"CustomerId\" integer NOT NULL,\n"
            + "    \"Note\" text DEFAULT 'none',\n"
            + "    PRIMARY KEY (\"Id\"),\n"
            + "    FOREIGN KEY (\"CustomerId\") REFERENCES \"public\".\"Customers\" (\"Id\")\n);",
            SqlScripts.CreateFromCatalogue(dialect, table));
    }

    [Fact]
    public async Task PropertiesCountTheRowsColumnsIndexesAndForeignKeys()
    {
        await using var session = await ShopAsync();

        var customers = await ObjectStatistics.ReadAsync(session, new DbObjectName(null, "Customers"));
        var orders = await ObjectStatistics.ReadAsync(session, new DbObjectName(null, "Orders"));
        var view = await ObjectStatistics.ReadAsync(session, new DbObjectName(null, "Names"));

        Assert.Equal((3L, 3, 0, 0), (customers.RowCount, customers.ColumnCount, customers.IndexCount, customers.ForeignKeyCount));

        // Orders: the index the user made, and the one SQLite builds for a composite key.
        Assert.Equal((0L, 4, 2, 1), (orders.RowCount, orders.ColumnCount, orders.IndexCount, orders.ForeignKeyCount));

        Assert.Equal((DbObjectKind.View, 3L, 1), (view.Kind, view.RowCount, view.ColumnCount));
    }

    [Fact]
    public async Task ATableWithoutAPrimaryKeyIsScriptedWithAConditionPlaceholderFromTheRealCatalogue()
    {
        await using var session = await ShopAsync();

        var loose = await session.GetDetailsAsync(new DbObjectName(null, "Loose"));

        Assert.Empty(loose.KeyColumns);

        // No key to name the row by: the statements ask for a condition instead of touching every row.
        Assert.Equal("DELETE FROM \"Loose\"\nWHERE <condition>;", SqlScripts.Delete(session.Dialect, loose));
        Assert.Equal("UPDATE \"Loose\"\nSET\n    \"A\" = <A>,\n    \"B\" = <B>\nWHERE <condition>;", SqlScripts.Update(session.Dialect, loose));

        // And the template is not runnable until completed: the placeholder is not valid SQL.
        Assert.True((await session.ExecuteAsync(SqlScripts.Delete(session.Dialect, loose))).Failed);
    }

    public static TheoryData<ISqlDialect, string, string> OddNames => new()
    {
        { new SqliteDialect(), "\"a\"\"b]c`d\"", "\"x\"\"y]z`w\"" },
        { new SqlServerDialect(), "[a\"b]]c`d]", "[x\"y]]z`w]" },
        { new PostgreSqlDialect(), "\"a\"\"b]c`d\"", "\"x\"\"y]z`w\"" },
        { new MySqlDialect(), "`a\"b]c``d`", "`x\"y]z``w`" },
    };

    [Theory]
    [MemberData(nameof(OddNames))]
    public void NamesWithAQuoteABracketOrABacktickAreEscapedByEachDialect(ISqlDialect dialect, string table, string column)
    {
        var details = new TableDetails(
            new DbObjectName(null, "a\"b]c`d"), DbObjectKind.Table,
            [
                new ColumnInfo("x\"y]z`w", "int", false, true, false, null, 1),
                new ColumnInfo("other", "int", true, false, false, null, 2),
            ], [], []);

        Assert.Equal($"SELECT\n    {column},\n    {dialect.Quote("other")}\nFROM {table};", SqlScripts.Select(dialect, details));
        Assert.Equal($"INSERT INTO {table} ({column}, {dialect.Quote("other")})\nVALUES (<x\"y]z`w>, <other>);", SqlScripts.Insert(dialect, details));
        Assert.Equal($"DELETE FROM {table}\nWHERE {column} = <x\"y]z`w>;", SqlScripts.Delete(dialect, details));
        Assert.Equal($"UPDATE {table}\nSET\n    {dialect.Quote("other")} = <other>\nWHERE {column} = <x\"y]z`w>;", SqlScripts.Update(dialect, details));
    }

    private static TableDetails Invoices(string? schema) => new(
        new DbObjectName(schema, "Invoices"), DbObjectKind.Table,
        [
            new ColumnInfo("Year", "int", false, true, false, null, 1),
            new ColumnInfo("Number", "int", false, true, true, null, 2),
            new ColumnInfo("Status", "varchar(10)", false, false, false, "'open'", 3),
            new ColumnInfo("Code", "varchar(20)", true, false, false, null, 4),
        ],
        [
            new IndexInfo("PRIMARY", ["Year", "Number"], true, true),
            new IndexInfo("UX_Invoices_Code", ["Code"], true, false),
            new IndexInfo("IX_Invoices_Status", ["Status", "Year"], false, false),
        ],
        []);

    [Fact]
    public void MySqlCreateKeepsAutoIncrementTheCompositeKeyTheStringDefaultAndTheIndexes()
    {
        var create = SqlScripts.CreateFromCatalogue(new MySqlDialect(), Invoices(null));

        Assert.StartsWith("-- Approximate:", create);
        Assert.Contains("`Number` int NOT NULL AUTO_INCREMENT,", create);
        Assert.Contains("`Status` varchar(10) NOT NULL DEFAULT 'open',", create);
        Assert.Contains("PRIMARY KEY (`Year`, `Number`)", create);
        Assert.Contains("CREATE UNIQUE INDEX `UX_Invoices_Code` ON `Invoices` (`Code`);", create);
        Assert.Contains("CREATE INDEX `IX_Invoices_Status` ON `Invoices` (`Status`, `Year`);", create);

        // The primary key's own index is not written twice.
        Assert.DoesNotContain("INDEX `PRIMARY`", create);
    }

    [Fact]
    public void SqlServerCreateKeepsIdentityTheCompositeKeyTheStringDefaultAndTheIndexes()
    {
        var create = SqlScripts.CreateFromCatalogue(new SqlServerDialect(), Invoices("dbo"));

        Assert.Contains("[Number] int IDENTITY(1,1) NOT NULL,", create);
        Assert.Contains("[Status] varchar(10) NOT NULL DEFAULT 'open',", create);
        Assert.Contains("PRIMARY KEY ([Year], [Number])", create);
        Assert.Contains("CREATE UNIQUE INDEX [UX_Invoices_Code] ON [dbo].[Invoices] ([Code]);", create);
        Assert.Contains("CREATE INDEX [IX_Invoices_Status] ON [dbo].[Invoices] ([Status], [Year]);", create);
    }

    [Fact]
    public void PostgreSqlCreateWritesAnIdentityColumnWithoutItsSequenceDefault()
    {
        var table = Invoices("public");
        table = table with { Columns = [.. table.Columns.Select(c => c.Name == "Number" ? c with { DefaultValue = "nextval('s')" } : c)] };

        var create = SqlScripts.CreateFromCatalogue(new PostgreSqlDialect(), table);

        Assert.Contains("\"Number\" int GENERATED BY DEFAULT AS IDENTITY NOT NULL,", create);
        Assert.DoesNotContain("nextval", create);
    }

    [Fact]
    public void AnExpressionIndexIsNamedInAnApproximateScriptRatherThanWrittenWrong()
    {
        var table = Invoices(null) with { Indexes = [new IndexInfo("IX_Expr", ["(expression)"], false, false)] };

        var create = SqlScripts.CreateFromCatalogue(new MySqlDialect(), table);

        Assert.Contains("-- Index IX_Expr is on an expression and is not scripted.", create);
        Assert.DoesNotContain("CREATE INDEX", create);
    }

    [Theory]
    [InlineData("varchar(10)", "", "abc", "'abc'")]
    [InlineData("varchar(10)", "", "it's", "'it''s'")]
    [InlineData("varchar(10)", "", "a\\b", "'a\\\\b'")]
    [InlineData("int", "", "5", "5")]
    [InlineData("decimal(5,2)", "", "0.50", "0.50")]
    [InlineData("varchar(10)", "", "'abc'", "'abc'")]
    [InlineData("datetime", "DEFAULT_GENERATED", "CURRENT_TIMESTAMP", "CURRENT_TIMESTAMP")]
    [InlineData("varchar(36)", "DEFAULT_GENERATED", "uuid()", "(uuid())")]
    public void MySqlStringDefaultsAreQuotedAndExpressionsAreNot(string type, string extra, string value, string expected) =>
        Assert.Equal(expected, MySqlSession.DefaultText(type, extra, value));

    [Fact]
    public void MySqlNoDefaultStaysNone()
    {
        Assert.Null(MySqlSession.DefaultText("varchar(10)", "", null));
        Assert.Null(MySqlSession.DefaultText("varchar(10)", "", "NULL"));
    }

    [Fact]
    public async Task CountingRowsStopsWhenItsTokenIsCancelled()
    {
        await using var session = await ShopAsync();

        await session.ExecuteAsync(
            "CREATE VIEW Slow AS WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 1000000000) SELECT x FROM c");

        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ObjectStatistics.ReadAsync(session, new DbObjectName(null, "Slow"), cancel.Token));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "The count ran on after it was cancelled.");
    }

    [Fact]
    public async Task ACountThatFailsWithoutAMessageIsAnErrorNotAZero()
    {
        await using var inner = await ShopAsync();
        var silent = new SilentSession(inner);

        // The session answers with no row and no message: zero rows would be a lie.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ObjectStatistics.ReadAsync(silent, new DbObjectName(null, "Customers")));

        silent.Fail = true;

        // A failed batch whose error carries no text still ends as an exception, not a crash in the reader.
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ObjectStatistics.ReadAsync(silent, new DbObjectName(null, "Customers")));

        Assert.False(string.IsNullOrWhiteSpace(thrown.Message));
    }

    /// <summary>A session whose batches answer nothing, or fail without saying why.</summary>
    private sealed class SilentSession(IDatabaseSession inner) : IDatabaseSession
    {
        public bool Fail { get; set; }

        public DatabaseKind Kind => inner.Kind;
        public string DisplayName => inner.DisplayName;
        public ISqlDialect Dialect => inner.Dialect;

        public Task<IReadOnlyList<DbObjectInfo>> GetObjectsAsync(CancellationToken ct = default) => inner.GetObjectsAsync(ct);
        public Task<TableDetails> GetDetailsAsync(DbObjectName name, CancellationToken ct = default) => inner.GetDetailsAsync(name, ct);

        public Task<QueryBatchResult> ExecuteAsync(string sql, int maxRows = 10000, CancellationToken ct = default) =>
            Task.FromResult(new QueryBatchResult([], Fail ? [new QueryMessage("", true, null)] : [], TimeSpan.Zero));

        public Task<DataPage> ReadPageAsync(DataPageRequest request, CancellationToken ct = default) => inner.ReadPageAsync(request, ct);
        public Task<int> ApplyEditsAsync(DbObjectName table, IReadOnlyList<RowEdit> edits, CancellationToken ct = default) => inner.ApplyEditsAsync(table, edits, ct);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
