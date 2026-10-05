using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Shell;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The Visual Studio style property grid and the events grid take their colours
/// from the IDE theme. Expected colours are read from Theme.axaml through the
/// application, not from the palette class, so a palette that goes back to a
/// hand-picked Light/Dark pair fails here.
/// </summary>
public class WinFormsPropertyGridThemeFollowTests
{
    public static TheoryData<string> Themes => new()
    {
        "Light", "Dark", "VisualStudioBlue", "HighContrastLight", "HighContrastDark", "SolarizedLight", "SolarizedDark",
    };

    private static ThemeVariant Variant(string name) => name switch
    {
        "Light" => ThemeVariant.Light,
        "Dark" => ThemeVariant.Dark,
        "VisualStudioBlue" => IdeThemes.VisualStudioBlue,
        "HighContrastLight" => IdeThemes.HighContrastLight,
        "HighContrastDark" => IdeThemes.HighContrastDark,
        "SolarizedLight" => IdeThemes.SolarizedLight,
        _ => IdeThemes.SolarizedDark,
    };

    private static Color ThemeColor(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return Assert.IsType<Color>(value);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte value)
        {
            var c = value / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }

    internal static double Contrast(Color a, Color b)
    {
        var (hi, lo) = (Luminance(a), Luminance(b));
        if (hi < lo) (hi, lo) = (lo, hi);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static PropertyGrid CreateGrid() => new()
    {
        VisualStudioStyle = true,
        Properties =
        [
            new DesignableProperty("Alpha", typeof(string), PropertyEditorKind.Text, PropertyCategory.Appearance, [], "One")
            {
                Description = "The first property",
                CategoryName = "Misc",
            },
            new DesignableProperty("AccessibleDefaultActionDescription", typeof(string), PropertyEditorKind.Text,
                PropertyCategory.Appearance, [], "Two")
            {
                CategoryName = "Misc",
                IsReadOnly = true,
            },
        ],
    };

    private static Window Show(Control content, ThemeVariant variant, int width = 360, int height = 260)
    {
        var window = new Window { Content = content, Width = width, Height = height, RequestedThemeVariant = variant };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static Border Row(PropertyGrid grid, string name) => grid.Rows.OfType<Border>()
        .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static byte[] Render(Window window)
    {
        var width = (int)window.Width;
        var height = (int)window.Height;
        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));
        bitmap.Render(window);
        var bytes = new byte[width * height * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), bytes.Length, width * 4);
        }
        finally
        {
            pin.Free();
        }

        return bytes;
    }

    private static void AssertPixel(byte[] bytes, int width, Point point, Color expected, string what)
    {
        var offset = ((int)point.Y * width + (int)point.X) * 4;
        Assert.True(expected.R == bytes[offset] && expected.G == bytes[offset + 1] && expected.B == bytes[offset + 2],
            $"{what}: expected {expected} but the pixel at {point} is ({bytes[offset]}, {bytes[offset + 1]}, {bytes[offset + 2]})");
    }

    [AvaloniaTheory]
    [MemberData(nameof(Themes))]
    public void GridFollowsTheIdeTheme(string themeName)
    {
        var variant = Variant(themeName);
        var grid = CreateGrid();
        var elsewhere = new Button { Height = 24 };
        DockPanel.SetDock(elsewhere, Avalonia.Controls.Dock.Bottom);
        var window = Show(new DockPanel { Children = { elsewhere, grid } }, variant);
        try
        {
            var row = Row(grid, "Alpha");
            row.Focus();
            Dispatcher.UIThread.RunJobs();

            var background = ThemeColor("EditorBackground", variant);
            var foreground = ThemeColor("EditorForeground", variant);
            var selection = ThemeColor("ListActiveSelectionBackground", variant);
            var selectionText = ThemeColor("ListActiveSelectionForeground", variant);
            var line = ThemeColor("PanelBorder", variant);
            var pane = ThemeColor("SideBarBackground", variant);

            var bytes = Render(window);
            AssertPixel(bytes, 360, new Point(330, 130), background, "grid body");
            AssertPixel(bytes, 360, row.TranslatePoint(new Point(90, 4), window)!.Value, selection, "selected row");
            AssertPixel(bytes, 360, row.TranslatePoint(new Point(121, 8), window)!.Value, line, "name/value splitter");
            var description = grid.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PropertyGridDescription");
            AssertPixel(bytes, 360, description.TranslatePoint(new Point(300, 40), window)!.Value, pane, "help pane");

            Assert.True(Contrast(foreground, background) >= 4.5, "normal text contrast");
            var labelBrush = Assert.IsAssignableFrom<ISolidColorBrush>(
                row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "Alpha").Foreground);
            Assert.True(Contrast(labelBrush.Color, selection) >= 4.5, "selected text contrast");
            // The theme's own token wins whenever it is already readable.
            if (Contrast(selectionText, selection) >= 4.5)
                Assert.Equal(selectionText, labelBrush.Color);
            var readOnly = Assert.IsAssignableFrom<ISolidColorBrush>(grid.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == "AccessibleDefaultActionDescription").Foreground);
            Assert.True(Contrast(readOnly.Color, background) >= 3.0, "read-only text stays legible");

            // Focus leaves: the selection stays, in the quieter inactive colour.
            elsewhere.Focus();
            Dispatcher.UIThread.RunJobs();
            var inactive = ThemeColor("ListInactiveSelectionBackground", variant);
            bytes = Render(window);
            AssertPixel(bytes, 360, row.TranslatePoint(new Point(90, 4), window)!.Value, inactive, "unfocused selection");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LiveSwitchBetweenCustomVariantsRepaintsTheGrid()
    {
        var grid = CreateGrid();
        var window = Show(grid, ThemeVariant.Light);
        try
        {
            foreach (var name in new[] { "HighContrastDark", "SolarizedLight", "Dark", "HighContrastLight" })
            {
                var variant = Variant(name);
                window.RequestedThemeVariant = variant;
                Dispatcher.UIThread.RunJobs();
                AssertPixel(Render(window), 360, new Point(330, 130), ThemeColor("EditorBackground", variant), name);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("HighContrastDark")]
    public void ToolbarCheckedStateAndLongNamesFollowTheTheme(string themeName)
    {
        var variant = Variant(themeName);
        var grid = CreateGrid();
        var window = Show(grid, variant);
        try
        {
            var checkedButton = grid.GetVisualDescendants().OfType<ToggleButton>().First(b => b.IsChecked == true);
            var bytes = Render(window);
            var at = checkedButton.TranslatePoint(new Point(2, 2), window)!.Value;
            AssertPixel(bytes, 360, at, ThemeColor("ListActiveSelectionBackground", variant), "checked toolbar button");

            var longName = grid.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == "AccessibleDefaultActionDescription");
            Assert.Equal("AccessibleDefaultActionDescription", ToolTip.GetTip(longName));
            Assert.Equal(TextTrimming.CharacterEllipsis, longName.TextTrimming);
            Assert.Equal(12, longName.FontSize);
            Assert.Equal(WinFormsPropertyGridPalette.RowHeight, Row(grid, "AccessibleDefaultActionDescription").Bounds.Height);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    [InlineData("HighContrastDark")]
    public void ACheckedToolbarButtonKeepsItsLookUnderThePointerAndAnUncheckedOneGetsTheHoverFill(string themeName)
    {
        var variant = Variant(themeName);
        var grid = CreateGrid();
        var window = Show(grid, variant);
        try
        {
            var buttons = grid.GetVisualDescendants().OfType<ToggleButton>().ToList();
            var on = buttons.First(b => b.IsChecked == true);
            var off = buttons.First(b => b.IsChecked != true);
            var selection = ThemeColor("ListActiveSelectionBackground", variant);
            var hover = ThemeColor("ListInactiveSelectionBackground", variant);

            // Headless has no pointer to move, so the pseudo-class Avalonia would set is set here.
            ((IPseudoClasses)on.Classes).Set(":pointerover", true);
            ((IPseudoClasses)off.Classes).Set(":pointerover", true);
            Dispatcher.UIThread.RunJobs();

            var bytes = Render(window);
            AssertPixel(bytes, 360, on.TranslatePoint(new Point(2, 2), window)!.Value, selection,
                "checked button under the pointer");
            AssertPixel(bytes, 360, off.TranslatePoint(new Point(2, 2), window)!.Value, hover,
                "unchecked button under the pointer");

            ((IPseudoClasses)on.Classes).Set(":pressed", true);
            Dispatcher.UIThread.RunJobs();
            AssertPixel(Render(window), 360, on.TranslatePoint(new Point(2, 2), window)!.Value, selection,
                "checked button pressed");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("HighContrastDark")]
    [InlineData("SolarizedLight")]
    public void EventsPanelMatchesTheGridAndTheTheme(string themeName)
    {
        var variant = Variant(themeName);
        var panel = new WinFormsEventPanel { VisualStudioStyle = true };
        panel.Show(new WinFormsEventInspectorTests.TestComponent());
        var window = Show(panel, variant, 420, 360);
        try
        {
            var category = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "EventCategory_Behavior");
            var editor = panel.GetVisualDescendants().OfType<TextBox>().Single();
            var row = editor.GetVisualAncestors().OfType<Grid>().First();
            editor.Focus();
            Dispatcher.UIThread.RunJobs();

            var bytes = Render(window);
            AssertPixel(bytes, 420, category.TranslatePoint(new Point(180, 10), window)!.Value,
                ThemeColor("SideBarBackground", variant), "category row");
            AssertPixel(bytes, 420, row.TranslatePoint(new Point(100, 2), window)!.Value,
                ThemeColor("ListActiveSelectionBackground", variant), "selected event row");
            AssertPixel(bytes, 420, new Point(400, 200), ThemeColor("EditorBackground", variant), "grid body");

            // Same row height and the same name column as the property grid.
            Assert.Equal(WinFormsPropertyGridPalette.RowHeight, row.Bounds.Height);
            Assert.Equal(WinFormsPropertyGridPalette.RowHeight, category.Bounds.Height);
            Assert.Equal(120 + 3, editor.TranslatePoint(new Point(), row)!.Value.X);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EventsPanelWithoutAControlShowsAnEmptyState()
    {
        var panel = new WinFormsEventPanel { VisualStudioStyle = true };
        panel.Show(null);
        var window = Show(panel, ThemeVariant.Dark, 420, 360);
        try
        {
            var empty = panel.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "EventsEmptyState");
            Assert.Equal(Localizer.Get(StringKeys.EventsNothingSelected), empty.Text);
            Assert.Empty(panel.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal(ThemeColor("DescriptionForeground", ThemeVariant.Dark),
                Assert.IsAssignableFrom<ISolidColorBrush>(empty.Foreground).Color);
        }
        finally
        {
            window.Close();
        }
    }
}
