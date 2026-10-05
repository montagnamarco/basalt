using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Shell;

namespace Basalt.Tests;

/// <summary>
/// Where the keyboard is, in lists, trees and toolbars.
///
/// Nothing in the IDE drew keyboard focus: the FocusBorder token was read by
/// no panel, so tabbing through a toolbar or arrowing down a list left the
/// user guessing which row would act. Rasterised, because a style that
/// resolves is not a style that draws.
/// </summary>
public sealed class PanelFocusTests : IDisposable
{
    private ThemeScope? _theme;

    /// <summary>The application's variant for this test, restored when it ends.</summary>
    private void UseTheme(ThemeVariant variant)
    {
        _theme?.Dispose();
        _theme = new ThemeScope(variant);
    }

    public void Dispose() => _theme?.Dispose();

    private static Color Token(Window window, string key)
    {
        Assert.True(window.TryFindResource(key, window.ActualThemeVariant, out var value), key);
        return (Color)value!;
    }

    private Window ShowList(ThemeVariant variant, out ListBox list)
    {
        list = new ListBox { ItemsSource = new[] { "alpha", "beta", "gamma" } };
        var other = new Button { Content = "elsewhere" };
        // The application's variant, not the window's: the brushes are
        // application resources and resolve their colours against it.
        UseTheme(variant);
        var window = new Window
        {
            Width = 300, Height = 200,
            Content = new StackPanel { Children = { list, other } }
        };

        window.Show();
        window.UpdateLayout();

        return window;
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ARowReachedByKeyboardIsOutlinedInTheFocusColour(string name)
    {
        var window = ShowList(name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, out var list);

        try
        {
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
            row.Focus(NavigationMethod.Tab);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var raster = Raster.Of(window);
            var bounds = Raster.BoundsIn(row, window);
            var focus = Token(window, "FocusBorder");

            // Along the row's top edge, away from the corners.
            var onEdge = Enumerable.Range(bounds.X + 8, 20)
                .Any(x => Raster.Near(raster.At(x, bounds.Y), focus, 6)
                       || Raster.Near(raster.At(x, bounds.Y + 1), focus, 6));

            Assert.True(onEdge, $"{name}: the row with keyboard focus has no outline in {focus}.");

            // And only that row: the one above is not outlined.
            var above = Raster.BoundsIn(list.GetVisualDescendants().OfType<ListBoxItem>().First(), window);
            var strayEdge = Enumerable.Range(above.X + 8, 20)
                .Any(x => Raster.Near(raster.At(x, above.Y + 1), focus, 6));

            Assert.False(strayEdge, $"{name}: a row without focus is outlined too.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ASelectedRowIsFilledWithTheActiveColourOnlyWhileTheListHasFocus(string name)
    {
        var window = ShowList(name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, out var list);

        try
        {
            list.SelectedIndex = 1;
            var row = list.GetVisualDescendants().OfType<ListBoxItem>().ElementAt(1);
            var other = window.GetVisualDescendants().OfType<Button>().Single();

            other.Focus();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var bounds = Raster.BoundsIn(row, window);
            var inactive = Raster.Of(window).At(bounds.Right - 10, bounds.Y + bounds.Height / 2);

            Assert.True(Raster.Near(inactive, Token(window, "ListInactiveSelectionBackground"), 4),
                $"{name}: an unfocused list shows its selection as {inactive}.");

            row.Focus();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var active = Raster.Of(window).At(bounds.Right - 10, bounds.Y + bounds.Height / 2);

            Assert.True(Raster.Near(active, Token(window, "ListActiveSelectionBackground"), 4),
                $"{name}: a focused list shows its selection as {active}.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ARowUnderThePointerGetsTheThemesHoverColour(string name)
    {
        var window = ShowList(name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light, out var list);

        try
        {
            // The keys Fluent's rows read; hover itself cannot be produced
            // without a pointer, so what is checked is what the row will use.
            window.TryFindResource("ListBoxItemBackgroundPointerOver", window.ActualThemeVariant, out var hover);
            window.TryFindResource("TreeViewItemBackgroundPointerOver", window.ActualThemeVariant, out var treeHover);

            Assert.Equal(Token(window, "ListHoverBackground"), ((ISolidColorBrush)hover!).Color);
            Assert.Equal(Token(window, "ListHoverBackground"), ((ISolidColorBrush)treeHover!).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void ASelectedTreeRowFollowsTheSameColoursAsAListRow(string name)
    {
        UseTheme(name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var tree = new TreeView { ItemsSource = new[] { "alpha", "beta", "gamma" }, SelectedItem = "beta" };
        var other = new Button { Content = "elsewhere" };
        var window = new Window
        {
            Width = 300, Height = 200,
            Content = new StackPanel { Children = { tree, other } }
        };

        window.Show();

        try
        {
            other.Focus();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var row = tree.GetVisualDescendants().OfType<TreeViewItem>().ElementAt(1);
            var bounds = Raster.BoundsIn(row, window);
            var spot = (bounds.Right - 10, bounds.Y + bounds.Height / 2);

            Assert.True(Raster.Near(Raster.Of(window).At(spot.Item1, spot.Item2),
                Token(window, "ListInactiveSelectionBackground"), 4), $"{name}: unfocused tree selection.");

            row.Focus();
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Assert.True(Raster.Near(Raster.Of(window).At(spot.Item1, spot.Item2),
                Token(window, "ListActiveSelectionBackground"), 4), $"{name}: focused tree selection.");
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void AToolbarButtonReachedByTabIsOutlined(string name)
    {
        UseTheme(name == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light);
        var button = new Button { Content = "Run", Margin = new Thickness(20) };
        var window = new Window { Width = 200, Height = 100, Content = button };

        window.Show();

        try
        {
            button.Focus(NavigationMethod.Tab);
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var bounds = Raster.BoundsIn(button, window);
            var raster = Raster.Of(window);
            var focus = Token(window, "FocusBorder");

            Assert.True(Enumerable.Range(bounds.X + 6, 12)
                .Any(x => Raster.Near(raster.At(x, bounds.Y), focus, 6)
                       || Raster.Near(raster.At(x, bounds.Y + 1), focus, 6)),
                $"{name}: the focused toolbar button has no outline.");
        }
        finally { window.Close(); }
    }
}
