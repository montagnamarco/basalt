using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsPropertyPanelIntegrationTests
{
    [AvaloniaFact]
    public async Task ObjectSelectorSwitchesBetweenVisualControlAndComponentTray()
    {
        var source = Source();
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            var selector = Assert.Single(view.GetVisualDescendants().OfType<ComboBox>(),
                combo => combo.Name == "WinFormsSelectedObject");
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => surface.RootForm is not null
                && selector.ItemsSource?.Cast<WinFormsDesignerObject>().Count() == 4);

            SelectObject(selector, "Button1");
            await WaitUntilAsync(() => surface.SelectedControl?.Name == "Button1");
            Assert.Equal("Button1", Assert.IsType<WinFormsDesignerObject>(selector.SelectedItem).Name);
            Assert.Contains(grid.Properties, property => property.Name == "Size");

            SelectObject(selector, "ContextMenuStrip1");
            await WaitUntilAsync(() => tray.SelectedName == "ContextMenuStrip1");
            Assert.Null(surface.SelectedControl);
            Assert.Equal("ContextMenuStrip1", Assert.IsType<WinFormsDesignerObject>(selector.SelectedItem).Name);
            Assert.Contains(grid.Properties, property => property.Name == "Name");
            Assert.Equal(source, document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ContextMenuStripDropdownAssignsAndClearsAReferenceWithOneUndoPerEdit()
    {
        var source = Source();
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var row = FindRow(grid, "ContextMenuStrip");
            var editor = Assert.Single(row.GetVisualDescendants().OfType<ComboBox>(),
                combo => combo.Name == "PropertyGridValue");
            Assert.Contains("ContextMenuStrip1", editor.ItemsSource!.Cast<string>());
            Assert.Null(surface.SelectedControl!.ContextMenuStrip);

            editor.SelectedItem = "ContextMenuStrip1";
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Button1.ContextMenuStrip = Me.ContextMenuStrip1", StringComparison.Ordinal));
            var assigned = document.Text;
            Assert.IsType<Forms.ContextMenuStrip>(surface.SelectedControl!.ContextMenuStrip);

            editor = Assert.Single(FindRow(grid, "ContextMenuStrip").GetVisualDescendants().OfType<ComboBox>(),
                combo => combo.Name == "PropertyGridValue");
            editor.SelectedItem = Localizer.Get("PropertyGrid_None");
            await WaitUntilAsync(() => document.Text != assigned);
            Assert.Null(surface.SelectedControl!.ContextMenuStrip);

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == assigned);
            Assert.IsType<Forms.ContextMenuStrip>(surface.SelectedControl!.ContextMenuStrip);
            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == source);
            Assert.Null(surface.SelectedControl!.ContextMenuStrip);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExpandedSizeChildRemainsVisibleThroughEditAndUndo()
    {
        var source = Source();
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            var expand = Assert.Single(FindRow(grid, "Size").GetVisualDescendants().OfType<Button>(),
                button => Equals(button.Content, "+"));
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var child = Assert.Single(grid.Properties, property =>
                property.Name == "Width" && property.ParentName == "Size");
            Assert.Equal("90", child.CurrentValue);

            var width = Assert.Single(FindRow(grid, "Width").GetVisualDescendants().OfType<TextBox>(),
                box => box.Name == "PropertyGridValue");
            width.Text = "120";
            width.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => document.Text.Contains(
                "Me.Button1.Size = New System.Drawing.Size(120, 30)", StringComparison.Ordinal));
            Assert.Equal(120, surface.SelectedControl!.Width);
            Assert.Contains(grid.Properties, property => property.Name == "Width" && property.ParentName == "Size");

            ClickUndo(view);
            await WaitUntilAsync(() => document.Text == source);
            Assert.Equal(90, surface.SelectedControl!.Width);
            Assert.Contains(grid.Properties, property => property.Name == "Width" && property.ParentName == "Size");
            Assert.Equal("90", Assert.Single(grid.Properties, property =>
                property.Name == "Width" && property.ParentName == "Size").CurrentValue);
        }
        finally
        {
            window.Close();
        }
    }

    private static string Source() => new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
        .AddComponent("ContextMenuStrip1", "Global.System.Windows.Forms.ContextMenuStrip");

    private static void SelectObject(ComboBox selector, string name)
    {
        selector.SelectedItem = selector.ItemsSource!.Cast<WinFormsDesignerObject>()
            .Single(item => item.Name == name);
    }

    private static Border FindRow(PropertyGrid grid, string name)
    {
        return Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == name));
    }

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
        undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The designer did not finish the property-panel operation.");
    }
}
