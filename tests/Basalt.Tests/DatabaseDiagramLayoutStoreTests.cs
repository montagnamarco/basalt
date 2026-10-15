using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Database;

namespace Basalt.Tests;

/// <summary>Where the diagram's layout is kept: beside the settings file, per connection, and surviving what a crash or a hand edit leaves.</summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseDiagramLayoutStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-diagram-layout-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private DiagramLayout Layout(double x, double zoom = 1) =>
        new(new Dictionary<string, DiagramPoint> { ["Customers"] = new(x, 20), ["dbo.Orders"] = new(300, 40.5) }, zoom, 12, -7);

    [Fact]
    public async Task ALayoutIsKeptAndReadBackByAnotherStore()
    {
        var path = Path.Combine(_root, "layouts.json");

        await new DiagramLayoutStore(path).SaveAsync("shop", Layout(10, zoom: 1.5));

        var read = await new DiagramLayoutStore(path).GetAsync("shop");

        Assert.NotNull(read);
        Assert.Equal(new DiagramPoint(10, 20), read.Tables["Customers"]);
        Assert.Equal(new DiagramPoint(300, 40.5), read.Tables["dbo.Orders"]);
        Assert.Equal(1.5, read.Zoom);
        Assert.Equal(12, read.PanX);
        Assert.Equal(-7, read.PanY);
    }

    [Fact]
    public async Task ConnectionsKeepTheirOwnLayoutAndForgettingOneLeavesTheOthers()
    {
        var store = new DiagramLayoutStore(Path.Combine(_root, "layouts.json"));

        await store.SaveAsync("a", Layout(1));
        await store.SaveAsync("b", Layout(2));
        await store.SaveAsync("a", Layout(3));

        Assert.Equal(3, (await store.GetAsync("a"))!.Tables["Customers"].X);
        Assert.Equal(2, (await store.GetAsync("b"))!.Tables["Customers"].X);
        Assert.Null(await store.GetAsync("c"));

        await store.ForgetAsync("a");

        Assert.Null(await store.GetAsync("a"));
        Assert.NotNull(await store.GetAsync("b"));
    }

    [Fact]
    public async Task AFileThatIsNotJsonIsKeptAsideAndTheNextSaveStartsANewOne()
    {
        var path = Path.Combine(_root, "layouts.json");
        await File.WriteAllTextAsync(path, "{ not json, but it held other connections");

        var store = new DiagramLayoutStore(path);

        Assert.Null(await store.GetAsync("a"));

        await store.SaveAsync("a", Layout(5));

        Assert.Equal(5, (await store.GetAsync("a"))!.Tables["Customers"].X);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp"));

        // What was there is not destroyed: it is beside the new file.
        Assert.Equal("{ not json, but it held other connections", await File.ReadAllTextAsync(path + ".bak"));
    }

    [Fact]
    public async Task AFileWithAnEntryOfTheWrongShapeIsKeptAsideWholeNotSilentlyOverwritten()
    {
        var path = Path.Combine(_root, "layouts.json");
        var store = new DiagramLayoutStore(path);

        await store.SaveAsync("other", Layout(9));
        var good = await File.ReadAllTextAsync(path);

        await File.WriteAllTextAsync(path, good.Replace("\"Tables\"", "\"Tables\": 7, \"Unused\""));

        await store.SaveAsync("a", Layout(1));

        // The other connection's layout is in the file kept aside, and the old .bak is never replaced by a later bad file.
        Assert.Contains("\"other\"", await File.ReadAllTextAsync(path + ".bak"));

        await File.WriteAllTextAsync(path, "garbage");
        await store.SaveAsync("b", Layout(2));

        Assert.Contains("\"other\"", await File.ReadAllTextAsync(path + ".bak"));
        Assert.Equal(2, Directory.GetFiles(_root, "layouts.json*.bak").Length);
    }

    [Fact]
    public async Task AFileThatCannotBeReadIsLeftAloneAndTheSaveIsSkipped()
    {
        if (!OperatingSystem.IsWindows()) return; // Elsewhere file locks are advisory: a locked file can still be read.

        var path = Path.Combine(_root, "layouts.json");
        var store = new DiagramLayoutStore(path);

        await store.SaveAsync("other", Layout(9));
        var before = await File.ReadAllTextAsync(path);

        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await store.SaveAsync("a", Layout(1));

        Assert.Equal(before, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public void TheFileSitsBesideTheSettingsFileAndFollowsBasaltSettings()
    {
        Assert.Equal(
            Path.Combine(_root, "database-diagrams.json"),
            DiagramLayoutStore.PathBeside(Path.Combine(_root, "settings.json")));

        Assert.Equal(
            Path.Combine(_root, "custom.database-diagrams.json"),
            DiagramLayoutStore.PathBeside(Path.Combine(_root, "custom.json")));

        // With BASALT_SETTINGS set the settings file is that path, and so the layout file is beside it.
        var previous = Environment.GetEnvironmentVariable("BASALT_SETTINGS");
        var custom = Path.Combine(_root, "elsewhere", "mine.json");

        try
        {
            Environment.SetEnvironmentVariable("BASALT_SETTINGS", custom);

            Assert.Equal(
                Path.Combine(_root, "elsewhere", "mine.database-diagrams.json"),
                DiagramLayoutStore.PathBeside(new SettingsStore().FilePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("BASALT_SETTINGS", previous);
        }
    }

    [Fact]
    public async Task ASaveCreatesTheFolderAndNeverThrowsWhenTheFileCannotBeWritten()
    {
        var nested = Path.Combine(_root, "a", "b", "layouts.json");

        await new DiagramLayoutStore(nested).SaveAsync("x", Layout(1));
        Assert.True(File.Exists(nested));

        // A directory where the file should be: the write fails, and the diagram is not told.
        var blocked = Path.Combine(_root, "blocked.json");
        Directory.CreateDirectory(blocked);

        await new DiagramLayoutStore(blocked).SaveAsync("x", Layout(1));
    }
}
