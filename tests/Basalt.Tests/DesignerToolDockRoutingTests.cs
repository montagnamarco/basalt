using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Basalt.Designer.Toolbox;
using Basalt.Shell;
using Basalt.Shell.Controls;
using Basalt.Shell.Docking;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public sealed class DesignerToolDockRoutingTests
{
    [AvaloniaFact]
    public async Task DockPanelsFollowFrameworkAndClearForCodeWithoutReplacingDockHosts()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-dock-routing-").FullName;
        try
        {
            var formsPath = Path.Combine(folder, "MainForm.Designer.vb");
            var avaloniaPath = Path.Combine(folder, "MainWindow.axaml");
            var codePath = Path.Combine(folder, "Module1.vb");
            await File.WriteAllTextAsync(formsPath, WinFormsDesignerDocumentTests.Source);
            await File.WriteAllTextAsync(avaloniaPath, """
                <Window xmlns="https://github.com/avaloniaui">
                    <StackPanel><Button Content="Hello" /></StackPanel>
                </Window>
                """);
            await File.WriteAllTextAsync(codePath, "Module Module1\nEnd Module");
            using var window = new TestWindow();
            var model = Assert.IsType<MainWindowViewModel>(window.Window.DataContext);
            var toolbox = DockHost(window.Window, "_designerToolboxDock");
            var properties = DockHost(window.Window, "_designerPropertiesDock");
            var events = DockHost(window.Window, "_designerEventsDock");

            await model.OpenFileAsync(formsPath, inDesigner: true);
            await window.SettleAsync();
            // The Forms toolbox: the shared filter-and-categories control
            // listing Forms controls (the Avalonia one is a ToolboxPanel).
            var formsToolbox = Assert.IsType<ToolboxList>(toolbox.Content);
            Assert.Contains(formsToolbox.List.ItemsSource!.Cast<object>(), row => row is WinFormsToolboxItem);
            var formsProperties = Assert.IsType<WinFormsPropertyPanel>(properties.Content);
            var formsEvents = Assert.IsType<WinFormsEventPanel>(events.Content);
            var formsHost = Assert.Single(window.Window.GetVisualDescendants().OfType<DesignerHost>());
            Assert.DoesNotContain(formsHost.GetVisualDescendants(), control => ReferenceEquals(control, formsProperties));
            Assert.DoesNotContain(formsHost.GetVisualDescendants(), control => ReferenceEquals(control, formsEvents));

            var surface = Assert.Single(formsHost.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var textRow = Assert.Single(formsProperties.GetVisualDescendants().OfType<Border>(), row =>
                row.Name == "PropertyGridRow"
                && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Text"));
            var editor = Assert.Single(textRow.GetVisualDescendants().OfType<TextBox>());
            editor.Text = "Edited from IDE dock";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await window.SettleAsync();
            Assert.Equal("Edited from IDE dock", surface.SelectedControl!.Text);
            model.UndoCommand.Execute(null);
            await window.SettleAsync();
            Assert.Equal("Before", surface.SelectedControl!.Text);
            model.RedoCommand.Execute(null);
            await window.SettleAsync();
            Assert.Equal("Edited from IDE dock", surface.SelectedControl!.Text);

            var imageSize = new PixelSize((int)Math.Ceiling(window.Window.Bounds.Width),
                (int)Math.Ceiling(window.Window.Bounds.Height));
            using (var screenshot = new RenderTargetBitmap(imageSize))
            {
                screenshot.Render(window.Window);
                screenshot.Save(Path.Combine(Path.GetTempPath(), "basalt-dockable-winforms-designer.png"),
                    new PngBitmapEncoderOptions());
            }

            formsHost.ShowText();
            Assert.Null(toolbox.Content);
            Assert.Null(properties.Content);
            Assert.Null(events.Content);
            Assert.Null(model.DesignerUndoRequested);
            Assert.Null(model.DesignerRedoRequested);
            formsHost.ShowDesigner();
            Assert.Same(formsProperties, properties.Content);
            Assert.Same(formsEvents, events.Content);

            await model.OpenFileAsync(avaloniaPath, inDesigner: true);
            await window.SettleAsync();
            Assert.IsType<ToolboxPanel>(toolbox.Content);
            Assert.IsType<PropertyPanel>(properties.Content);
            Assert.IsType<EventsPanel>(events.Content);
            Assert.Same(toolbox, DockHost(window.Window, "_designerToolboxDock"));
            Assert.Same(properties, DockHost(window.Window, "_designerPropertiesDock"));

            var formsDocument = Assert.Single(model.OpenDocuments, document => document.FilePath == formsPath);
            model.ActivateFromTabSelection(formsDocument);
            await window.SettleAsync();
            Assert.Same(formsProperties, properties.Content);
            Assert.Same(formsEvents, events.Content);

            await model.OpenFileAsync(codePath);
            await window.SettleAsync();
            Assert.Null(toolbox.Content);
            Assert.Null(properties.Content);
            Assert.Null(events.Content);
            Assert.Null(model.DesignerUndoRequested);
            Assert.Null(model.DesignerRedoRequested);

            model.ActivateFromTabSelection(formsDocument);
            await window.SettleAsync();
            model.CloseActiveDocument();
            await window.SettleAsync();
            Assert.DoesNotContain(formsDocument, model.OpenDocuments);
            Assert.IsNotType<WinFormsPropertyPanel>(properties.Content);
            Assert.IsNotType<WinFormsEventPanel>(events.Content);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [AvaloniaFact]
    public async Task F4ReopensAClosedPropertiesDockAndFocusesItsSelectedRow()
    {
        var folder = Directory.CreateTempSubdirectory("basalt-property-focus-").FullName;
        try
        {
            var path = Path.Combine(folder, "MainForm.Designer.vb");
            await File.WriteAllTextAsync(path, WinFormsDesignerDocumentTests.Source);
            using var window = new TestWindow();
            var model = Assert.IsType<MainWindowViewModel>(window.Window.DataContext);
            await model.OpenFileAsync(path, inDesigner: true);
            await window.SettleAsync();
            var host = Assert.Single(window.Window.GetVisualDescendants().OfType<DesignerHost>());
            var surface = Assert.Single(host.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var factoryField = typeof(MainWindow).GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var factory = Assert.IsType<IdeDockFactory>(factoryField.GetValue(window.Window));
            factory.CloseDockable(factory.Properties);
            await window.SettleAsync();
            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4 });
            host.ShowText();
            host.ShowDesigner();
            Assert.True(surface.Focus());
            await window.SettleAsync();
            Assert.Same(surface, window.Window.FocusManager!.GetFocusedElement());

            factory.CloseDockable(factory.Properties);
            await window.SettleAsync();
            surface.Focus();
            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4 });
            await window.SettleAsync();
            Assert.Contains(factory.Properties, Assert.IsAssignableFrom<Dock.Model.Core.IDock>(factory.Properties.Owner).VisibleDockables!);
            var focused = Assert.IsAssignableFrom<Control>(window.Window.FocusManager!.GetFocusedElement());
            var properties = Assert.IsType<WinFormsPropertyPanel>(DockHost(window.Window, "_designerPropertiesDock").Content);
            Assert.Contains(focused, properties.GetVisualDescendants());
            Assert.Equal("Button1", surface.SelectedControl!.Name);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static ContentControl DockHost(MainWindow window, string fieldName)
    {
        var field = typeof(MainWindow).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsType<ContentControl>(field.GetValue(window));
    }
}
