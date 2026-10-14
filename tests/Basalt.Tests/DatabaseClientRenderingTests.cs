using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
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
/// What the database client draws, read back from a rasterised window: the
/// cell values of a Table Data grid, the keywords of a query editor in each
/// theme, and the file row of the connection dialog.
/// </summary>
/// <remarks>
/// A colour set on a property says nothing about what reaches the screen, so
/// each check rasterises the real IDE window and measures the glyph pixels
/// against the background they were drawn on. Connections are temporary SQLite
/// files, so the engine that answers is the real one; passwords go to an
/// in-memory store, never the keychain.
///
/// The theme is read at startup from BASALT_SETTINGS, which is process-wide, so
/// the class shares <see cref="SettingsFileCollection"/> and restores the
/// application's theme when it finishes.
/// </remarks>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseClientRenderingTests : IDisposable
{
    /// <summary>WCAG AA for body text.</summary>
    private const double TextContrast = 4.5;

    /// <summary>WCAG 1.4.11: the grey of a NULL against its surroundings.</summary>
    private const double GlyphContrast = 3.0;

    private readonly string _root = Directory.CreateTempSubdirectory("basalt-rendering-database-").FullName;

    private readonly string _settings = Path.Combine(
        Path.GetTempPath(), "basalt-rendering-database-settings-" + Guid.NewGuid().ToString("N") + ".json");

    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");

    // Restored on Dispose: opening a window applies its saved theme to the whole
    // application, and a test that leaves Dark behind breaks the next one that
    // assumes Light, depending on run order.
    private readonly ThemeVariant? _previousVariant = Application.Current?.RequestedThemeVariant;

    public DatabaseClientRenderingTests() =>
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);
        if (Application.Current is { } app) app.RequestedThemeVariant = _previousVariant;

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { File.Delete(_settings); } catch (IOException) { }
    }

    // ------------------------------------------------------------------
    // 1. A Table Data cell's value is drawn, in both themes.
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task TableCellValuesAreDrawnInEachThemeAndANullStaysGrey(AppTheme theme)
    {
        SaveTheme(theme);
        var database = CreateShop(_root);

        using var shell = new RenderedShell();
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);
        await AddSqliteConnectionAsync(shell, "Shop", database);

        var view = await OpenTableFromExplorerAsync(shell, "Customers");
        await shell.SettleAsync(3);

        var window = shell.Window;
        var grid = GridOf(view);
        var raster = Raster.Of(window);

        // "Rossi" is a value: its text must reach the cell with clear contrast against the cell's background.
        var value = TextBlockShowing(grid, "Rossi");
        var valueArea = Raster.BoundsIn(value, window);
        var valueBackground = BackgroundOf(raster, valueArea);
        var valueContrast = raster.BestContrastAgainst(valueArea, valueBackground);

        Assert.True(valueContrast >= TextContrast,
            $"'Rossi' reaches only {valueContrast:0.00}:1 against its cell in {theme}: the value is not drawn.");

        // A NULL is drawn in the description colour; the fix for values must leave it drawn.
        var nullCell = TextBlockShowing(grid, Localizer.Get(StringKeys.DbNull));
        var nullArea = Raster.BoundsIn(nullCell, window);
        var nullContrast = raster.BestContrastAgainst(nullArea, BackgroundOf(raster, nullArea));

        Assert.True(nullContrast >= GlyphContrast,
            $"The NULL in {theme} reaches only {nullContrast:0.00}:1 against its cell.");
    }

    // ------------------------------------------------------------------
    // 2. The SQL keywords follow the theme, and follow a change of theme.
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.Dark)]
    public async Task ASqlKeywordReadsOnTheQueryEditorInEachTheme(AppTheme theme)
    {
        SaveTheme(theme);

        using var shell = new RenderedShell();
        await shell.SettleAsync(5);
        var query = await NewQueryAsync(shell, "SELECT 1");

        AssertKeywordReads(shell.Window, query, "SELECT", theme);
    }

    [AvaloniaFact]
    public async Task AnOpenQueryTakesADarkThemeChosenAfterItWasOpened()
    {
        SaveTheme(AppTheme.Light);

        using var shell = new RenderedShell();
        await shell.SettleAsync(5);
        var query = await NewQueryAsync(shell, "SELECT 1");

        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = AppTheme.Dark;
        shell.Window.ApplySettingsForTests(settings);
        await shell.SettleAsync(3);

        AssertKeywordReads(shell.Window, query, "SELECT", AppTheme.Dark);
    }

    [AvaloniaFact]
    public async Task AQueryUnderTheSystemThemeOnADarkSystemTakesTheDarkColours()
    {
        // "Follow the system" was resolved when the view was built, before it was in the window, so it read
        // the default variant and coloured SQL for a light background on a dark one.
        SaveTheme(AppTheme.System);

        using var shell = new RenderedShell();
        await shell.SettleAsync(5);

        // A dark system, as the window's contents see it: the main window leaves the application's variant
        // to the platform under System, and the headless platform is light.
        shell.Window.RequestedThemeVariant = ThemeVariant.Dark;
        await shell.SettleAsync(3);
        Assert.Equal(ThemeVariant.Dark, shell.Window.ActualThemeVariant);

        var query = await NewQueryAsync(shell, "SELECT 1");

        // Which palette the grammar got, read directly: the editor's own background depends on how the
        // window's resources resolve, so a pixel check here could pass with the wrong palette.
        Assert.Equal(AppTheme.Dark, query.ColouringTheme);

        // And when the system turns light again, the colours follow without a settings change.
        shell.Window.RequestedThemeVariant = ThemeVariant.Light;
        await shell.SettleAsync(3);
        Assert.Equal(AppTheme.Light, query.ColouringTheme);
    }

    [AvaloniaFact]
    public async Task AClosedQueryTabTakesItsColouringWithIt()
    {
        // The colouring is a transformer on the editor's view: a tab closed without releasing it kept it,
        // and the grammar state behind it, alive. Reintroduced once (no Dispose on close): the count stayed.
        SaveTheme(AppTheme.Light);

        using var shell = new RenderedShell();
        await shell.SettleAsync(5);

        // Left empty, so the close is not stopped by the question about unsaved text.
        var query = await NewQueryAsync(shell, "");
        Assert.False(query.HasUnsavedChanges);

        var transformers = query.Editor.TextArea.TextView.LineTransformers;
        var whileOpen = transformers.Count;
        Assert.True(whileOpen > 0, "The query has no colouring to release.");

        shell.Window.CloseDatabaseDocumentForTests(Assert.Single(shell.Window.DatabaseDocumentsForTests));
        await shell.SettleAsync(3);

        Assert.Empty(shell.Window.DatabaseDocumentsForTests);
        Assert.True(transformers.Count < whileOpen, "The closed query tab kept its colouring installed.");
    }

    // ------------------------------------------------------------------
    // 3. Browse sits to the right of the file box, at the box's height.
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task BrowseSitsToTheRightOfTheFileBoxAtItsHeight()
    {
        using var shell = new RenderedShell();
        await shell.SettleAsync(5);
        await ShowExplorerAsync(shell);

        var dialog = await OpenAddDialogAsync(shell);
        await shell.SettleAsync(3);

        var box = Raster.BoundsIn(Named<TextBox>(dialog, "DatabaseFile"), dialog);
        var browse = Raster.BoundsIn(Named<Button>(dialog, "DatabaseBrowse"), dialog);

        // A hidden or collapsed row would pass every comparison below with zeros.
        Assert.True(box.Width > 0 && box.Height > 0 && browse.Width > 0 && browse.Height > 0,
            $"The file row is not laid out: box {box}, Browse {browse}.");

        Assert.True(browse.X >= box.Right,
            $"Browse starts at x={browse.X}, which is not right of the file box (it ends at x={box.Right}).");

        var boxCentre = box.Y + box.Height / 2.0;
        var browseCentre = browse.Y + browse.Height / 2.0;

        Assert.True(Math.Abs(boxCentre - browseCentre) <= 1.0,
            $"Browse's centre is {browseCentre} and the box's is {boxCentre}: they are not level.");

        Assert.True(Math.Abs(browse.Height - box.Height) <= 1,
            $"Browse is {browse.Height}px tall and the box {box.Height}px: it is not the box's height.");
    }

    // ------------------------------------------------------------------
    // Measuring
    // ------------------------------------------------------------------

    /// <summary>
    /// Measures a keyword in the query editor against the editor's background, in a raster of the window.
    /// The keyword's colour is the one most of its pixels share: the stems, not the anti-aliased edges.
    /// </summary>
    private static void AssertKeywordReads(MainWindow window, QueryView query, string keyword, AppTheme theme)
    {
        var editor = query.Editor;
        var raster = Raster.Of(window);
        var area = TokenArea(window, editor, 0, keyword.Length);

        // A blank stretch of the same row, well right of the one word on it.
        var rowRight = editor.TranslatePoint(new Point(editor.Bounds.Width - 40, 0), window) ?? default;
        var background = raster.At((int)Math.Round(rowRight.X), area.Y + area.Height / 2);

        var ink = InkOf(raster, area, background);
        var contrast = Raster.Contrast(ink, background);

        Assert.True(contrast >= TextContrast,
            $"'{keyword}' is {ink} on {background} ({contrast:0.00}:1) in {theme}: the keyword does not read.");
    }

    /// <summary>
    /// The rectangle a span of text covers, in the coordinates of <paramref name="root"/>, so it can be
    /// read from a raster of that visual.
    /// </summary>
    private static PixelRect TokenArea(Visual root, TextEditor text, int offset, int length)
    {
        var view = text.TextArea.TextView;
        var start = text.Document.GetLocation(offset);
        var end = text.Document.GetLocation(offset + length);

        var from = view.GetVisualPosition(
            new TextViewPosition(start.Line, start.Column), VisualYPosition.LineTop);
        var to = view.GetVisualPosition(
            new TextViewPosition(end.Line, end.Column), VisualYPosition.LineBottom);

        var topLeft = view.TranslatePoint(from, root) ?? from;
        var bottomRight = view.TranslatePoint(to, root) ?? to;

        return new PixelRect(
            (int)Math.Floor(topLeft.X), (int)Math.Floor(topLeft.Y),
            (int)Math.Ceiling(bottomRight.X - topLeft.X),
            (int)Math.Ceiling(bottomRight.Y - topLeft.Y));
    }

    /// <summary>
    /// The colour most of an area's pixels share, among those that differ visibly from the background.
    /// </summary>
    private static Color InkOf(Raster raster, PixelRect area, Color background)
    {
        var counts = new Dictionary<Color, int>();

        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
            {
                var pixel = raster.At(x, y);
                if (Raster.Contrast(pixel, background) < 1.5) continue;

                counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
            }

        Assert.NotEmpty(counts);

        return counts.MaxBy(pair => pair.Value).Key;
    }

    /// <summary>
    /// The colour behind a cell's text: its top-right corner, where glyphs do not reach, since a value's
    /// text box is only as wide as the text.
    /// </summary>
    private static Color BackgroundOf(Raster raster, PixelRect area) =>
        raster.At(area.Right - 1, area.Y);

    private static TextBlock TextBlockShowing(Visual scope, string text)
    {
        var block = scope.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == text);

        Assert.NotNull(block);

        return block!;
    }

    // ------------------------------------------------------------------
    // The window, the database and the Explorer, as the journeys drive them.
    // ------------------------------------------------------------------

    private void SaveTheme(AppTheme theme)
    {
        var store = new SettingsStore(_settings);
        var settings = store.Load();
        settings.Appearance.Theme = theme;
        store.Save(settings);
    }

    private static string L(string key) => Localizer.Get(key);

    /// <summary>A SQLite file with one table, Customers, whose second row has a NULL city.</summary>
    private static string CreateShop(string root)
    {
        var path = Path.Combine(root, "shop.db");

        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);
            INSERT INTO Customers (Name, City) VALUES ('Rossi', 'Milano'), ('Bianchi', 'Roma'), ('Verdi', NULL);
            """;
        command.ExecuteNonQuery();

        return path;
    }

    private static async Task ShowExplorerAsync(RenderedShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.MenuView), L(StringKeys.DbExplorerTitle));

        // The dock builds a tool's content on its first layout pass, so the tree exists only after the window settles.
        await shell.SettleAsync(5);
    }

    /// <summary>Opens the connection dialog from the Database menu and fills in a SQLite file.</summary>
    private static async Task AddSqliteConnectionAsync(RenderedShell shell, string name, string path)
    {
        var dialog = await OpenAddDialogAsync(shell);

        Named<TextBox>(dialog, "DatabaseName").Text = name;
        Named<TextBox>(dialog, "DatabaseFile").Text = path;
        Click(Named<Button>(dialog, "DatabaseOk"));

        Assert.True(await WaitUntilAsync(shell, () => shell.Explorer.Roots.Count > 0), "The connection was not saved.");
    }

    private static async Task<DatabaseConnectionDialog> OpenAddDialogAsync(RenderedShell shell)
    {
        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbAddConnection));
        await shell.SettleAsync(3);

        var dialog = await WaitForDialogAsync(shell);

        Assert.True(dialog is DatabaseConnectionDialog, "The connection dialog did not open.");

        return (DatabaseConnectionDialog)dialog!;
    }

    /// <summary>Opens a new query from the Database menu and gives it the SQL.</summary>
    private static async Task<QueryView> NewQueryAsync(RenderedShell shell, string sql)
    {
        InvokeMenu(shell.Window, L(StringKeys.DbMenu), L(StringKeys.DbNewQuery));
        await shell.SettleAsync(3);

        var query = Assert.Single(shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<QueryView>());
        query.Sql = sql;
        await shell.SettleAsync(3);

        return query;
    }

    /// <summary>Opens a table by double-clicking it in the Explorer, and waits for its rows.</summary>
    private static async Task<TableDataView> OpenTableFromExplorerAsync(RenderedShell shell, string table)
    {
        var connection = Assert.IsType<ConnectionNode>(Assert.Single(shell.Explorer.Roots));
        await ExpandAsync(shell, connection);
        await WaitUntilAsync(shell, () => connection.IsLoaded);

        var tables = connection.Children.OfType<GroupNode>()
            .Single(g => g.Text.StartsWith(L(StringKeys.DbTablesGroup) + " (", StringComparison.Ordinal));
        await ExpandAsync(shell, tables);

        DoubleClick(shell, tables.Children.OfType<ObjectNode>().Single(o => o.Text == table));

        return await WaitForTableAsync(shell, table);
    }

    private static async Task<TableDataView> WaitForTableAsync(RenderedShell shell, string name)
    {
        TableDataView? view = null;

        await WaitUntilAsync(shell, () =>
        {
            view = shell.Window.DatabaseDocumentsForTests.Select(d => d.View).OfType<TableDataView>()
                .FirstOrDefault(v => v.Table.Name == name && !v.IsRemoved);

            return view is not null && view.RangeText.Length > 0;
        });

        return view ?? throw new InvalidOperationException($"The rows of {name} were not shown.");
    }

    private static async Task ExpandAsync(RenderedShell shell, ExplorerNode node)
    {
        ItemFor(shell, node).IsExpanded = true;
        await shell.SettleAsync(3);
    }

    /// <summary>Double-clicks a node: its row is selected and the double tap reaches the tree.</summary>
    private static void DoubleClick(RenderedShell shell, ExplorerNode node)
    {
        var item = ItemFor(shell, node);
        item.IsSelected = true;

        // Raised on the row itself, as a real double-click's source is: the tree finds the node from it.
        item.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
    }

    private static TreeViewItem ItemFor(RenderedShell shell, ExplorerNode node) =>
        shell.Explorer.GetVisualDescendants().OfType<TreeView>().Single()
            .GetVisualDescendants().OfType<TreeViewItem>()
            .Single(item => ReferenceEquals(item.DataContext, node));

    private static DataGrid GridOf(Visual scope) => scope.GetVisualDescendants().OfType<DataGrid>().Single();

    private static T Named<T>(Visual scope, string name) where T : Control =>
        scope.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void InvokeMenu(MainWindow window, params string[] path)
    {
        IReadOnlyList<MenuEntry> level = window.MenuEntriesForTests();
        MenuEntry? hit = null;

        foreach (var header in path)
        {
            hit = level.FirstOrDefault(e => e.Header == header)
                ?? throw new InvalidOperationException($"No menu entry named {header}.");

            level = hit.Children ?? [];
        }

        hit!.Action!.Invoke();
    }

    private static async Task<Window?> WaitForDialogAsync(RenderedShell shell)
    {
        Window? dialog = null;

        for (var i = 0; i < 80 && dialog is null; i++)
        {
            await shell.SettleAsync(1);
            dialog = shell.Window.OwnedWindows.FirstOrDefault();
        }

        return dialog;
    }

    private static async Task<bool> WaitUntilAsync(RenderedShell shell, Func<bool> condition, int attempts = 120)
    {
        for (var i = 0; i < attempts; i++)
        {
            if (condition()) return true;

            await shell.SettleAsync(1);
        }

        return condition();
    }

    /// <summary>The IDE window at a size the rasteriser can read, with the in-memory stores the database client needs.</summary>
    private sealed class RenderedShell : IDisposable
    {
        public RenderedShell()
        {
            Window = new MainWindow(null, new AiAssistantServices(new MemoryKeyStore(), (_, _, _) =>
                throw new InvalidOperationException("The database rendering checks do not call the assistant.")));

            Window.DatabaseWorkspaceForTests.UsePasswords(new MemoryDatabasePasswordStore());
            Window.Width = 1440;
            Window.Height = 900;
            Window.Show();
        }

        public MainWindow Window { get; }

        public DatabaseExplorerPanel Explorer => Window.DatabaseExplorerForTests;

        /// <summary>Several layout passes with short pauses, so panels built over more than one pass exist.</summary>
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

    /// <summary>An in-memory key store: nothing reaches the keychain or the key file.</summary>
    private sealed class MemoryKeyStore : IApiKeyStore
    {
        public bool UsesPlatformKeychain => false;

        public string? Get(AiVendor vendor) => null;

        public bool Set(AiVendor vendor, string apiKey) => true;

        public bool Remove(AiVendor vendor) => true;

        public bool Has(AiVendor vendor) => false;
    }
}
