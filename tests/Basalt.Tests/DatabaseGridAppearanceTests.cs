using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Core.Settings;
using Basalt.Data.Sessions;
using Basalt.Shell.Database;
using Microsoft.Data.Sqlite;

namespace Basalt.Tests;

/// <summary>
/// How the database grids look, read from rasterised windows: the column headers in the dark theme, the Type
/// cell of the table designer, and the designer's headers fitting their words.
/// </summary>
[Collection(SettingsFileCollection.Name)]
public sealed class DatabaseGridAppearanceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basalt-grid-looks-").FullName;
    private readonly string _settings;
    private readonly string? _previousSettings = Environment.GetEnvironmentVariable("BASALT_SETTINGS");
    private readonly ThemeVariant? _previousVariant = Application.Current?.RequestedThemeVariant;
    private readonly List<Window> _windows = [];

    public DatabaseGridAppearanceTests()
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

    // ------------------------------------------------------------------
    // 1. Headers in the dark theme
    // ------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task TheColumnHeadersOfAResultGridAreNotBlackAndSitCloseToTheCells(string theme)
    {
        UseTheme(theme);

        var grid = DatabaseGrids.ReadOnly(
            [new ResultColumnInfo("Id", "INTEGER"), new ResultColumnInfo("Name", "TEXT")],
            [[1L, "Rossi"], [2L, "Bianchi"]]);

        var window = Show(grid);
        await Settle(window);

        AssertHeaderLooksRight(window, grid, theme);
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public async Task TheColumnHeadersOfTheTableDesignerAreNotBlackAndSitCloseToTheCells(string theme)
    {
        UseTheme(theme);

        var designer = await DesignerAsync();
        var window = Show(designer);
        await Settle(window);

        AssertHeaderLooksRight(window, designer.Grid, theme);
    }

    private static void AssertHeaderLooksRight(Window window, DataGrid grid, string theme)
    {
        // A header pixel clear of its text: the padding left of the first column's label, inside the header.
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>().First();
        var raster = Raster.Of(window);
        var area = Raster.BoundsIn(header, window);

        var headerPixel = raster.At(area.X + 5, area.Y + area.Height / 2);

        var row = grid.GetVisualDescendants().OfType<DataGridRow>().First();
        var firstCell = Raster.BoundsIn(row.GetVisualDescendants().OfType<DataGridCell>().First(), window);
        var cellPixel = raster.At(firstCell.X + firstCell.Width - 4, firstCell.Y + firstCell.Height - 4);

        Assert.True(Raster.Contrast(headerPixel, Colors.Black) > 1.15, $"The {theme} header is black ({headerPixel}).");

        // A different surface, but a small step from the cells beside it: a bar, not a hole.
        Assert.True(Raster.Contrast(headerPixel, cellPixel) < 1.6,
            $"The {theme} header ({headerPixel}) is far from the cell background ({cellPixel}).");
    }

    // ------------------------------------------------------------------
    // 2. The Type cell
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task TheTypeTextSitsOnTheSameLineAsTheNameInTheFirstRow()
    {
        UseTheme("Light");

        var designer = await DesignerAsync();
        var window = Show(designer);
        await Settle(window);

        var row = designer.Grid.GetVisualDescendants().OfType<DataGridRow>().First();
        var name = row.GetVisualDescendants().OfType<TextBox>().First();
        var type = row.GetVisualDescendants().OfType<ComboBox>().First();

        var raster = Raster.Of(window);

        var nameCentre = InkCentre(raster, window, name, 0.5);
        var typeCentre = InkCentre(raster, window, type, 0.6);

        Assert.True(Math.Abs(nameCentre - typeCentre) <= 3.0,
            $"The Type text is centred at y={typeCentre:0.0} and the Name text at y={nameCentre:0.0}: they are not on one line.");
    }

    /// <summary>The vertical centre of the dark pixels in the left part of a control, in window coordinates.</summary>
    private static double InkCentre(Raster raster, Window window, Control control, double leftShare)
    {
        var area = Raster.BoundsIn(control, window);
        var background = raster.At(area.X + 1, area.Y + 1);
        double top = double.MaxValue, bottom = double.MinValue;

        // Inside the box, clear of its borders and, for the combo, of the arrow on the right.
        for (var y = area.Y + 2; y < area.Bottom - 2; y++)
            for (var x = area.X + 4; x < area.X + (int)(area.Width * leftShare); x++)
            {
                if (Raster.Contrast(raster.At(x, y), background) < 3.0) continue;

                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y);
            }

        Assert.True(top < double.MaxValue, $"No text was drawn in {control.GetType().Name}.");

        return (top + bottom) / 2;
    }

    // ------------------------------------------------------------------
    // 3. The designer's headers fit their words
    // ------------------------------------------------------------------

    [AvaloniaFact]
    public async Task EveryHeaderOfTheTableDesignerShowsItsWholeTextAndCanBeResized()
    {
        UseTheme("Light");

        var designer = await DesignerAsync();
        var window = Show(designer);
        await Settle(window);

        Assert.True(designer.Grid.CanUserResizeColumns);

        var headers = designer.Grid.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string).ToList();

        Assert.Equal(6, headers.Count);

        foreach (var header in headers)
        {
            var text = (string)header.Content!;
            var label = header.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == text);

            label.Measure(Size.Infinity);

            // The label got all the room its words need: a header cut to "Allow nu" is given less.
            Assert.True(label.Bounds.Width + 0.5 >= label.DesiredSize.Width,
                $"'{text}' is cut: it needs {label.DesiredSize.Width:0.#}px and has {label.Bounds.Width:0.#}px.");

            // And it sits inside the header, not running over its edge.
            Assert.True(label.Bounds.Right <= header.Bounds.Width + 0.5, $"'{text}' runs past its column.");
        }

        foreach (var column in designer.Grid.Columns)
        {
            Assert.True(column.CanUserResize, $"{column.Header} cannot be resized.");
            Assert.True(column.ActualWidth >= column.MinWidth, $"{column.Header} is narrower than its minimum.");
        }
    }

    // ------------------------------------------------------------------

    private void UseTheme(string theme) =>
        Application.Current!.RequestedThemeVariant = theme == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

    private Window Show(Control content)
    {
        var window = new Window { Width = 1000, Height = 500, Content = content };

        window.RequestedThemeVariant = Application.Current!.RequestedThemeVariant;
        _windows.Add(window);
        window.Show();

        return window;
    }

    private static async Task Settle(Window window)
    {
        for (var i = 0; i < 5; i++)
        {
            window.UpdateLayout();
            await Task.Delay(50);
        }

        window.UpdateLayout();
    }

    private async Task<TableDesignerView> DesignerAsync()
    {
        var path = Path.Combine(_root, "shop.db");

        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE Customers (Id INTEGER PRIMARY KEY, Name TEXT NOT NULL, City TEXT);";
            command.ExecuteNonQuery();
        }

        var workspace = new DatabaseWorkspace(new SettingsStore(_settings), new MemoryDatabasePasswordStore());
        var saved = await workspace.AddAsync(
            new DatabaseDraft("Shop", DatabaseKind.Sqlite, path, "", "", "", "", "", false, false, false, "Prefer", ""));

        var view = new TableDesignerView(workspace, saved.Id, new DbObjectName(null, "Customers"));

        // Loaded after it is shown, as the document does; the grid's columns are built by the load.
        await view.LoadAsync();

        return view;
    }
}
