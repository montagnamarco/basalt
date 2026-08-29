using Basalt.Data;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// Querying a database from inside the IDE.
/// </summary>
/// <remarks>
/// SQLite because it is a file rather than a server: it works the moment the
/// IDE is installed, with nothing to set up. The shape here — open, list,
/// query — is what any other provider needs too.
/// </remarks>
public sealed class DatabaseClientTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-db", Guid.NewGuid().ToString("N"));

    public DatabaseClientTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A database with something in it to query.</summary>
    private string Sample()
    {
        var path = Path.Combine(_root, "sample.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT, Balance REAL);
            INSERT INTO Customers (Name, Balance) VALUES ('Rossi', 120.5);
            INSERT INTO Customers (Name, Balance) VALUES ('Bianchi', 0);
            INSERT INTO Customers (Name, Balance) VALUES ('Verdi', NULL);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER);
            """;
        command.ExecuteNonQuery();

        return path;
    }

    [Fact]
    public async Task ReturnsTheRowsAQuerySelects()
    {
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var result = await client.ExecuteAsync("SELECT Name, Balance FROM Customers ORDER BY Name");

        Assert.Equal(["Name", "Balance"], result.Columns.Select(c => c.Name));
        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("Bianchi", result.Rows[0][0]);
    }

    [Fact]
    public async Task ShowsNothingForANullRatherThanTheWordNull()
    {
        // A null and the text "NULL" are different values, and a grid that
        // wrote the same thing for both would be lying about the data.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var result = await client.ExecuteAsync("SELECT Balance FROM Customers WHERE Name = 'Verdi'");

        Assert.Null(result.Rows[0][0]);
    }

    [Fact]
    public async Task ListsTheTablesAndTheirColumns()
    {
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var tables = await client.GetTablesAsync();

        Assert.Equal(["Customers", "Orders"], tables.Select(t => t.Name));
        Assert.Equal(["Id", "Name", "Balance"], tables[0].Columns.Select(c => c.Name));
    }

    [Fact]
    public async Task LeavesOutTheTablesSqliteKeepsForItself()
    {
        // Not the user's data, and listing them buries what is.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        Assert.DoesNotContain(
            await client.GetTablesAsync(),
            t => t.Name.StartsWith("sqlite_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReportsACountForAStatementThatChangesRows()
    {
        // An UPDATE is an answer too, and a panel that only understood SELECT
        // would say nothing at all after one.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var result = await client.ExecuteAsync("UPDATE Customers SET Balance = 0 WHERE Balance IS NULL");

        Assert.True(result.IsCount);
        Assert.Equal(1, result.Affected);
    }

    [Fact]
    public async Task SaysWhatIsWrongWithSqlThatWillNotRun()
    {
        // While typing, a query is invalid far more often than not: the panel
        // has to survive it and say why.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var error = await Assert.ThrowsAsync<SqliteException>(
            () => client.ExecuteAsync("SELECT * FROM NoSuchTable"));

        Assert.Contains("NoSuchTable", error.Message);
    }

    [Fact]
    public async Task RefusesAPathWhereThereIsNoDatabase()
    {
        // Creating one because a path was mistyped leaves an empty database
        // where the user expected their data, and nothing to say so.
        await using var client = new DatabaseClient();

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => client.OpenAsync(Path.Combine(_root, "missing.db")));

        Assert.False(client.IsOpen);
    }

    [Fact]
    public async Task CanBeToldToStop()
    {
        // A query over a large table can take minutes, and an IDE that cannot
        // be interrupted is one that has hung.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.ExecuteAsync("SELECT * FROM Customers", cancelled.Token));
    }

    [Fact]
    public async Task ReportsHowLongItTook()
    {
        // The first thing anyone wants to know about a slow query.
        await using var client = new DatabaseClient();
        await client.OpenAsync(Sample());

        var result = await client.ExecuteAsync("SELECT * FROM Customers");

        Assert.True(result.Took > TimeSpan.Zero);
    }
}
