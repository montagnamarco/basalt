using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The panel that shows a database.
/// </summary>
/// <remarks>
/// Checking what is actually in a table meant a second tool and a second
/// window. The panel is deliberately small — connect, look, query, read —
/// because that is what interrupts the work.
/// </remarks>
public sealed class DatabasePanelTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "basalt-dbpanel", Guid.NewGuid().ToString("N"));

    public DatabasePanelTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Sample()
    {
        var path = Path.Combine(_root, "sample.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT);
            INSERT INTO Customers (Name) VALUES ('Rossi'), ('Bianchi');
            """;
        command.ExecuteNonQuery();

        return path;
    }

    [AvaloniaFact]
    public void SaysWhatToDoBeforeAnythingIsOpen()
    {
        // An empty panel says neither "nothing open" nor "broken".
        using var panel = new DatabasePanel();

        Assert.Contains("Open a database", panel.StatusText);
    }

    [AvaloniaFact]
    public async Task ListsTheTablesOnceOpened()
    {
        using var panel = new DatabasePanel();

        await panel.OpenAsync(Sample());

        Assert.Equal(["Customers"], panel.TableNames);
    }

    [AvaloniaFact]
    public async Task StaysUsableWhenTheFileIsNotADatabase()
    {
        // A file picked by mistake must not leave the panel broken.
        var notADatabase = Path.Combine(_root, "notes.txt");
        await File.WriteAllTextAsync(notADatabase, "just some text");

        using var panel = new DatabasePanel();

        await panel.OpenAsync(notADatabase);

        Assert.Null(panel.Source);
        Assert.NotEmpty(panel.StatusText);
    }

    [AvaloniaFact]
    public async Task SaysWhereTheFileWentWhenThereIsNone()
    {
        using var panel = new DatabasePanel();

        await panel.OpenAsync(Path.Combine(_root, "missing.db"));

        Assert.Null(panel.Source);
    }

    [AvaloniaFact]
    public async Task RemembersWhatItIsConnectedTo()
    {
        using var panel = new DatabasePanel();

        var path = Sample();
        await panel.OpenAsync(path);

        Assert.Equal(path, panel.Source);
    }
}
