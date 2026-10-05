using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell.Controls;

namespace Basalt.Tests;

public class PropertyGridVisualStudioTests
{
    private static DesignableProperty Property(string name, string? value = "Before") =>
        new(name, typeof(string), PropertyEditorKind.Text, PropertyCategory.Common, [], value)
        { CategoryName = "Design", Description = "Description for " + name };

    [AvaloniaFact]
    public void CompactRowsRenderGridLinesSelectionAndDescription()
    {
        var grid = new PropertyGrid { VisualStudioStyle = true, Properties = [Property("Alpha"), Property("Beta")] };
        using var owner = new Owner(grid);
        var row = PropertyRow(grid, "Alpha");
        row.Focus();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Alpha", grid.SelectedPropertyName);
        Assert.Equal(WinFormsPropertyGridPalette.RowHeight, row.Bounds.Height);
        var label = row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == "Alpha");
        var selected = label.TranslatePoint(new Point(100, 10), owner.Window)!.Value;
        var divider = row.TranslatePoint(new Point(121, 8), owner.Window)!.Value;
        var bottom = row.TranslatePoint(new Point(90, WinFormsPropertyGridPalette.RowHeight - 1), owner.Window)!.Value;
        var palette = WinFormsPropertyGridPalette.For(grid);
        AssertPixel(owner.Window, selected, palette.SelectionBackgroundColor);
        AssertPixel(owner.Window, divider, palette.GridLineColor);
        AssertPixel(owner.Window, bottom, palette.GridLineColor);
        var description = grid.GetVisualDescendants().OfType<Border>().Single(border => border.Name == "PropertyGridDescription");
        Assert.Contains(description.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Description for Alpha");
        Assert.DoesNotContain(grid.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_Reset");
    }

    [AvaloniaFact]
    public void KeyboardCommitsOnceCancelsAndNavigatesAndContextResetRemovesAssignment()
    {
        var grid = new PropertyGrid { VisualStudioStyle = true, Properties = [Property("Alpha"), Property("Beta")] };
        var edits = new List<(string Name, string? Value)>();
        grid.PropertyEdited += (name, value) => edits.Add((name, value));
        using var owner = new Owner(grid);
        var row = PropertyRow(grid, "Alpha");
        var editor = row.GetVisualDescendants().OfType<TextBox>().Single();
        editor.Text = "Canceled";
        Key(editor, Avalonia.Input.Key.Escape);
        Assert.Equal("Before", editor.Text);
        editor.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.Empty(edits);
        editor.Text = "Committed";
        Key(editor, Avalonia.Input.Key.Enter);
        editor.RaiseEvent(new FocusChangedEventArgs(InputElement.LostFocusEvent));
        Assert.Equal([("Alpha", "Committed")], edits);
        row.Focus();
        Key(row, Avalonia.Input.Key.Down);
        Assert.Equal("Beta", grid.SelectedPropertyName);
        Key(PropertyRow(grid, "Beta"), Avalonia.Input.Key.Up);
        Assert.Equal("Alpha", grid.SelectedPropertyName);
        var reset = Assert.IsType<MenuItem>(Assert.Single(row.ContextMenu!.ItemsSource!.Cast<object>()));
        reset.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Assert.Equal(("Alpha", (string?)null), edits[1]);
    }

    [AvaloniaFact]
    public void BooleanDropdownEditorsReadOnlyRowsAndNestedEditsUseSeparateRoutes()
    {
        var grid = new PropertyGrid
        {
            VisualStudioStyle = true,
            CanRequestPropertyEditor = property => property.Name is "Font" or "Items",
            Properties =
            [
                Property("Enabled", "True") with { ValueType = typeof(bool), EditorKind = PropertyEditorKind.Boolean },
                Property("Font") with { IsExpandable = true, HasCustomEditor = true },
                Property("Size", "12") with { ParentName = "Font", Depth = 1 },
                Property("ReadOnly") with { IsReadOnly = true },
                Property("Items") with { IsCollection = true, HasCustomEditor = true },
            ],
        };
        var edits = new List<string?>();
        grid.PropertyEdited += (_, value) => edits.Add(value);
        DesignableProperty? custom = null;
        grid.PropertyEditorRequested += property => custom = property;
        DesignableProperty? nested = null;
        grid.NestedPropertyEdited += (property, _) => nested = property;
        using var owner = new Owner(grid);
        var enabled = PropertyRow(grid, "Enabled");
        var combo = enabled.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(["True", "False"], combo.ItemsSource!.Cast<string>());
        Key(enabled, Avalonia.Input.Key.F4);
        Assert.True(combo.IsDropDownOpen);
        combo.IsDropDownOpen = false;
        combo.SelectedItem = "False";
        Assert.Equal(["False"], edits);
        var font = PropertyRow(grid, "Font");
        Key(font, Avalonia.Input.Key.F4);
        Assert.Equal("Font", custom!.Name);
        Assert.DoesNotContain(grid.Rows, control => control.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Size"));
        Key(font, Avalonia.Input.Key.Right);
        var size = PropertyRow(grid, "Size").GetVisualDescendants().OfType<TextBox>().Single();
        size.Text = "14";
        Key(size, Avalonia.Input.Key.Enter);
        Assert.Equal("Font", nested!.ParentName);
        Assert.Equal("Size", nested.Name);
        Assert.Empty(PropertyRow(grid, "ReadOnly").GetVisualDescendants().OfType<TextBox>());
        var items = PropertyRow(grid, "Items");
        Assert.Empty(items.GetVisualDescendants().OfType<TextBox>());
        items.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Name == "PropertyGridEllipsis")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Items", custom!.Name);
    }

    [AvaloniaFact]
    public void ToolbarSortsCollapsesCategoriesAndSwitchesEventsBody()
    {
        var events = new TextBlock { Text = "Events body" };
        var grid = new PropertyGrid
        {
            VisualStudioStyle = true,
            EventsContent = events,
            Properties = [Property("Zulu"), Property("Alpha") with { CategoryName = "Appearance" }],
        };
        using var owner = new Owner(grid);
        var header = grid.Rows.OfType<Button>().Single(button => button.Content?.ToString()?.Contains("Design", StringComparison.Ordinal) == true);
        header.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.DoesNotContain(grid.Rows, row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Zulu"));
        Click(grid, "PropertyGridAlphabetical");
        Assert.Equal(PropertyGrid.SortOrder.Alphabetical, grid.Order);
        Assert.Equal(["Alpha", "Zulu"], grid.Rows.Select(row => row.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text is "Alpha" or "Zulu").Text));
        Click(grid, "PropertyGridEvents");
        Assert.True(grid.EventsMode);
        Assert.Contains(events, grid.GetVisualDescendants());
        Click(grid, "PropertyGridProperties");
        Assert.False(grid.EventsMode);
        Assert.Contains(grid.GetVisualDescendants().OfType<Border>(), border => border.Name == "PropertyGridRow");
    }

    private static Border PropertyRow(PropertyGrid grid, string name) => grid.Rows.OfType<Border>()
        .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == name));
    private static void Key(Control control, Key key) => control.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });
    private static void Click(PropertyGrid grid, string name) => grid.GetVisualDescendants().OfType<Button>()
        .Single(button => button.Name == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void AssertPixel(Window window, Point point, Color expected)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(360, 260));
        bitmap.Render(window);
        bitmap.Save(Path.Combine(Path.GetTempPath(), "basalt-vs-property-grid.png"), new PngBitmapEncoderOptions());
        var bytes = new byte[360 * 260 * 4];
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            bitmap.CopyPixels(new PixelRect(0, 0, 360, 260), pin.AddrOfPinnedObject(), bytes.Length, 360 * 4);
        }
        finally
        {
            pin.Free();
        }
        var offset = ((int)point.Y * 360 + (int)point.X) * 4;
        Assert.Equal(expected.R, bytes[offset]);
        Assert.Equal(expected.G, bytes[offset + 1]);
        Assert.Equal(expected.B, bytes[offset + 2]);
    }

    private sealed class Owner : IDisposable
    {
        public Window Window { get; }
        public Owner(PropertyGrid grid)
        {
            Window = new Window
            {
                Content = grid,
                Width = 360,
                Height = 260,
                RequestedThemeVariant = ThemeVariant.Light,
            };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }
        public void Dispose() => Window.Close();
    }
}
