using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;
using Basalt.Shell.Database;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// The buttons of the database views wear an icon beside their label: the Explorer's toolbar, the query view, the
/// table data view, the table designer and the diagram.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseButtonIconTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-button-icons-").FullName;

    private readonly string _settings;
    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");
    private readonly ThemeVariant? _previousVariant = Application.Current?.RequestedThemeVariant;
    private readonly List<Window> _windows = [];

    public DatabaseButtonIconTests()
    {
        _settings = Path.Combine(_root, "settings.json");
        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _settings);
    }

    public void Dispose()
    {
        foreach (var window in _windows) window.Close();

        Environment.SetEnvironmentVariable("BASALT_SETTINGS", _previousSettings);

        if (Application.Current is { } app) app.RequestedThemeVariant = _previousVariant;

        SqliteConnection.ClearAllPools();

        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [AvaloniaFact]
    public async Task EveryButtonOfTheDatabaseViewsShowsAnIconAndATooltip()
    {
        var (workspace, connectionId) = await WorkspaceWithShopAsync();

        var views = new (string Name, Control View)[]
        {
            ("explorer", new DatabaseExplorerPanel(workspace)),
            ("query", new QueryView(workspace, 1, connectionId, "", null, AppTheme.Light)),
            ("table data", new TableDataView(workspace, connectionId, new DbObjectName(null, "Customers"), DbObjectKind.Table)),
            ("table designer", new TableDesignerView(workspace, connectionId, null)),
            ("diagram", new DatabaseDiagramView(workspace, connectionId, new DiagramLayoutStore(DiagramLayoutStore.PathBeside(_settings)))),
        };

        foreach (var (name, view) in views)
        {
            var window = Show(view);
            await Settle(window);

            var buttons = view.GetVisualDescendants().OfType<Button>().Where(IsToolbarButton).ToList();

            Assert.True(buttons.Count > 0, $"The {name} view has no buttons to check.");

            foreach (var button in buttons)
            {
                var label = button.Content as string;

                // The label stays a string, so a button is still found by its words.
                Assert.True(!string.IsNullOrWhiteSpace(label), $"{name}: a button has no text label ({button.Content}).");
                Assert.True(button.ContentTemplate is not null, $"{name}: '{label}' has no icon template.");
                Assert.True(!string.IsNullOrWhiteSpace(ToolTip.GetTip(button)?.ToString()), $"{name}: '{label}' has no tooltip.");

                // The face the template makes holds a drawn icon and the label. Built here as well as read from the
                // screen, because a button that is hidden until it is needed (Reconnect) has no visuals yet.
                var face = (Control)((IDataTemplate)button.ContentTemplate!).Build(button.Content)!;
                var icon = face.GetVisualDescendants().Prepend(face).OfType<IconView>().SingleOrDefault();

                Assert.True(icon is not null && icon.Kind != IconKind.None, $"{name}: '{label}' shows no icon.");
                Assert.NotNull(IdeIcons.PathDataFor(icon!.Kind));

                if (button.IsEffectivelyVisible)
                {
                    var shown = button.GetVisualDescendants().OfType<IconView>().ToList();
                    Assert.True(shown.Count == 1 && shown[0].Kind != IconKind.None, $"{name}: '{label}' draws no icon on screen.");
                }
            }
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public async Task TheIconOfAButtonIsDrawnWithInkThatReadsOnTheThemesBackground(string theme)
    {
        // A neutral icon follows the chrome foreground: pixels, not properties, say it did.
        var variant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        // The application's variant, which is what the theme's brushes are looked up under (restored in Dispose).
        Application.Current!.RequestedThemeVariant = variant;

        var button = DatabaseButtons.Create(IconKind.Refresh, "Refresh");
        var window = Show(button);
        window.RequestedThemeVariant = variant;
        await Settle(window);

        Assert.Equal(variant, window.ActualThemeVariant);

        var icon = button.GetVisualDescendants().OfType<IconView>().Single();
        var raster = Raster.Of(window);
        var area = Raster.BoundsIn(icon, window);

        Assert.True(area.Width > 0 && area.Height > 0, "The icon was not laid out.");

        // The button's own face, at the edge of the icon box, is what the ink is read against.
        var face = raster.At(Math.Max(0, area.X - 2), area.Y + area.Height / 2);

        Assert.True(
            raster.BestContrastAgainst(area, face) >= 3.0,
            $"The icon does not read on the {theme} theme (best contrast {raster.BestContrastAgainst(area, face):0.0}:1 against {face}).");
    }

    [AvaloniaFact]
    public async Task ADisabledButtonsIconIsDimmedLikeItsLabel()
    {
        var (workspace, connectionId) = await WorkspaceWithShopAsync();

        var view = new TableDataView(workspace, connectionId, new DbObjectName(null, "Customers"), DbObjectKind.Table);
        var window = Show(view);
        await Settle(window);

        // Nothing has changed yet, so Save is disabled.
        var save = view.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Basalt.Core.Localization.Localizer.Get(Basalt.Core.Localization.StringKeys.DbSave));
        Assert.False(save.IsEnabled);

        var disabled = InkContrast(window, save);

        save.IsEnabled = true;
        await Settle(window);

        var enabled = InkContrast(window, save);

        // Full strength when it can be used, a good deal fainter when it cannot.
        Assert.True(disabled < enabled * 0.6, $"The disabled icon ({disabled:0.0}:1) is nearly as strong as the enabled one ({enabled:0.0}:1).");
    }

    private static double InkContrast(Window window, Button button)
    {
        var icon = button.GetVisualDescendants().OfType<IconView>().Single();
        var raster = Raster.Of(window);
        var area = Raster.BoundsIn(icon, window);
        var face = raster.At(Math.Max(0, area.X - 2), area.Y + area.Height / 2);

        return raster.BestContrastAgainst(area, face);
    }

    // ------------------------------------------------------------------

    /// <summary>The buttons the views draw themselves; the ones inside a grid or an editor belong to their controls.</summary>
    private static bool IsToolbarButton(Button button) =>
        button.GetType() == typeof(Button)
        && button.GetVisualAncestors().All(a => a is not (DataGrid or AvaloniaEdit.TextEditor or Avalonia.Controls.Primitives.ScrollBar or ComboBox or TreeView));

    private Window Show(Control content)
    {
        var window = new Window { Width = 1100, Height = 700, Content = content };

        _windows.Add(window);
        window.Show();

        return window;
    }

    private static async Task Settle(Window window)
    {
        for (var i = 0; i < 4; i++)
        {
            window.UpdateLayout();
            await Task.Delay(50);
        }

        window.UpdateLayout();
    }

    private async Task<(DatabaseWorkspace Workspace, string ConnectionId)> WorkspaceWithShopAsync()
    {
        var path = Path.Combine(_root, "shop.db");

        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT);";
            command.ExecuteNonQuery();
        }

        var workspace = new DatabaseWorkspace(new SettingsStore(_settings), new MemoryDatabasePasswordStore());
        var saved = await workspace.AddAsync(
            new DatabaseDraft("Shop", DatabaseKind.Sqlite, path, "", "", "", "", "", false, false, false, "Prefer", ""));

        return (workspace, saved.Id);
    }
}
