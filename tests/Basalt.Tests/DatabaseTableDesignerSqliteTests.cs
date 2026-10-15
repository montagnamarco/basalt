using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The table designer's model and script, run against real SQLite files: a table is created, then changed in each
/// way the designer allows, and every result is checked by reading the schema and the rows back through a session.
/// </summary>
public sealed class DatabaseTableDesignerSqliteTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-table-designer-").FullName;
    private readonly List<IDatabaseSession> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions) session.DisposeAsync().AsTask().GetAwaiter().GetResult();

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<IDatabaseSession> OpenAsync(string? script = null)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".sqlite");
        var connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = script ?? "CREATE TABLE _seed (x INTEGER)";
            command.ExecuteNonQuery();
        }

        var session = await new SqliteProvider().OpenAsync(connectionString);
        _sessions.Add(session);

        return session;
    }

    private static readonly TableScriptGenerator Generator = TableScriptGenerator.For(DatabaseKind.Sqlite);

    private static async Task<TableScript> SaveAsync(IDatabaseSession session, TableDesign? original, TableDesign edited)
    {
        var script = Generator.Script(original, edited);

        await TableDesignRunner.ApplyAsync(session, script);

        return script;
    }

    private static async Task<TableDesign> LoadAsync(IDatabaseSession session, string table) =>
        await TableDesignRunner.LoadAsync(session, new DbObjectName(null, table));

    private static async Task<object?[][]> RowsAsync(IDatabaseSession session, string sql)
    {
        var result = await session.ExecuteAsync(sql);

        Assert.False(result.Failed, string.Join("; ", result.Messages.Select(m => m.Text)));

        return [.. result.ResultSets[0].Rows.Select(r => r.ToArray())];
    }

    private static TableDesign Edit(TableDesign design, Func<List<DesignColumn>, List<DesignColumn>> change) =>
        design with { Columns = change([.. design.Columns]) };

    private static DesignColumn Col(string name, string type, bool nullable = true, bool key = false, string? fallback = null, bool identity = false) =>
        new(name, type, nullable, key, fallback, identity);

    private const string Customers = """
        CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, Age INTEGER, City TEXT DEFAULT 'Roma');
        INSERT INTO Customers (Name, Age, City) VALUES ('Rossi', 41, 'Milano'), ('Bianchi', 29, NULL), ('Verdi', 35, 'Torino');
        """;

    [Fact]
    public async Task ANewTableIsCreatedWithItsColumnsKeyAndDefault()
    {
        var session = await OpenAsync();

        var design = TableDesign.Empty(new DbObjectName(null, "Products")) with
        {
            Columns =
            [
                Col("Id", "INTEGER", nullable: false, key: true, identity: true),
                Col("Name", "TEXT", nullable: false),
                Col("Price", "REAL", fallback: "9.5"),
                Col("Added", "TEXT", fallback: "CURRENT_TIMESTAMP"),
            ],
        };

        Assert.Empty(design.Validate(DatabaseKind.Sqlite));
        await SaveAsync(session, null, design);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Products"));

        Assert.Equal(["Id", "Name", "Price", "Added"], table.Columns.Select(c => c.Name));
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.True(table.Columns[0].IsIdentity);
        Assert.False(table.Columns[1].IsNullable);
        Assert.Equal("9.5", table.Columns[2].DefaultValue);
        Assert.Equal("CURRENT_TIMESTAMP", table.Columns[3].DefaultValue);

        // The identity assigns the key: an insert without one gets 1.
        await session.ExecuteAsync("INSERT INTO Products (Name) VALUES ('Pen')");
        Assert.Equal(1L, (await RowsAsync(session, "SELECT Id FROM Products"))[0][0]);
    }

    [Fact]
    public async Task AddingAColumnIsAnAlterTableAndKeepsTheRows()
    {
        var session = await OpenAsync(Customers);
        var original = await LoadAsync(session, "Customers");

        var edited = Edit(original, c => [.. c, Col("Note", "TEXT")]);
        var script = await SaveAsync(session, original, edited);

        // SQLite adds a column in place: no rebuild, no foreign key switch.
        Assert.Contains("ADD COLUMN", script.Text);
        Assert.DoesNotContain("DROP TABLE", script.Text);
        Assert.Empty(script.Before);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.Equal(["Id", "Name", "Age", "City", "Note"], table.Columns.Select(c => c.Name));
        Assert.Equal(3, (await RowsAsync(session, "SELECT * FROM Customers")).Length);
    }

    [Fact]
    public async Task RenamingAColumnKeepsItsData()
    {
        var session = await OpenAsync(Customers);
        await session.ExecuteAsync("PRAGMA foreign_keys = ON");
        var original = await LoadAsync(session, "Customers");

        var edited = Edit(original, c => [.. c.Select(x => x.Name == "City" ? x with { Name = "Town" } : x)]);
        var script = await SaveAsync(session, original, edited);

        Assert.Contains("PRAGMA foreign_keys = OFF;", script.Text);
        Assert.Contains("PRAGMA foreign_key_check;", script.Text);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.Equal(["Id", "Name", "Age", "Town"], table.Columns.Select(c => c.Name));
        Assert.Equal("Roma", table.Columns[3].DefaultValue?.Trim('\''));

        var rows = await RowsAsync(session, "SELECT Name, Town FROM Customers ORDER BY Id");
        Assert.Equal(["Rossi", "Milano"], rows[0]);
        Assert.Null(rows[1][1]);
        Assert.Equal(["Verdi", "Torino"], rows[2]);
        Assert.Equal(1L, (await RowsAsync(session, "PRAGMA foreign_keys"))[0][0]);
    }

    [Fact]
    public async Task ChangingATypeRebuildsTheTableAndKeepsTheRows()
    {
        var session = await OpenAsync(Customers);
        var original = await LoadAsync(session, "Customers");

        var edited = Edit(original, c => [.. c.Select(x => x.Name == "Age" ? x with { DataType = "TEXT" } : x)]);
        await SaveAsync(session, original, edited);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.Equal("TEXT", table.Columns.Single(c => c.Name == "Age").DataType);

        var rows = await RowsAsync(session, "SELECT Age, typeof(Age) FROM Customers ORDER BY Id");
        Assert.Equal(["41", "text"], rows[0]);
    }

    [Fact]
    public async Task DroppingAColumnRemovesItAndKeepsTheOthers()
    {
        var session = await OpenAsync(Customers);
        var original = await LoadAsync(session, "Customers");

        var edited = Edit(original, c => [.. c.Where(x => x.Name != "Age")]);
        await SaveAsync(session, original, edited);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.Equal(["Id", "Name", "City"], table.Columns.Select(c => c.Name));
        Assert.Equal(["Bianchi", null], (await RowsAsync(session, "SELECT Name, City FROM Customers WHERE Id = 2"))[0]);
    }

    [Fact]
    public async Task SettingAPrimaryKeyOnATableWithoutOne()
    {
        var session = await OpenAsync("""
            CREATE TABLE Log (Code TEXT NOT NULL, Message TEXT);
            INSERT INTO Log VALUES ('a', 'first'), ('b', 'second');
            """);
        var original = await LoadAsync(session, "Log");

        Assert.Empty(original.KeyColumns);

        var edited = Edit(original, c => [.. c.Select(x => x.Name == "Code" ? x with { IsPrimaryKey = true } : x)]);
        await SaveAsync(session, original, edited);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Log"));
        Assert.True(table.IsEditable);
        Assert.Equal(["Code"], table.KeyColumns.Select(c => c.Name));
        Assert.Equal(2, (await RowsAsync(session, "SELECT * FROM Log")).Length);
    }

    [Fact]
    public async Task ARebuildCarriesOverIndexesForeignKeyActionsUniquesAndTriggers()
    {
        var session = await OpenAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (
                Id INTEGER PRIMARY KEY,
                ParentId INTEGER REFERENCES Parent (Id) ON DELETE CASCADE,
                Code TEXT UNIQUE,
                Label TEXT);
            CREATE INDEX IX_Child_Label ON Child (Label);
            CREATE TABLE Audit (Note TEXT);
            CREATE TRIGGER Child_Insert AFTER INSERT ON Child BEGIN INSERT INTO Audit VALUES ('added'); END;
            INSERT INTO Parent VALUES (1);
            INSERT INTO Child VALUES (10, 1, 'x', 'one');
            """);
        var original = await LoadAsync(session, "Child");

        // Rename a column that is not involved, forcing a rebuild.
        var edited = Edit(original, c => [.. c.Select(x => x.Name == "Label" ? x with { Name = "Title" } : x)]);
        await SaveAsync(session, original, edited);

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Child"));

        Assert.Contains(table.Indexes, i => i.Name == "IX_Child_Label" && i.Columns.SequenceEqual(["Title"]));
        Assert.Contains(table.Indexes, i => i.IsUnique && i.Columns.SequenceEqual(["Code"]));
        Assert.Single(table.ForeignKeys);

        var sql = (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'Child'"))[0][0]!;
        Assert.Contains("ON DELETE CASCADE", sql);

        // The trigger still fires, and deleting the parent still cascades.
        await session.ExecuteAsync("INSERT INTO Child VALUES (11, 1, 'y', 'two')");
        Assert.Equal(2, (await RowsAsync(session, "SELECT * FROM Audit")).Length);
        await session.ExecuteAsync("DELETE FROM Parent");
        Assert.Empty(await RowsAsync(session, "SELECT * FROM Child"));
    }

    [Fact]
    public async Task AFailedRebuildLeavesTheTableAsItWasAndForeignKeysOn()
    {
        var session = await OpenAsync(Customers);
        await session.ExecuteAsync("PRAGMA foreign_keys = ON");
        var original = await LoadAsync(session, "Customers");

        // Bianchi has no city: NOT NULL cannot hold the copy.
        var edited = Edit(original, c => [.. c.Select(x => x.Name == "City" ? x with { IsNullable = false, DefaultValue = null } : x)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(session, original, edited));

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Customers"));
        Assert.True(table.Columns.Single(c => c.Name == "City").IsNullable);
        Assert.Equal(3, (await RowsAsync(session, "SELECT * FROM Customers")).Length);
        Assert.Empty(await RowsAsync(session, "SELECT name FROM sqlite_master WHERE name LIKE 'new_%'"));
        Assert.Equal(1L, (await RowsAsync(session, "PRAGMA foreign_keys"))[0][0]);

        // The transaction was rolled back: a new one can begin.
        await session.ExecuteAsync("BEGIN");
        await session.ExecuteAsync("ROLLBACK");
    }

    [Fact]
    public async Task ARebuildThatBreaksAForeignKeyIsRolledBack()
    {
        var session = await OpenAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY, Code TEXT);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentCode TEXT, FOREIGN KEY (ParentCode) REFERENCES Parent (Id));
            INSERT INTO Parent VALUES (1, 'a');
            INSERT INTO Child VALUES (1, '1');
            """);
        await session.ExecuteAsync("PRAGMA foreign_keys = ON");

        // Renaming the key column the child's foreign key points at leaves the child pointing nowhere.
        var parent = await LoadAsync(session, "Parent");
        var edited = Edit(parent, c => [.. c.Select(x => x.Name == "Id" ? x with { Name = "Number" } : x)]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(session, parent, edited));

        var table = await session.GetDetailsAsync(new DbObjectName(null, "Parent"));
        Assert.Equal(["Id", "Code"], table.Columns.Select(c => c.Name));
        Assert.Equal(1L, (await RowsAsync(session, "PRAGMA foreign_keys"))[0][0]);
    }

    [Fact]
    public async Task NamesWithTheQuoteCharacterAreQuotedInTheScriptAndRoundTrip()
    {
        var session = await OpenAsync();
        var name = new DbObjectName(null, "we\"ird");

        var design = TableDesign.Empty(name) with
        {
            Columns = [Col("a\"b", "TEXT", nullable: false, key: true), Col("c d", "INTEGER")],
        };

        var script = await SaveAsync(session, null, design);
        Assert.Contains("\"we\"\"ird\"", script.Text);
        Assert.Contains("\"a\"\"b\"", script.Text);

        var original = await LoadAsync(session, "we\"ird");
        await session.ExecuteAsync("INSERT INTO \"we\"\"ird\" VALUES ('k', 1)");

        var edited = Edit(original, c => [.. c.Select(x => x.Name == "c d" ? x with { Name = "e\"f" } : x)]);
        await SaveAsync(session, original, edited);

        var table = await session.GetDetailsAsync(name);
        Assert.Equal(["a\"b", "e\"f"], table.Columns.Select(c => c.Name));
        Assert.Equal(["k", 1L], (await RowsAsync(session, "SELECT * FROM \"we\"\"ird\""))[0]);
    }

    [Fact]
    public async Task AnUnchangedDesignProducesNoScript()
    {
        var session = await OpenAsync(Customers);
        var original = await LoadAsync(session, "Customers");

        var script = Generator.Script(original, original);

        Assert.True(script.IsEmpty);
        Assert.Equal("", script.Text);
    }

    [Fact]
    public void ADesignWithNoNameADuplicateOrNoTypeIsRefused()
    {
        var design = TableDesign.Empty(new DbObjectName(null, "T")) with
        {
            Columns = [Col("a", "TEXT"), Col("A", "TEXT"), Col("", "TEXT"), Col("b", " ")],
        };

        Assert.Equal(3, design.Validate(DatabaseKind.Sqlite).Count);
        Assert.NotEmpty(TableDesign.Empty(new DbObjectName(null, "T")).Validate(DatabaseKind.Sqlite));
    }

    // ------------------------------------------------------------------
    // What a rebuild must not lose
    // ------------------------------------------------------------------

    [Fact]
    public async Task ARebuildKeepsAutoincrementAndTheCounterEvenAfterTheLastRowsWereDeleted()
    {
        var session = await OpenAsync("""
            CREATE TABLE Tickets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT);
            INSERT INTO Tickets (Title) VALUES ('a'), ('b'), ('c');
            DELETE FROM Tickets WHERE Id > 1;
            """);
        var original = await LoadAsync(session, "Tickets");

        var edited = Edit(original, c => [.. c.Select(x => x.Name == "Title" ? x with { Name = "Subject" } : x)]);
        var script = await SaveAsync(session, original, edited);

        Assert.Contains("AUTOINCREMENT", script.Text);
        Assert.Empty(script.Warnings);

        // Ids 2 and 3 were used: the next one is 4, as it would have been without the rebuild.
        await session.ExecuteAsync("INSERT INTO Tickets (Subject) VALUES ('d')");
        Assert.Equal(4L, (await RowsAsync(session, "SELECT max(Id) FROM Tickets"))[0][0]);
        Assert.Single(await RowsAsync(session, "SELECT * FROM sqlite_sequence WHERE name = 'Tickets'"));
        Assert.Empty(await RowsAsync(session, "SELECT * FROM sqlite_sequence WHERE name LIKE 'new_%'"));
    }

    [Fact]
    public async Task ARebuildOfAnEmptyAutoincrementTableKeepsItsCounter()
    {
        var session = await OpenAsync("""
            CREATE TABLE Tickets (Id INTEGER PRIMARY KEY AUTOINCREMENT, Title TEXT);
            INSERT INTO Tickets (Title) VALUES ('a'), ('b'), ('c'), ('d'), ('e');
            DELETE FROM Tickets;
            """);
        var original = await LoadAsync(session, "Tickets");

        await SaveAsync(session, original, Edit(original, c => [.. c, Col("Extra", "TEXT", nullable: false, fallback: "''", key: false)]));

        await session.ExecuteAsync("INSERT INTO Tickets (Title) VALUES ('f')");
        Assert.Equal(6L, (await RowsAsync(session, "SELECT Id FROM Tickets"))[0][0]);
    }

    [Fact]
    public async Task ARebuildRecreatesAPartialUniqueIndexWithItsWhereAndItsRenamedColumn()
    {
        var session = await OpenAsync("""
            CREATE TABLE Items (Id INTEGER PRIMARY KEY, Code TEXT, Active INTEGER);
            CREATE UNIQUE INDEX UX_Items_Code ON Items (Code) WHERE Active = 1;
            INSERT INTO Items (Code, Active) VALUES ('a', 1), ('a', 0);
            """);
        var original = await LoadAsync(session, "Items");

        await SaveAsync(session, original, Edit(original, c => [.. c.Select(x => x.Name == "Code" ? x with { Name = "Sku" } : x)]));

        var sql = (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'UX_Items_Code'"))[0][0]!;
        Assert.Contains("\"Sku\"", sql);
        Assert.Contains("WHERE Active = 1", sql);

        // Only the active rows are unique: a second inactive 'a' is fine, a second active one is refused.
        Assert.False((await session.ExecuteAsync("INSERT INTO Items (Sku, Active) VALUES ('a', 0)")).Failed);
        Assert.True((await session.ExecuteAsync("INSERT INTO Items (Sku, Active) VALUES ('a', 1)")).Failed);
    }

    [Fact]
    public async Task ARebuildKeepsDescAndCollateOnIndexColumns()
    {
        var session = await OpenAsync("""
            CREATE TABLE People (Id INTEGER PRIMARY KEY, Name TEXT, City TEXT);
            CREATE INDEX IX_People ON People (Name COLLATE NOCASE DESC, City DESC);
            """);
        var original = await LoadAsync(session, "People");

        await SaveAsync(session, original, Edit(original, c => [.. c.Select(x => x.Name == "Name" ? x with { Name = "Title" } : x)]));

        var sql = (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'IX_People'"))[0][0]!;
        Assert.Contains("\"Title\" COLLATE NOCASE DESC", sql);
        Assert.Contains("\"City\" DESC", sql);

        // The index really is descending: index_xinfo says so for both key columns.
        var info = await RowsAsync(session, "SELECT name, desc, coll FROM pragma_index_xinfo('IX_People') WHERE key = 1");
        Assert.Equal([["Title", 1L, "NOCASE"], ["City", 1L, "BINARY"]], info.Select(r => r).ToArray());
    }

    [Fact]
    public async Task AnIndexOnAnExpressionOrWhereThatNamesARenamedColumnRefusesTheChange()
    {
        var session = await OpenAsync("""
            CREATE TABLE People (Id INTEGER PRIMARY KEY, Name TEXT, City TEXT);
            CREATE INDEX IX_Lower ON People (lower(Name));
            CREATE INDEX IX_City ON People (City) WHERE Name IS NOT NULL;
            """);
        var original = await LoadAsync(session, "People");

        var script = Generator.Script(original, Edit(original, c => [.. c.Select(x => x.Name == "Name" ? x with { Name = "Title" } : x)]));

        Assert.NotNull(script.Refusal);
        Assert.True(script.IsEmpty);

        // Renaming a column that neither the expression nor the WHERE names is fine: the plain column is renamed in its index.
        var other = Generator.Script(original, Edit(original, c => [.. c.Select(x => x.Name == "City" ? x with { Name = "Town" } : x)]));
        Assert.Null(other.Refusal);

        await TableDesignRunner.ApplyAsync(session, other);

        var sql = (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'IX_City'"))[0][0]!;
        Assert.Contains("\"Town\"", sql);
        Assert.Contains("WHERE Name IS NOT NULL", sql);
        Assert.Contains("lower(Name)", (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'IX_Lower'"))[0][0]!);
    }

    [Fact]
    public async Task ATriggerThatNamesADroppedColumnRefusesTheChangeAndOtherTriggersAreKept()
    {
        var session = await OpenAsync("""
            CREATE TABLE T (Id INTEGER PRIMARY KEY, A TEXT, B TEXT);
            CREATE TABLE Log (Note TEXT);
            CREATE TRIGGER T_Insert AFTER INSERT ON T BEGIN INSERT INTO Log VALUES (NEW.A); END;
            """);
        var original = await LoadAsync(session, "T");

        var drop = Generator.Script(original, Edit(original, c => [.. c.Where(x => x.Name != "A")]));
        Assert.Contains("T_Insert", drop.Refusal);

        // Dropping a column the trigger does not use is fine, and the trigger survives.
        await SaveAsync(session, original, Edit(original, c => [.. c.Where(x => x.Name != "B")]));
        await session.ExecuteAsync("INSERT INTO T (A) VALUES ('x')");
        Assert.Single(await RowsAsync(session, "SELECT * FROM Log"));
    }

    [Fact]
    public async Task ARebuildKeepsDeferrableForeignKeysAndRefusesWhenItCannotTellTwoKeysApart()
    {
        var session = await OpenAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent (Id) DEFERRABLE INITIALLY DEFERRED, Note TEXT);
            CREATE TABLE Twins (Id INTEGER PRIMARY KEY, A INTEGER REFERENCES Parent (Id) DEFERRABLE INITIALLY DEFERRED, B INTEGER REFERENCES Parent (Id));
            """);
        var child = await LoadAsync(session, "Child");

        await SaveAsync(session, child, Edit(child, c => [.. c.Select(x => x.Name == "Note" ? x with { Name = "Remark" } : x)]));

        var sql = (string)(await RowsAsync(session, "SELECT sql FROM sqlite_master WHERE name = 'Child'"))[0][0]!;
        Assert.Contains("DEFERRABLE INITIALLY DEFERRED", sql);

        // Two keys to one table, one deferrable: foreign_key_list cannot say which, so the rebuild will not guess.
        var twins = await LoadAsync(session, "Twins");

        var rename = Generator.Script(twins, Edit(twins, c => [.. c.Select(x => x.Name == "Id" ? x with { Name = "Number" } : x)]));
        Assert.NotNull(rename.Refusal);
    }

    [Fact]
    public async Task ASelfReferencingKeyFollowsARenamedReferencedColumn()
    {
        var session = await OpenAsync("""
            CREATE TABLE Staff (Id INTEGER PRIMARY KEY, Name TEXT, ManagerId INTEGER REFERENCES Staff (Id));
            INSERT INTO Staff VALUES (1, 'boss', NULL), (2, 'worker', 1);
            """);
        await session.ExecuteAsync("PRAGMA foreign_keys = ON");
        var original = await LoadAsync(session, "Staff");

        await SaveAsync(session, original, Edit(original, c => [.. c.Select(x => x.Name == "Id" ? x with { Name = "Number" } : x)]));

        var key = await RowsAsync(session, "SELECT \"table\", \"from\", \"to\" FROM pragma_foreign_key_list('Staff')");
        Assert.Equal(["Staff", "ManagerId", "Number"], key.Single());

        // Still enforced, against the renamed column.
        Assert.True((await session.ExecuteAsync("INSERT INTO Staff VALUES (3, 'x', 99)")).Failed);
        Assert.False((await session.ExecuteAsync("INSERT INTO Staff VALUES (3, 'x', 2)")).Failed);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    public async Task ARebuildPutsForeignKeysBackToTheValueTheyHad(long before)
    {
        var session = await OpenAsync(Customers);
        await session.ExecuteAsync($"PRAGMA foreign_keys = {before}");
        var original = await LoadAsync(session, "Customers");

        var script = await SaveAsync(session, original, Edit(original, c => [.. c.Select(x => x.Name == "City" ? x with { Name = "Town" } : x)]));

        Assert.Equal(before, (await RowsAsync(session, "PRAGMA foreign_keys"))[0][0]);
        Assert.NotNull(script.SettingQuery);
    }

    [Fact]
    public async Task AViolationAlreadyInTheDatabaseStopsTheRebuildWithTheCountInTheMessage()
    {
        var session = await OpenAsync("""
            CREATE TABLE Parent (Id INTEGER PRIMARY KEY, Code TEXT);
            CREATE TABLE Child (Id INTEGER PRIMARY KEY, ParentId INTEGER REFERENCES Parent (Id));
            INSERT INTO Parent VALUES (1, 'a');
            """);

        // An orphan, written while the keys were not enforced.
        await session.ExecuteAsync("PRAGMA foreign_keys = OFF");
        await session.ExecuteAsync("INSERT INTO Child VALUES (1, 99)");
        await session.ExecuteAsync("PRAGMA foreign_keys = ON");

        var parent = await LoadAsync(session, "Parent");
        var edited = Edit(parent, c => [.. c.Select(x => x.Name == "Code" ? x with { Name = "Label" } : x)]);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(session, parent, edited));

        Assert.Equal(Localizer.Get(StringKeys.TableDesignerForeignKeyViolation, 1), failure.Message);
        Assert.Equal(["Id", "Code"], (await session.GetDetailsAsync(new DbObjectName(null, "Parent"))).Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task TheRebuildScriptOpensWithThePragmaTheTransactionAndANoteToRunItWhole()
    {
        var session = await OpenAsync(Customers);
        var original = await LoadAsync(session, "Customers");

        var script = Generator.Script(original, Edit(original, c => [.. c.Select(x => x.Name == "City" ? x with { Name = "Town" } : x)]));
        var lines = script.Text.ReplaceLineEndings("\n").Split('\n');

        Assert.StartsWith("-- ", lines[0]);
        Assert.Contains("whole", lines[0]);
        Assert.Equal("PRAGMA foreign_keys = OFF;", lines[1]);
        Assert.Equal("BEGIN TRANSACTION;", lines[2]);
        Assert.Equal("PRAGMA foreign_keys = ON;", lines[^1]);
        Assert.True(script.IsDestructive);

        // An added column is not a rebuild and carries none of that.
        Assert.False(Generator.Script(original, Edit(original, c => [.. c, Col("Note", "TEXT")])).IsDestructive);
    }

    [Fact]
    public async Task TheRebuildWarnsOnlyAboutWhatIsStillLost()
    {
        var session = await OpenAsync("""
            CREATE TABLE Rules (Id INTEGER PRIMARY KEY, Name TEXT, Amount INTEGER CHECK (Amount > 0));
            CREATE TABLE Plain (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);
            """);

        var rules = await LoadAsync(session, "Rules");
        var lost = Generator.Script(rules, Edit(rules, c => [.. c.Select(x => x.Name == "Name" ? x with { Name = "Title" } : x)]));
        Assert.Single(lost.Warnings);

        var plain = await LoadAsync(session, "Plain");
        var kept = Generator.Script(plain, Edit(plain, c => [.. c.Select(x => x.Name == "Name" ? x with { Name = "Title" } : x)]));
        Assert.Empty(kept.Warnings);
    }

    [Fact]
    public async Task TheDesignerRefusesToRunOverATableThatChangedAfterItWasLoaded()
    {
        var session = await OpenAsync(Customers);
        var loaded = await LoadAsync(session, "Customers");

        Assert.True(await TableDesignRunner.IsUnchangedAsync(session, loaded));

        await session.ExecuteAsync("ALTER TABLE Customers ADD COLUMN Note TEXT");
        Assert.False(await TableDesignRunner.IsUnchangedAsync(session, loaded));

        // A new index and a new trigger are changes as well.
        var again = await LoadAsync(session, "Customers");
        await session.ExecuteAsync("CREATE INDEX IX_Late ON Customers (Name)");
        Assert.False(await TableDesignRunner.IsUnchangedAsync(session, again));

        again = await LoadAsync(session, "Customers");
        await session.ExecuteAsync("CREATE TRIGGER Late AFTER INSERT ON Customers BEGIN SELECT 1; END");
        Assert.False(await TableDesignRunner.IsUnchangedAsync(session, again));
    }

    [Fact]
    public async Task AnIndexWhereChangedByAnotherConnectionOrACheckAddedElsewhereIsAChange()
    {
        var session = await OpenAsync("""
            CREATE TABLE Items (Id INTEGER PRIMARY KEY, Code TEXT, Active INTEGER);
            CREATE UNIQUE INDEX UX_Items_Code ON Items (Code) WHERE Active = 1;
            """);
        var loaded = await LoadAsync(session, "Items");

        // Same name, same columns, same uniqueness: only the WHERE differs.
        await session.ExecuteAsync("DROP INDEX UX_Items_Code; CREATE UNIQUE INDEX UX_Items_Code ON Items (Code) WHERE Active = 2");
        Assert.False(await TableDesignRunner.IsUnchangedAsync(session, loaded));

        // A CHECK appears only in the table's own statement.
        var other = await OpenAsync("CREATE TABLE Plain (Id INTEGER PRIMARY KEY, A INTEGER)");
        var plain = await LoadAsync(other, "Plain");
        await other.ExecuteAsync("ALTER TABLE Plain ADD COLUMN B INTEGER CHECK (B > 0)");
        var after = await LoadAsync(other, "Plain");
        Assert.False(plain.SameStructureAs(after with { Columns = plain.Columns }));
    }
}
