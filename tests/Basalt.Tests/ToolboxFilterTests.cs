using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

/// <summary>
/// The toolbox filter, its collapsible categories and the Forms categories.
///
/// A hundred Forms controls in one alphabetical column, and a filter-less
/// Avalonia toolbox, made the control you want a scroll away. The listing
/// rules are tested without a window; the view is tested through the keys a
/// user presses.
/// </summary>
public sealed class ToolboxFilterTests
{
    private static readonly IReadOnlySet<string> NoneFolded = new HashSet<string>();

    private static List<object> Listing(string? filter, params string[] folded) =>
        ToolboxListing.Build(
            ToolboxCatalog.All, item => item.Category, item => [item.DisplayName, item.ElementName],
            filter, new HashSet<string>(folded));

    [Fact]
    public void TheFilterFindsAnEntryWhicheverWayItIsSpelled()
    {
        // The catalogue says "Check Box", the markup says CheckBox, and
        // people type either.
        foreach (var query in new[] { "check box", "CheckBox", "checkb" })
        {
            var found = Listing(query).OfType<ToolboxItem>().ToList();

            Assert.Contains(found, item => item.ElementName == "CheckBox");
            Assert.DoesNotContain(found, item => item.ElementName == "Button");
        }
    }

    [Fact]
    public void TheFilterKeepsOnlyTheCategoriesThatHaveAMatch()
    {
        var rows = Listing("grid");

        Assert.All(rows.OfType<ToolboxItem>(), item =>
            Assert.Contains("grid", item.DisplayName + item.ElementName, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(rows.OfType<ToolboxItem>().Select(i => i.Category).Distinct(),
            rows.OfType<ToolboxHeading>().Select(h => h.Name));
        Assert.All(rows.OfType<ToolboxHeading>(), heading =>
            Assert.Equal(rows.OfType<ToolboxItem>().Count(i => i.Category == heading.Name), heading.Count));
    }

    [Fact]
    public void AFoldedCategoryHidesItsEntriesUntilTheUserFilters()
    {
        var folded = Listing(null, "Layout");

        Assert.Contains(folded.OfType<ToolboxHeading>(), h => h.Name == "Layout" && h.Collapsed);
        Assert.DoesNotContain(folded.OfType<ToolboxItem>(), item => item.Category == "Layout");

        // Filtering opens it again: a match inside a folded category that
        // stayed hidden would read as "no such control".
        var filtered = Listing("grid", "Layout");
        Assert.Contains(filtered.OfType<ToolboxItem>(), item => item.Category == "Layout");
        Assert.DoesNotContain(filtered.OfType<ToolboxHeading>(), h => h.Collapsed);
    }

    [Theory]
    [InlineData(typeof(System.Windows.Forms.Button), "Common Controls")]
    [InlineData(typeof(System.Windows.Forms.Panel), "Containers")]
    [InlineData(typeof(System.Windows.Forms.MenuStrip), "Menus & Toolbars")]
    [InlineData(typeof(System.Windows.Forms.DataGridView), "Data")]
    [InlineData(typeof(System.Windows.Forms.OpenFileDialog), "Dialogs")]
    [InlineData(typeof(System.Windows.Forms.Timer), "Components")]
    public void FormsControlsAreFiledWhereVisualStudioPutsThem(Type type, string category) =>
        Assert.Equal(category, new WinFormsToolboxItem(type).Category);

    [Fact]
    public void AControlFromTheUsersOwnAssemblyFallsUnderComponents() =>
        Assert.Equal("Components", new WinFormsToolboxItem(typeof(ToolboxFilterTests)).Category);

    [AvaloniaFact]
    public void TypingInTheFilterNarrowsTheAvaloniaToolbox()
    {
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 260, Height = 600 };
        window.Show();

        try
        {
            panel.Body.FilterText = "slid";

            var items = panel.Body.List.ItemsSource!.OfType<ToolboxItem>().ToList();
            Assert.Equal("Slider", Assert.Single(items).ElementName);
            Assert.False(panel.Body.ShowsNoMatches);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AFilterThatMatchesNothingSaysSoInsteadOfShowingABlankPanel()
    {
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 260, Height = 600 };
        window.Show();

        try
        {
            panel.Body.FilterText = "zzzz";

            Assert.True(panel.Body.ShowsNoMatches);
            Assert.Contains("zzzz", panel.Body.NoMatchesMessage);

            panel.Body.FilterText = "";
            Assert.False(panel.Body.ShowsNoMatches);
            Assert.NotEmpty(panel.Body.List.ItemsSource!.OfType<ToolboxItem>());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TheKeyboardFiltersFoldsAndAddsWithoutTheMouse()
    {
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 260, Height = 600 };
        var chosen = new List<ToolboxItem>();
        panel.ControlChosen += (_, item) => chosen.Add(item);
        window.Show();

        try
        {
            var body = panel.Body;

            // Enter on a heading folds it and inserts nothing.
            body.List.SelectedIndex = 0;
            var firstCategory = ((ToolboxHeading)body.List.SelectedItem!).Name;
            Press(body.List, Key.Enter);
            Assert.True(body.IsCollapsed(firstCategory));
            Assert.Empty(chosen);
            Assert.IsType<ToolboxHeading>(body.List.SelectedItem);

            // Right unfolds it, Left folds it again.
            Press(body.List, Key.Right);
            Assert.False(body.IsCollapsed(firstCategory));
            Press(body.List, Key.Left);
            Assert.True(body.IsCollapsed(firstCategory));

            // Typed into the filter, Enter adds the first match.
            body.FilterText = "slid";
            Press(body.Filter, Key.Enter);
            Assert.Equal("Slider", Assert.Single(chosen).ElementName);

            // Escape empties the box.
            Press(body.Filter, Key.Escape);
            Assert.Equal("", body.FilterText);

            // Enter on an entry in the list adds it.
            var item = body.List.ItemsSource!.OfType<ToolboxItem>().First();
            body.List.SelectedItem = item;
            Press(body.List, Key.Enter);
            Assert.Same(item, chosen[^1]);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AClickOnAHeadingFoldsItOnceAndADoubleClickDoesNotFoldItBack()
    {
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 260, Height = 600 };
        window.Show();
        window.UpdateLayout();

        try
        {
            var body = panel.Body;
            var rows = body.List.ItemsSource;
            var category = ((ToolboxHeading)body.List.ItemsSource!.Cast<object>().First()).Name;
            var activated = 0;
            body.ItemActivated += (_, _) => activated++;

            // One click folds, once the press has finished.
            Press(body, category, clickCount: 1);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsCollapsed(category));
            Press(body, category, clickCount: 1);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(body.IsCollapsed(category));

            // A double-click is a press, a second press with a click count of
            // two, and DoubleTapped. It must change the state once, not fold
            // it and then fold it back.
            // Avalonia raises DoubleTapped itself from the second press.
            Press(body, category, clickCount: 1);
            Press(body, category, clickCount: 2);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.True(body.IsCollapsed(category), "a double-click folds exactly once");
            Assert.Equal(0, activated);

            // The list is updated in place: replacing it mid-press lost the
            // selection and the capture of the heading under the pointer.
            Assert.Same(rows, body.List.ItemsSource);
            Assert.Equal(category, ((ToolboxHeading)body.List.SelectedItem!).Name);
        }
        finally { window.Close(); }
    }

    private static void Press(ToolboxList body, string category, int clickCount)
    {
        var name = body.List.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == category);
        var row = (Control)name.GetVisualParent()!;
        body.List.SelectedItem = body.List.ItemsSource!.OfType<ToolboxHeading>().First(h => h.Name == category);
        row.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(
            row,
            new Avalonia.Input.Pointer(0, Avalonia.Input.PointerType.Mouse, isPrimary: true),
            row,
            new Point(4, 4),
            timestamp: 0,
            new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
            Avalonia.Input.KeyModifiers.None,
            clickCount));
    }

    [AvaloniaFact]
    public async Task TheFormsToolboxIsGroupedFilterableAndStillAddsOnEnter()
    {
        var document = new Basalt.Shell.ViewModels.EditorDocumentViewModel(
            "MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();

        try
        {
            var toolbox = Assert.IsType<ToolboxList>(view.ToolboxContent);
            var headings = toolbox.List.ItemsSource!.OfType<ToolboxHeading>().Select(h => h.Name).ToList();

            Assert.Equal(WinFormsToolboxCatalog.CategoryOrder.Where(headings.Contains), headings);
            Assert.Contains("Common Controls", headings);
            Assert.Contains("Components", headings);

            toolbox.FilterText = "button";
            var shown = toolbox.List.ItemsSource!.OfType<WinFormsToolboxItem>().ToList();
            Assert.Contains(shown, i => i.ControlType == typeof(System.Windows.Forms.Button));
            Assert.DoesNotContain(shown, i => i.ControlType == typeof(System.Windows.Forms.Label));

            // Wait for the designer to load the form before pressing keys.
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            toolbox.FilterText = "";

            // A stale selection must not decide what Enter inserts: Label is
            // selected in the list while the filter's first match is ProgressBar.
            toolbox.List.SelectedItem = toolbox.List.ItemsSource!.OfType<WinFormsToolboxItem>()
                .First(i => i.ControlType == typeof(System.Windows.Forms.Label));
            toolbox.FilterText = "progress";
            var first = toolbox.List.ItemsSource!.OfType<WinFormsToolboxItem>().First();
            Assert.Equal(typeof(System.Windows.Forms.ProgressBar), first.ControlType);

            toolbox.Filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => document.Text.Contains("ProgressBar1", StringComparison.Ordinal));
            Assert.DoesNotContain("Label1", document.Text);

            // No match, nothing to insert: Enter leaves the form alone.
            var before = document.Text;
            toolbox.FilterText = "zzzz";
            toolbox.Filter.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(before, document.Text);
        }
        finally { window.Close(); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not finish the requested operation.");
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void RowsAndHeadingsReadInTheThemeTheyAreIn(string variant)
    {
        // A heading drawn in a fixed colour vanishes on one of the two
        // surfaces; the text must have real contrast against the panel.
        using var scope = new ThemeScope(variant == "Dark" ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light);
        var panel = new ToolboxPanel();
        var window = new Window { Content = panel, Width = 260, Height = 600 };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        try
        {
            Assert.True(window.TryFindResource("SideBarBackgroundBrush", window.ActualThemeVariant, out var side));
            var background = ((ISolidColorBrush)side!).Color;
            var heading = panel.Body.List.GetVisualDescendants().OfType<TextBlock>()
                .First(t => t.FontWeight == FontWeight.Bold);

            var raster = Raster.Of(window);
            var area = Raster.BoundsIn(heading, window);

            Assert.True(raster.BestContrastAgainst(area, background) >= 4.5,
                $"the heading is unreadable in {variant}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryHeadingIsDrawnAsBoldAsTheWeightItAsksFor()
    {
        // The first heading of the Forms toolbox, "Common Controls", came out
        // regular while the others were bold, though its FontWeight read
        // SemiBold: the first semi-bold run drawn in a process fell back to
        // the regular face. So each heading is rasterised in the list and
        // again alone in a window, and the two must leave the same ink.
        // (Alone in a process this is the first such text drawn, which is
        // where the glitch shows; later in a run the font is already warm.)
        var document = new Basalt.Shell.ViewModels.EditorDocumentViewModel(
            "MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1000, Height = 700 };
        window.Show();
        window.UpdateLayout();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        try
        {
            var toolbox = Assert.IsType<ToolboxList>(view.ToolboxContent);
            var raster = Raster.Of(window);
            var headings = toolbox.List.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.Text is "Common Controls" or "Containers").ToList();
            Assert.Equal(2, headings.Count);

            foreach (var heading in headings)
            {
                var inList = Ink(raster, Raster.BoundsIn(heading, window));

                var alone = new TextBlock
                {
                    Text = heading.Text, FontSize = heading.FontSize, FontWeight = heading.FontWeight,
                    FontFamily = heading.FontFamily, Foreground = heading.Foreground, Background = Brushes.White,
                };
                var aloneWindow = new Window { Content = alone, Width = 300, Height = 50 };
                aloneWindow.Show();
                aloneWindow.UpdateLayout();
                var aloneRaster = Raster.Of(alone);
                var reference = Ink(aloneRaster, new PixelRect(0, 0, (int)heading.Bounds.Width, (int)heading.Bounds.Height));
                aloneWindow.Close();

                Assert.True(Math.Abs(inList - reference) / reference < 0.1,
                    $"\"{heading.Text}\" leaves {inList:F0} ink in the list but {reference:F0} drawn alone");
            }
        }
        finally { window.Close(); }
    }

    /// <summary>Total distance from white, so a heavier weight reads as a larger number.</summary>
    private static double Ink(Raster raster, PixelRect area)
    {
        var sum = 0.0;
        for (var y = area.Y; y < area.Bottom; y++)
            for (var x = area.X; x < area.Right; x++)
                sum += Raster.Contrast(raster.At(x, y), Colors.White) - 1;
        return sum;
    }

    private static void Press(Control target, Key key) =>
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
}
