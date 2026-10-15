using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
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
/// The database diagram as a user meets it: the boxes and lines rasterised and read back as pixels, the pointer
/// moving and zooming them, the layout kept per connection, and a double-click opening the table designer.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseDiagramViewTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-diagram-view-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-diagram-view-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    public DatabaseDiagramViewTests() => Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
        try { File.Delete(DiagramLayoutStore.PathBeside(_settings)); } catch (IOException) { }
        try { File.Delete(QueryHistory.PathBeside(_settings)); } catch (IOException) { }
    }

    private static readonly DbObjectName Customers = new(null, "Customers");
    private static readonly DbObjectName Orders = new(null, "Orders");

    // ------------------------------------------------------------------
    // Pixels
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(0.6)]
    public async Task ARelationLineIsDrawnInTheGapBetweenTheTwoBoxesItJoins(double zoom)
    {
        using var host = await HostAsync();
        host.Canvas.SetView(zoom, new Vector(DiagramCanvas.Margin, DiagramCanvas.Margin));
        await host.SettleAsync();

        var raster = Raster.Of(host.Canvas);
        var relation = host.View.Model.Relations.Single(r => r.FromTable == Orders && r.ToTable == Customers);
        var line = host.Canvas.RelationLine(relation);

        var customers = host.Canvas.BoxRect(Customers);
        var orders = host.Canvas.BoxRect(Orders);

        // The referencing table is to the right: the line leaves its left edge, crosses the gap, and enters the other box.
        Assert.True(customers.Right < orders.X, "The layout should put Customers left of Orders.");

        var start = line[0];
        var elbow = line[1];

        Assert.Equal(orders.X, start.X, 0.5);

        // The middle of the first stretch lies in the gap between the boxes, outside both.
        var probe = new Point((start.X + elbow.X) / 2, start.Y);

        Assert.True(probe.X > customers.Right && probe.X < orders.X, "The stretch should lie between the boxes.");
        Assert.False(customers.Contains(probe) || orders.Contains(probe));

        var lineColour = host.Canvas.LineColour;
        var background = host.Canvas.BackgroundColour;

        Assert.True(HasLineNear(raster, probe, lineColour, background), "No pixel of the line's colour where the line should be.");

        // Not a filled region: well above the line it is the background again.
        var above = raster.At((int)Math.Round(probe.X), (int)Math.Round(probe.Y) - 12);
        Assert.True(Raster.Near(above, background, 4), $"Expected the background above the line but found {above}.");

        // The line joins the right edge of Customers at the height of the referenced column.
        var end = line[^1];
        Assert.Equal(customers.Right, end.X, 0.5);
        Assert.True(HasLineNear(raster, new Point(end.X + 6, end.Y), lineColour, background), "The line does not reach the box it joins.");
    }

    [AvaloniaFact]
    public async Task ABoxShowsItsTitleItsColumnsAndTheirKeyMarks()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var raster = Raster.Of(host.Canvas);
        var box = host.Canvas.BoxRect(Orders);
        var header = host.Canvas.HeaderColour;

        // The title bar is filled with the header colour, and the title is drawn in it.
        var titleBar = new PixelRect((int)box.X + 3, (int)box.Y + 3, (int)box.Width - 6, (int)DiagramGeometry.HeaderHeight - 6);

        Assert.True(Raster.Near(raster.At(titleBar.Right - 4, titleBar.Y + 3), header, 4), "The title bar is not the header colour.");
        Assert.True(raster.BestContrastAgainst(titleBar, header) >= 3.0, "No title text in the title bar.");

        // A box with its title removed shows only the bar: the same area of another box's body, below its columns, is empty.
        var empty = new PixelRect((int)box.X + 3, (int)(box.Bottom - 5), (int)box.Width - 6, 3);
        var body = raster.At(empty.X + 2, empty.Y + 1);
        Assert.True(raster.BestContrastAgainst(empty, body) < 1.2);

        // The first column row: a mark to the left (PK) and the name beside it.
        var firstRow = new PixelRect((int)box.X + 28, (int)(box.Y + DiagramGeometry.HeaderHeight + 2), 90, (int)DiagramGeometry.RowHeight - 4);
        var rowBackground = raster.At(firstRow.X - 2, firstRow.Y);
        Assert.True(raster.BestContrastAgainst(firstRow, rowBackground) >= 3.0, "The column name is not drawn.");

        var mark = new PixelRect((int)box.X + 5, (int)(box.Y + DiagramGeometry.HeaderHeight + 2), 20, (int)DiagramGeometry.RowHeight - 4);
        Assert.True(raster.BestContrastAgainst(mark, rowBackground) >= 1.5, "The primary key mark is not drawn.");

        // Amount has no key: nothing in the mark column of its row.
        var amountRow = new PixelRect((int)box.X + 5, (int)(box.Y + DiagramGeometry.HeaderHeight + 2 * DiagramGeometry.RowHeight + 3), 20, (int)DiagramGeometry.RowHeight - 6);
        Assert.True(raster.BestContrastAgainst(amountRow, rowBackground) < 1.2, "A column without a key carries a mark.");
    }

    // ------------------------------------------------------------------
    // Pointer
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task DraggingTheBackgroundPansAndDraggingABoxMovesOnlyThatBox()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var customers = host.Canvas.BoxRect(Customers);
        var orders = host.Canvas.BoxRect(Orders);
        var pan = host.Canvas.Pan;

        // Background: below everything, in the gap right of the last column.
        var empty = new Point(host.Canvas.Bounds.Width - 20, host.Canvas.Bounds.Height - 20);
        Assert.Null(host.Canvas.TableAt(empty));

        host.Drag(empty, new Point(empty.X - 60, empty.Y - 30));

        Assert.Equal(pan.X - 60, host.Canvas.Pan.X, 0.5);
        Assert.Equal(pan.Y - 30, host.Canvas.Pan.Y, 0.5);
        Assert.Equal(customers.X - 60, host.Canvas.BoxRect(Customers).X, 0.5);
        Assert.Equal(orders.Y - 30, host.Canvas.BoxRect(Orders).Y, 0.5);

        // A box: its header is held and moved; the other box stays where it was.
        var before = host.Canvas.BoxRect(Orders);
        var other = host.Canvas.BoxRect(Customers);
        var grab = new Point(before.X + 40, before.Y + 8);

        host.Drag(grab, new Point(grab.X + 25, grab.Y + 70));

        var after = host.Canvas.BoxRect(Orders);
        Assert.Equal(before.X + 25, after.X, 0.5);
        Assert.Equal(before.Y + 70, after.Y, 0.5);
        Assert.Equal(other, host.Canvas.BoxRect(Customers));
        Assert.Equal(Orders, host.Canvas.DrawOrder[^1].Name);
    }

    [AvaloniaFact]
    public async Task CtrlWheelZoomsAroundThePointerAndTheWheelAloneDoesNot()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var at = new Point(300, 200);
        var worldBefore = new Point((at.X - host.Canvas.Pan.X) / host.Canvas.Zoom, (at.Y - host.Canvas.Pan.Y) / host.Canvas.Zoom);

        host.Wheel(at, new Vector(0, 1), RawInputModifiers.Control);

        Assert.True(host.Canvas.Zoom > 1.1, $"Zoom is {host.Canvas.Zoom}.");

        // The diagram point that was under the pointer still is.
        var worldAfter = new Point((at.X - host.Canvas.Pan.X) / host.Canvas.Zoom, (at.Y - host.Canvas.Pan.Y) / host.Canvas.Zoom);
        Assert.Equal(worldBefore.X, worldAfter.X, 0.01);
        Assert.Equal(worldBefore.Y, worldAfter.Y, 0.01);

        var zoomed = host.Canvas.Zoom;
        host.Wheel(at, new Vector(0, 1), RawInputModifiers.None);
        Assert.Equal(zoomed, host.Canvas.Zoom);

        for (var i = 0; i < 40; i++) host.Wheel(at, new Vector(0, -1), RawInputModifiers.Control);
        Assert.True(host.Canvas.Zoom >= 0.25 - 1e-9, "The zoom went below its floor.");
    }

    [AvaloniaFact]
    public async Task ADoubleClickOnABoxAsksForItsTableDesigner()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var asked = new List<DbObjectName>();
        host.View.TableDesignRequested += (_, name) => asked.Add(name);

        var box = host.Canvas.BoxRect(Customers);
        var inside = new Point(box.X + 50, box.Y + 40);

        host.Click(inside);
        Assert.Empty(asked);

        host.Click(inside);
        host.Click(inside);

        Assert.Contains(Customers, asked);

        // A double-click on the background asks for nothing.
        asked.Clear();
        var empty = new Point(host.Canvas.Bounds.Width - 20, host.Canvas.Bounds.Height - 20);
        host.Click(empty);
        host.Click(empty);
        Assert.Empty(asked);
    }

    // ------------------------------------------------------------------
    // Layout kept per connection
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task ADraggedBoxAndTheZoomAreKeptAndComeBackWhenTheDiagramIsOpenedAgain()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var box = host.Canvas.BoxRect(Orders);
        var grab = new Point(box.X + 40, box.Y + 8);

        host.Drag(grab, new Point(grab.X + 40, grab.Y + 90));
        host.Wheel(new Point(200, 200), new Vector(0, 1), RawInputModifiers.Control);

        var moved = host.Canvas.BoxRect(Orders);
        var zoom = host.Canvas.Zoom;

        await host.View.SaveLayoutAsync();

        // The file is beside the settings file and holds this connection's boxes.
        var path = DiagramLayoutStore.PathBeside(_settings);
        Assert.True(File.Exists(path));
        Assert.Contains("Orders", await File.ReadAllTextAsync(path));

        using var again = await HostAsync(host.ConnectionId);
        await again.SettleAsync();

        Assert.Equal(zoom, again.Canvas.Zoom, 1e-6);
        Assert.Equal(moved.X, again.Canvas.BoxRect(Orders).X, 0.01);
        Assert.Equal(moved.Y, again.Canvas.BoxRect(Orders).Y, 0.01);

        // Another connection's diagram does not take it.
        var layout = await new DiagramLayoutStore(path).GetAsync("someone-else");
        Assert.Null(layout);
    }

    [AvaloniaFact]
    public async Task ResetLayoutPutsTheBoxesBackWhereTheAutomaticLayoutPutsThem()
    {
        using var host = await HostAsync();
        await host.SettleAsync();

        var original = host.Canvas.BoxRect(Orders);
        var grab = new Point(original.X + 40, original.Y + 8);

        host.Drag(grab, new Point(grab.X + 100, grab.Y + 100));
        Assert.NotEqual(original, host.Canvas.BoxRect(Orders));

        host.View.ResetLayout();
        await host.SettleAsync(2);

        Assert.Equal(original, host.Canvas.BoxRect(Orders));

        // And the reset is what is kept.
        await host.View.SaveLayoutAsync();
        var saved = await new DiagramLayoutStore(DiagramLayoutStore.PathBeside(_settings)).GetAsync(host.ConnectionId);
        Assert.Equal(DiagramLayoutEngine.Auto(host.View.Model)[Orders].X, saved!.Tables["Orders"].X, 0.01);
    }

    // ------------------------------------------------------------------
    // Through the window
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheConnectionsMenuOpensTheDiagramAndADoubleClickOnABoxOpensTheDesigner()
    {
        using var shell = new Shell();
        var draft = new DatabaseDraft("Shop", DatabaseKind.Sqlite, CreateShop(), "", "", "", "", "", false, false, false, "Prefer", "");
        var saved = await shell.Workspace.AddAsync(draft);

        Assert.True(await shell.WaitUntilAsync(() => shell.Explorer.Roots.OfType<ConnectionNode>().Any(c => c.ConnectionId == saved.Id)));

        var connection = shell.Explorer.Roots.OfType<ConnectionNode>().Single();
        var action = shell.Explorer.MenuActionsForTests(connection).Single(a => a.Header == Localizer.Get(StringKeys.DbDiagram));

        Assert.True(action.IsAvailable!());
        action.Invoke();

        Assert.True(await shell.WaitUntilAsync(() => shell.Window.DatabaseDocumentsForTests.Any(d => d.View is DatabaseDiagramView { Model.Tables.Count: > 0 })),
            "The diagram never loaded.");

        var document = shell.Window.DatabaseDocumentsForTests.Single(d => d.View is DatabaseDiagramView);
        var view = (DatabaseDiagramView)document.View;

        Assert.Equal(Localizer.Get(StringKeys.DiagramTitle, "Shop"), document.Title);

        // Opening it again shows the tab it has.
        action.Invoke();
        await shell.SettleAsync(3);
        Assert.Single(shell.Window.DatabaseDocumentsForTests, d => d.View is DatabaseDiagramView);

        await shell.SettleAsync(4);

        var box = view.Canvas.BoxRect(Customers);
        var inWindow = view.Canvas.TranslatePoint(new Point(box.X + 50, box.Y + 40), shell.Window)!.Value;

        for (var i = 0; i < 2; i++)
        {
            shell.Window.MouseDown(inWindow, MouseButton.Left);
            shell.Window.MouseUp(inWindow, MouseButton.Left);
        }

        Assert.True(await shell.WaitUntilAsync(() => shell.Window.DatabaseDocumentsForTests.Any(d => d.View is TableDesignerView { Rows.Count: > 0 })),
            "The double-click did not open the table designer.");

        var designer = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<TableDesignerView>().Single();
        Assert.Equal(Customers, designer.Table);
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private string CreateShop()
    {
        var path = Path.Combine(_root, "shop-" + Guid.NewGuid().ToString("N") + ".db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY, CustomerId INTEGER NOT NULL REFERENCES Customers (Id), Amount REAL);
            CREATE TABLE OrderLines (OrderId INTEGER NOT NULL REFERENCES Orders (Id), Line INTEGER NOT NULL, Sku TEXT, PRIMARY KEY (OrderId, Line));
            """;
        command.ExecuteNonQuery();

        return path;
    }

    /// <summary>
    /// Whether some pixel near a point is the line's colour. A line one and a half pixels wide covers a pixel only
    /// in part, so a pixel counts when it is much nearer the line's colour than the background is.
    /// </summary>
    private static bool HasLineNear(Raster raster, Point at, Color line, Color background)
    {
        static double Distance(Color a, Color b) =>
            Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));

        var limit = 0.4 * Distance(line, background);

        for (var dy = -3; dy <= 3; dy++)
            for (var dx = -3; dx <= 3; dx++)
                if (Distance(raster.At((int)Math.Round(at.X) + dx, (int)Math.Round(at.Y) + dy), line) < limit) return true;

        return false;
    }

    private string? _shared;
    private DatabaseWorkspace? _workspace;
    private readonly List<Host> _hosts = [];

    private async Task<Host> HostAsync(string? existingConnectionId = null)
    {
        _workspace ??= new DatabaseWorkspace(new SettingsStore(_settings), new MemoryDatabasePasswordStore());

        if (existingConnectionId is null)
        {
            _shared = CreateShop();
            var saved = await _workspace.AddAsync(
                new DatabaseDraft("Shop", DatabaseKind.Sqlite, _shared, "", "", "", "", "", false, false, false, "Prefer", ""));
            existingConnectionId = saved.Id;
        }

        var view = new DatabaseDiagramView(_workspace, existingConnectionId, new DiagramLayoutStore(DiagramLayoutStore.PathBeside(_settings)));

        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();

        await view.LoadAsync();

        var host = new Host(window, view, existingConnectionId);
        _hosts.Add(host);

        return host;
    }

    /// <summary>A diagram in a window of its own, with the pointer driven the way a user would.</summary>
    private sealed class Host(Window window, DatabaseDiagramView view, string connectionId) : IDisposable
    {
        public DatabaseDiagramView View => view;

        public DiagramCanvas Canvas => view.Canvas;

        public string ConnectionId => connectionId;

        public async Task SettleAsync(int passes = 3)
        {
            for (var i = 0; i < passes; i++)
            {
                window.UpdateLayout();
                await Task.Delay(30);
            }

            window.UpdateLayout();
        }

        private Point InWindow(Point onCanvas) => Canvas.TranslatePoint(onCanvas, window)!.Value;

        public void Drag(Point from, Point to)
        {
            window.MouseDown(InWindow(from), MouseButton.Left);
            window.MouseMove(InWindow(new Point((from.X + to.X) / 2, (from.Y + to.Y) / 2)));
            window.MouseMove(InWindow(to));
            window.MouseUp(InWindow(to), MouseButton.Left);
        }

        public void Click(Point at)
        {
            window.MouseDown(InWindow(at), MouseButton.Left);
            window.MouseUp(InWindow(at), MouseButton.Left);
        }

        public void Wheel(Point at, Vector delta, RawInputModifiers modifiers) =>
            window.MouseWheel(InWindow(at), delta, modifiers);

        public void Dispose()
        {
            View.Dispose();
            window.Close();
        }
    }

    private sealed class Shell : IDisposable
    {
        public Shell()
        {
            Window = new MainWindow(null, new AiAssistantServices(new MemoryKeyStore(), (_, _, _) =>
                throw new InvalidOperationException("The diagram tests do not call the assistant.")));

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

        public async Task<bool> WaitUntilAsync(Func<bool> condition, int attempts = 120)
        {
            for (var i = 0; i < attempts; i++)
            {
                if (condition()) return true;

                await SettleAsync(1);
            }

            return condition();
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
