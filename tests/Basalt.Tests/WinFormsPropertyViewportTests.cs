using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class WinFormsPropertyViewportTests
{
    [AvaloniaFact]
    public async Task EditingAPropertyRetainsTheSelectedRowAndScrollOffset()
    {
        var document = new EditorDocumentViewModel(
            "MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 950, Height = 440 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);

            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var scroll = Assert.Single(grid.GetVisualDescendants().OfType<ScrollViewer>());
            await WaitUntilAsync(() => scroll.Extent.Height > scroll.Viewport.Height + 100);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var row = FindRow(grid, "Text");
            var editor = Assert.Single(row.GetVisualDescendants().OfType<TextBox>(), box =>
                box.Name == "PropertyGridValue");
            editor.Focus();
            scroll.Offset = new Vector(0, 90);
            Dispatcher.UIThread.RunJobs();
            var before = scroll.Offset.Y;
            Assert.True(before > 20, "the property list must actually be scrollable");

            editor.Text = "Edited";
            editor.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Enter,
            });
            await WaitUntilAsync(() => document.Text.Contains("Me.Button1.Text = \"Edited\"", StringComparison.Ordinal));
            Dispatcher.UIThread.RunJobs();

            Assert.InRange(scroll.Offset.Y, before - 1, before + 1);
            Assert.Equal("Text", grid.SelectedPropertyName);
            Assert.True(FindRow(grid, "Text").IsFocused);
        }
        finally
        {
            window.Close();
        }
    }

    private static Border FindRow(PropertyGrid grid, string name) =>
        Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The designer did not finish the property operation.");
    }
}
