using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
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
/// The query history: the last 50 queries run on each connection, kept in a file beside the settings, and
/// offered by the History button of a query tab.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseHistoryTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-history-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-history-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public DatabaseHistoryTests() => Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
        try { File.Delete(QueryHistory.PathBeside(_settings)); } catch (IOException) { }
    }

    private string HistoryFile => Path.Combine(_root, "history.json");

    [Fact]
    public async Task KeepsTheLastFiftyNewestFirst()
    {
        var history = new QueryHistory(HistoryFile);

        for (var i = 1; i <= 60; i++) await history.AddAsync("c1", $"SELECT {i}");

        var kept = await history.GetAsync("c1");

        Assert.Equal(QueryHistory.Capacity, kept.Count);
        Assert.Equal("SELECT 60", kept[0]);
        Assert.Equal("SELECT 11", kept[^1]);
    }

    [Fact]
    public async Task RunningTheSameQueryAgainMovesItToTheTopInsteadOfListingItTwice()
    {
        var history = new QueryHistory(HistoryFile);

        await history.AddAsync("c1", "SELECT 1");
        await history.AddAsync("c1", "SELECT 2");
        await history.AddAsync("c1", "  SELECT 1  ");

        Assert.Equal(["SELECT 1", "SELECT 2"], await history.GetAsync("c1"));
    }

    [Fact]
    public async Task EachConnectionHasItsOwnListAndItSurvivesARestart()
    {
        var first = new QueryHistory(HistoryFile);

        await first.AddAsync("c1", "SELECT 1");
        await first.AddAsync("c2", "SELECT 2");

        // A second instance reads the file, as the IDE does on its next start.
        var second = new QueryHistory(HistoryFile);

        Assert.Equal(["SELECT 1"], await second.GetAsync("c1"));
        Assert.Equal(["SELECT 2"], await second.GetAsync("c2"));
        Assert.Empty(await second.GetAsync("c3"));

        await second.ForgetAsync("c1");

        Assert.Empty(await new QueryHistory(HistoryFile).GetAsync("c1"));
        Assert.Equal(["SELECT 2"], await new QueryHistory(HistoryFile).GetAsync("c2"));
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsAnEmptyHistoryNotAnError()
    {
        await File.WriteAllTextAsync(HistoryFile, "{ this is not json");

        var history = new QueryHistory(HistoryFile);

        Assert.Empty(await history.GetAsync("c1"));

        await history.AddAsync("c1", "SELECT 1");

        Assert.Equal(["SELECT 1"], await new QueryHistory(HistoryFile).GetAsync("c1"));
    }

    [Fact]
    public async Task TwoRunsFinishingTogetherBothAreKept()
    {
        var history = new QueryHistory(HistoryFile);

        await Task.WhenAll(Enumerable.Range(1, 20).Select(i => history.AddAsync("c1", $"SELECT {i}")));

        Assert.Equal(20, (await new QueryHistory(HistoryFile).GetAsync("c1")).Count);
    }

    [Fact]
    public async Task TwoWindowsOnTheSameFileKeepBothEntries()
    {
        // Two instances, as two IDE windows have: neither may overwrite what the other wrote.
        var one = new QueryHistory(HistoryFile);
        var two = new QueryHistory(HistoryFile);

        await one.AddAsync("c1", "SELECT 'from one'");
        await two.AddAsync("c1", "SELECT 'from two'");
        await one.AddAsync("c1", "SELECT 'one again'");

        Assert.Equal(["SELECT 'one again'", "SELECT 'from two'", "SELECT 'from one'"], await new QueryHistory(HistoryFile).GetAsync("c1"));
        Assert.Equal(await one.GetAsync("c1"), await two.GetAsync("c1"));
    }

    [Fact]
    public async Task WritesGoThroughAUniqueTemporaryNameAndLeaveNoneBehind()
    {
        var history = new QueryHistory(HistoryFile);

        await Task.WhenAll(Enumerable.Range(1, 10).Select(i => new QueryHistory(HistoryFile).AddAsync("c1", $"SELECT {i}")));
        await history.AddAsync("c1", "SELECT 99");

        Assert.Equal([HistoryFile], Directory.GetFiles(_root));
        Assert.Equal(11, (await history.GetAsync("c1")).Count);
    }

    [Fact]
    public async Task AStaleTemporaryFileOfAnotherWriterDoesNotStopTheHistoryBeingSaved()
    {
        // Another process (or a crashed one) left a file where a fixed temporary name would have to go.
        Directory.CreateDirectory(HistoryFile + ".tmp");

        await new QueryHistory(HistoryFile).AddAsync("c1", "SELECT 1");

        Assert.Equal(["SELECT 1"], await new QueryHistory(HistoryFile).GetAsync("c1"));
    }

    [Fact]
    public async Task AQueryOverTheLengthCapIsNotRecordedAndTheFileStaysUnderItsCap()
    {
        var history = new QueryHistory(HistoryFile);

        await history.AddAsync("c1", "SELECT '" + new string('x', QueryHistory.MaximumQueryLength) + "'");
        Assert.Empty(await history.GetAsync("c1"));

        // Under the cap per query, but many connections each full of long queries: the file is kept to its cap.
        var long64k = "SELECT '" + new string('y', QueryHistory.MaximumQueryLength - 100) + "' -- ";

        for (var connection = 0; connection < 3; connection++)
            for (var i = 0; i < QueryHistory.Capacity; i++)
                await history.AddAsync("c" + connection, long64k + i);

        Assert.True(new FileInfo(HistoryFile).Length <= 2 * 1024 * 1024, "The history file grew past its cap.");
        Assert.NotEmpty(await history.GetAsync("c0"));
    }

    [Theory]
    [InlineData("CREATE LOGIN app WITH PASSWORD = 'hunter2'")]
    [InlineData("alter login app with password = 'x'")]
    [InlineData("CREATE USER bob IDENTIFIED BY 'secret'")]
    [InlineData("ALTER USER bob PASSWORD 'secret'")]
    [InlineData("ALTER ROLE r WITH PASSWORD 'secret'")]
    [InlineData("-- set up\n  /* x */ CREATE USER bob")]
    [InlineData("SELECT 1;\nCREATE LOGIN a WITH PASSWORD='p'")]
    public async Task StatementsThatSetACredentialAreNotRecorded(string sql)
    {
        var history = new QueryHistory(HistoryFile);

        await history.AddAsync("c1", sql);

        Assert.Empty(await history.GetAsync("c1"));
        Assert.False(QueryHistory.IsRecordable(sql));
    }

    [Theory]
    [InlineData("SELECT * FROM Users WHERE Name = 'create user'")]
    [InlineData("ALTER TABLE Roles ADD Password text")]
    public async Task OrdinaryStatementsThatMentionTheWordsAreRecorded(string sql)
    {
        var history = new QueryHistory(HistoryFile);

        await history.AddAsync("c1", sql);

        Assert.Equal([sql], await history.GetAsync("c1"));
    }

    [Fact]
    public async Task AQueryTheEncoderRefusesIsDroppedAndLaterSavesStillWork()
    {
        var history = new QueryHistory(HistoryFile);

        // A lone surrogate cannot be written as UTF-8 JSON; it must not fail this save or every one after it.
        await history.AddAsync("c1", "SELECT '\ud800'");
        await history.AddAsync("c1", "SELECT 1");

        Assert.Equal(["SELECT 1"], await new QueryHistory(HistoryFile).GetAsync("c1"));

        // An old file that holds one (written by something else) cannot be read at all; the history starts again
        // and the next save works, instead of failing for ever on text that is still in the file.
        await File.WriteAllTextAsync(HistoryFile, "{\"c1\":[\"SELECT 2\",\"bad \\ud800\"]}");

        await history.AddAsync("c1", "SELECT 3");

        Assert.Equal(["SELECT 3"], await new QueryHistory(HistoryFile).GetAsync("c1"));
    }

    [Fact]
    public async Task ARunStillBeingRecordedWhenItsConnectionIsRemovedLeavesNoOrphanEntry()
    {
        var history = new QueryHistory(HistoryFile);

        await history.AddAsync("gone", "SELECT 1");
        await history.ForgetAsync("gone");

        // The late write of a run that began before the removal.
        await history.AddAsync("gone", "SELECT 2");

        Assert.Empty(await history.GetAsync("gone"));
        Assert.DoesNotContain("gone", await File.ReadAllTextAsync(HistoryFile));
    }

    [Fact]
    public void TheFileSitsBesideTheSettingsFile()
    {
        Assert.Equal("/home/u/.basalt/database-history.json", QueryHistory.PathBeside("/home/u/.basalt/settings.json"));
        Assert.Equal("/tmp/x.database-history.json", QueryHistory.PathBeside("/tmp/x.json"));
    }

    [AvaloniaFact]
    public async Task RunningQueriesFillsTheHistoryAndChoosingOneBringsItBackIntoTheEditor()
    {
        using var shell = new Shell();
        var connectionId = await AddShopAsync(shell);

        var query = await OpenQueryAsync(shell, connectionId);

        foreach (var sql in new[] { "SELECT 1", "SELECT 2 AS Two", "SELECT 3" })
        {
            query.Sql = sql;
            await query.RunAsync();
        }

        // Newest first, as the button lists them.
        await query.ShowHistoryAsync();

        var items = query.HistoryMenu!.Items.OfType<MenuItem>().ToList();

        Assert.Equal(["SELECT 3", "SELECT 2 AS Two", "SELECT 1"], items.Select(i => (string)i.Header!).ToArray());

        // Choosing one puts its text in the editor, ready to run again.
        query.Sql = "";
        items[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

        Assert.Equal("SELECT 2 AS Two", query.Sql);

        // The history is in the file, so the next session has it.
        var onDisk = await new QueryHistory(QueryHistory.PathBeside(_settings)).GetAsync(connectionId);
        Assert.Equal(["SELECT 3", "SELECT 2 AS Two", "SELECT 1"], onDisk);
    }

    [AvaloniaFact]
    public async Task AFailingQueryIsRememberedToo_AndAnEmptyHistoryIsSaidSo()
    {
        using var shell = new Shell();
        var connectionId = await AddShopAsync(shell);

        var query = await OpenQueryAsync(shell, connectionId);

        await query.ShowHistoryAsync();
        Assert.Equal(L(StringKeys.DbHistoryEmpty), (string)query.HistoryMenu!.Items.OfType<MenuItem>().Single().Header!);
        Assert.False(query.HistoryMenu!.Items.OfType<MenuItem>().Single().IsEnabled);

        query.Sql = "SELEKT nothing";
        await query.RunAsync();

        await query.ShowHistoryAsync();
        Assert.Equal("SELEKT nothing", (string)query.HistoryMenu!.Items.OfType<MenuItem>().Single().Header!);
    }

    [AvaloniaFact]
    public async Task TheHistoryButtonIsInTheQueryToolbarAndListsTheQueriesOfTheChosenConnectionOnly()
    {
        using var shell = new Shell();
        var first = await AddShopAsync(shell);
        var second = (await shell.Workspace.AddAsync(Draft("Other", Path.Combine(_root, "other.db")))).Id;

        await shell.Workspace.History.AddAsync(first, "SELECT 'first'");
        await shell.Workspace.History.AddAsync(second, "SELECT 'second'");

        var query = await OpenQueryAsync(shell, second);

        var button = query.GetVisualDescendantsOf<Button>().Single(b => b.Name == "QueryHistory");
        Assert.NotNull(button);

        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(await WaitUntilAsync(shell, () => query.HistoryMenu is not null), "The button listed nothing.");
        Assert.Equal(["SELECT 'second'"], query.HistoryMenu!.Items.OfType<MenuItem>().Select(i => (string)i.Header!).ToArray());
    }

    [AvaloniaFact]
    public async Task RemovingAConnectionDropsItsHistory()
    {
        using var shell = new Shell();
        var connectionId = await AddShopAsync(shell);

        await shell.Workspace.History.AddAsync(connectionId, "SELECT 1");
        await shell.Workspace.RemoveAsync(connectionId);

        Assert.Empty(await new QueryHistory(QueryHistory.PathBeside(_settings)).GetAsync(connectionId));
    }

    // ------------------------------------------------------------------

    private static string L(string key) => Localizer.Get(key);

    private static DatabaseDraft Draft(string name, string path) =>
        new(name, DatabaseKind.Sqlite, path, "", "", "", "", "", false, false, false, "Prefer", "");

    private async Task<string> AddShopAsync(Shell shell)
    {
        var path = Path.Combine(_root, "shop.db");

        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE T (Id INTEGER PRIMARY KEY);";
            command.ExecuteNonQuery();
        }

        await shell.SettleAsync(5);

        var saved = await shell.Workspace.AddAsync(Draft("Shop", path));

        await WaitUntilAsync(shell, () => shell.Workspace.Connections.Any(c => c.Id == saved.Id));

        return saved.Id;
    }

    private static async Task<QueryView> OpenQueryAsync(Shell shell, string connectionId)
    {
        // New Query from the connection's context menu, as the user opens one.
        await WaitUntilAsync(shell, () => shell.Explorer.Roots.OfType<ConnectionNode>().Any(c => c.ConnectionId == connectionId));

        var node = shell.Explorer.Roots.OfType<ConnectionNode>().Single(c => c.ConnectionId == connectionId);

        shell.Explorer.MenuActionsForTests(node).Single(a => a.Header == L(StringKeys.DbNewQuery)).Invoke();
        await shell.SettleAsync(3);

        return shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>().Last();
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
                throw new InvalidOperationException("The history tests do not call the assistant.")));

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

internal static class VisualQueries
{
    /// <summary>The descendants of a control of one type.</summary>
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual root) where T : class =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
