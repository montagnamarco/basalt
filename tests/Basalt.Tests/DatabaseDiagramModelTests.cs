using Basalt.Data.Sessions;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The diagram's model read from a real SQLite database with foreign keys, and the layout that places its boxes.
/// </summary>
public sealed class DatabaseDiagramModelTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-diagram-model-").FullName;
    private readonly List<IDatabaseSession> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions) session.DisposeAsync().AsTask().GetAwaiter().GetResult();

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private async Task<IDatabaseSession> OpenAsync(string script)
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db") }.ToString();

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = script;
            command.ExecuteNonQuery();
        }

        var session = await new SqliteProvider().OpenAsync(connectionString);
        _sessions.Add(session);

        return session;
    }

    private const string Shop = """
        CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, ManagerId INTEGER REFERENCES Customers (Id));
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers (Id), Amount REAL);
        CREATE TABLE OrderLines (OrderId INTEGER NOT NULL REFERENCES Orders (Id), Line INTEGER NOT NULL, Sku TEXT, PRIMARY KEY (OrderId, Line));
        CREATE TABLE Orphans (Id INTEGER PRIMARY KEY, GhostId INTEGER REFERENCES Ghost (Id));
        CREATE VIEW ActiveCustomers AS SELECT Id, Name FROM Customers;
        """;

    [Fact]
    public async Task TheModelHoldsTheTablesWithTheirColumnsAndKeys()
    {
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync(Shop));

        // A view has no box.
        Assert.Equal(["Customers", "OrderLines", "Orders", "Orphans"], model.Tables.Select(t => t.Name.Name));

        var orders = model.Tables.Single(t => t.Name.Name == "Orders");
        Assert.Equal(["Id", "CustomerId", "Amount"], orders.Columns.Select(c => c.Name));
        Assert.Equal([true, false, false], orders.Columns.Select(c => c.IsPrimaryKey));
        Assert.Equal([false, true, false], orders.Columns.Select(c => c.IsForeignKey));
        Assert.Equal("REAL", orders.Columns[2].DataType);

        // A composite key marks both of its columns; one of them is also a foreign key.
        var lines = model.Tables.Single(t => t.Name.Name == "OrderLines");
        Assert.Equal([true, true, false], lines.Columns.Select(c => c.IsPrimaryKey));
        Assert.True(lines.Columns[0].IsForeignKey);
    }

    [Fact]
    public async Task TheModelHoldsTheForeignKeysBetweenTheTables()
    {
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync(Shop));

        var summary = model.Relations
            .Select(r => $"{r.FromTable.Name}.{string.Join(",", r.FromColumns)}->{r.ToTable.Name}.{string.Join(",", r.ToColumns)}")
            .Order()
            .ToList();

        // Customers points at itself; the key to the missing table Ghost has nothing to be drawn to.
        Assert.Equal(
        [
            "Customers.ManagerId->Customers.Id",
            "OrderLines.OrderId->Orders.Id",
            "Orders.CustomerId->Customers.Id",
        ], summary);
    }

    [Fact]
    public async Task AnEmptyDatabaseHasAnEmptyModel()
    {
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync("CREATE VIEW V AS SELECT 1 AS x"));

        Assert.Empty(model.Tables);
        Assert.Empty(model.Relations);
        Assert.Empty(DiagramLayoutEngine.Auto(model));
    }

    [Fact]
    public async Task TheAutomaticLayoutPutsReferencedTablesLeftOfTheOnesThatPointAtThem()
    {
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync(Shop));
        var at = DiagramLayoutEngine.Auto(model);

        double X(string table) => at[model.Tables.Single(t => t.Name.Name == table).Name].X;

        Assert.True(X("Customers") < X("Orders"));
        Assert.True(X("Orders") < X("OrderLines"));
        Assert.Equal(model.Tables.Count, at.Count);
    }

    [Fact]
    public async Task TheAutomaticLayoutLeavesNoTwoBoxesOverlapping()
    {
        var script = string.Join("\n", Enumerable.Range(0, 40).Select(i =>
            $"CREATE TABLE T{i:00} (Id INTEGER PRIMARY KEY, A TEXT, B TEXT, C TEXT, D TEXT, E TEXT, F TEXT, G TEXT, H TEXT);"));

        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync(script));
        var at = DiagramLayoutEngine.Auto(model);

        var boxes = model.Tables
            .Select(t => (t.Name, X: at[t.Name].X, Y: at[t.Name].Y, H: DiagramGeometry.BoxHeight(t)))
            .ToList();

        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var a = boxes[i];
                var b = boxes[j];

                var apartX = a.X + DiagramGeometry.BoxWidth <= b.X || b.X + DiagramGeometry.BoxWidth <= a.X;
                var apartY = a.Y + a.H <= b.Y || b.Y + b.H <= a.Y;

                Assert.True(apartX || apartY, $"{a.Name} overlaps {b.Name}.");
            }
        }
    }

    [Fact]
    public async Task SavedPositionsWinAndANewTableGetsTheAutomaticOne()
    {
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync(Shop));

        var saved = new Dictionary<string, DiagramPoint>
        {
            ["Customers"] = new(500, 400),
            ["Gone"] = new(1, 1),
        };

        var merged = DiagramLayoutEngine.Merge(model, saved);
        var auto = DiagramLayoutEngine.Auto(model);

        Assert.Equal(new DiagramPoint(500, 400), merged[new DbObjectName(null, "Customers")]);
        Assert.Equal(auto[new DbObjectName(null, "Orders")], merged[new DbObjectName(null, "Orders")]);
        Assert.False(merged.ContainsKey(new DbObjectName(null, "Gone")));
    }

    [Fact]
    public async Task ATableWithManyColumnsIsCappedAndSaysHowManyAreHidden()
    {
        var columns = string.Join(", ", Enumerable.Range(0, 60).Select(i => $"C{i} TEXT"));
        var model = await DatabaseDiagramModel.ReadAsync(await OpenAsync($"CREATE TABLE Wide ({columns})"));
        var wide = model.Tables.Single();

        Assert.Equal(60, wide.Columns.Count);
        Assert.Equal(DiagramGeometry.MaxVisibleColumns + 1, DiagramGeometry.VisibleRows(wide));
        Assert.Equal(DiagramGeometry.MaxVisibleColumns, DiagramGeometry.RowOf(wide, "C59"));
    }
}
