using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public class PropertyGridVisualStudioThemeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestedThemeRendersMatchingBackgroundGridAndSelection(bool dark)
    {
        var grid = CreateGrid();
        var window = CreateWindow(grid, dark ? ThemeVariant.Dark : ThemeVariant.Light);
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var row = Row(grid, "Font");
            row.Focus();
            Dispatcher.UIThread.RunJobs();
            var palette = WinFormsPropertyGridPalette.For(grid);
            Assert.Equal(dark ? ThemeVariant.Dark : ThemeVariant.Light, grid.ActualThemeVariant);
            AssertPixel(window, new Point(330, 130), palette.BackgroundColor);
            AssertPixel(window, row.TranslatePoint(new Point(90, 8), window)!.Value, palette.SelectionBackgroundColor);
            AssertPixel(window, row.TranslatePoint(new Point(121, 8), window)!.Value, palette.GridLineColor);
            var description = grid.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PropertyGridDescription");
            var text = description.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(palette.ForegroundColor, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LiveThemeSwitchRetainsExpansionSelectionAndDraftWithoutEditingSource()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        var grid = CreateGrid();
        var edits = 0;
        grid.PropertyEdited += (_, _) =>
        {
            edits++;
            document.Text = "Unexpected edit";
        };
        grid.NestedPropertyEdited += (_, _) =>
        {
            edits++;
            document.Text = "Unexpected nested edit";
        };
        var window = CreateWindow(grid, ThemeVariant.Light);
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var font = Row(grid, "Font");
            font.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Right });
            var size = Row(grid, "Size");
            var editor = size.GetVisualDescendants().OfType<TextBox>().Single();
            editor.Focus();
            editor.Text = "Uncommitted draft";
            Assert.Equal("Font.Size", grid.SelectedPropertyName);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            Dispatcher.UIThread.RunJobs();

            var rebuilt = Row(grid, "Size");
            var rebuiltEditor = rebuilt.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.Equal("Font.Size", grid.SelectedPropertyName);
            Assert.Equal("Uncommitted draft", rebuiltEditor.Text);
            Assert.True(rebuiltEditor.IsFocused);
            Assert.Equal(0, edits);
            Assert.Equal(original, document.Text);
            Assert.False(document.IsModified);
            AssertPixel(window, new Point(330, 150), WinFormsPropertyGridPalette.For(grid).BackgroundColor);

            window.RequestedThemeVariant = ThemeVariant.Light;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Font.Size", grid.SelectedPropertyName);
            Assert.Equal("Uncommitted draft", Row(grid, "Size").GetVisualDescendants().OfType<TextBox>().Single().Text);
            Assert.Equal(0, edits);
            Assert.Equal(original, document.Text);
            AssertPixel(window, new Point(330, 150), WinFormsPropertyGridPalette.For(grid).BackgroundColor);
        }
        finally
        {
            window.Close();
        }
    }

    private static PropertyGrid CreateGrid() => new()
    {
        VisualStudioStyle = true,
        Properties =
        [
            new DesignableProperty("Font", typeof(string), PropertyEditorKind.Text, PropertyCategory.Appearance, [], "Default")
            {
                IsExpandable = true,
                Description = "The text font",
            },
            new DesignableProperty("Size", typeof(float), PropertyEditorKind.Number, PropertyCategory.Appearance, [], "12")
            {
                ParentName = "Font",
                Depth = 1,
                Description = "The font size",
            },
        ],
    };

    private static Window CreateWindow(PropertyGrid grid, ThemeVariant theme) => new()
    {
        Content = grid,
        Width = 360,
        Height = 260,
        RequestedThemeVariant = theme,
    };

    private static Border Row(PropertyGrid grid, string name) => grid.Rows.OfType<Border>()
        .Single(row => row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static void AssertPixel(Window window, Point point, Color expected)
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(360, 260));
        bitmap.Render(window);
        var filename = window.ActualThemeVariant == ThemeVariant.Dark
            ? "basalt-vs-property-grid-dark.png"
            : "basalt-vs-property-grid-light.png";
        bitmap.Save(Path.Combine(Path.GetTempPath(), filename), new PngBitmapEncoderOptions());
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
}
