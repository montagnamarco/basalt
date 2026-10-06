using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public sealed class WinFormsDesignerDockPanelTests
{
    [AvaloniaFact]
    public async Task ExternalToolPanelsFollowCodeAndDesignWithoutLosingSelectionOrUndo()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        WinFormsDesignerView? view = null;
        using var host = new DesignerHost(document,
            () => view = new WinFormsDesignerView(document, dockTools: true),
            () => new TextBox());
        var toolboxDock = new ContentControl();
        var propertiesDock = new ContentControl();
        var eventsDock = new ContentControl();
        var window = CreateWindow(host, toolboxDock, propertiesDock, eventsDock);

        void RefreshDocks()
        {
            toolboxDock.Content = host.ShowingDesigner ? view!.ToolboxContent : null;
            propertiesDock.Content = host.ShowingDesigner ? view!.PropertiesContent : null;
            eventsDock.Content = host.ShowingDesigner ? view!.EventsContent : null;
        }

        host.ViewChanged += (_, _) => RefreshDocks();
        RefreshDocks();
        window.Show();

        try
        {
            var designer = Assert.IsType<WinFormsDesignerView>(view);
            var surface = designer.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            var toolbox = Assert.IsType<ToolboxList>(toolboxDock.Content);
            Assert.Contains(toolbox.List.ItemsSource!.Cast<object>(), row => row is WinFormsToolboxItem);
            var properties = Assert.IsType<WinFormsPropertyPanel>(propertiesDock.Content);
            Assert.IsType<WinFormsEventPanel>(eventsDock.Content);
            Assert.Null(properties.EventsContent);
            Assert.DoesNotContain(toolbox, host.GetVisualDescendants());
            Assert.DoesNotContain(properties, host.GetVisualDescendants());
            Assert.DoesNotContain(eventsDock.Content, host.GetVisualDescendants());

            var eventRequests = 0;
            designer.EventsPanelRequested += () => eventRequests++;
            var lightning = properties.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "PropertyGridEvents");
            lightning.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, eventRequests);
            Assert.False(properties.GetVisualDescendants().OfType<PropertyGrid>().Single().EventsMode);
            Assert.Same(designer.EventsContent, eventsDock.Content);

            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            Assert.Equal("Button1", surface.SelectedControl!.Name);
            var session = SessionOf(designer);
            var row = properties.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "PropertyGridRow" &&
                    border.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Text"));
            var editor = row.GetVisualDescendants().OfType<TextBox>()
                .Single(box => box.Name == "PropertyGridValue");
            editor.Text = "Edited from dock";
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
            await WaitUntilAsync(() => document.Text.Contains("Me.Button1.Text = \"Edited from dock\"", StringComparison.Ordinal));
            Assert.Equal("Edited from dock", surface.SelectedControl!.Text);

            // This exercises visual-tree reparenting used by a floating dock.
            // This does not exercise the Dock factory's native drag gesture.
            var mainLayout = Assert.IsType<Grid>(window.Content);
            var right = Assert.IsType<Grid>(mainLayout.Children[2]);
            mainLayout.Children.Remove(toolboxDock);
            right.Children.Remove(propertiesDock);
            right.Children.Remove(eventsDock);
            var floatingLayout = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180,300,300"),
                Children = { toolboxDock, propertiesDock, eventsDock },
            };
            Grid.SetColumn(propertiesDock, 1);
            Grid.SetColumn(eventsDock, 2);
            var floating = new Window
            {
                Content = floatingLayout,
                Width = 800,
                Height = 400,
            };
            try
            {
                floating.Show();
                Dispatcher.UIThread.RunJobs();
                Assert.Same(session, SessionOf(designer));
                Assert.Equal("Button1", surface.SelectedControl!.Name);
                Assert.Same(toolbox, toolboxDock.Content);
                Assert.Same(properties, propertiesDock.Content);
                Assert.Same(designer.EventsContent, eventsDock.Content);
            }
            finally
            {
                floatingLayout.Children.Clear();
                floating.Close();
                mainLayout.Children.Add(toolboxDock);
                right.Children.Add(propertiesDock);
                right.Children.Add(eventsDock);
                Grid.SetColumn(propertiesDock, 0);
                Grid.SetColumn(eventsDock, 0);
                Grid.SetRow(eventsDock, 1);
            }

            host.ShowText();
            Assert.Null(toolboxDock.Content);
            Assert.Null(propertiesDock.Content);
            Assert.Null(eventsDock.Content);
            Assert.Same(session, SessionOf(designer));
            host.ShowDesigner();
            await WaitUntilAsync(() => surface.RootForm is not null);
            Assert.Same(session, SessionOf(designer));
            Assert.Equal("Button1", surface.SelectedControl!.Name);
            Assert.Same(toolbox, toolboxDock.Content);
            Assert.Same(properties, propertiesDock.Content);
            Assert.Same(designer.EventsContent, eventsDock.Content);

            ClickUndo(designer);
            await WaitUntilAsync(() => document.Text == original);
        }
        finally
        {
            toolboxDock.Content = null;
            propertiesDock.Content = null;
            eventsDock.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ToolboxOutsideTheDocumentStillAddsOneControlWithOneUndo()
    {
        var original = WinFormsDesignerDocumentTests.Source;
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        WinFormsDesignerView? view = null;
        using var host = new DesignerHost(document,
            () => view = new WinFormsDesignerView(document, dockTools: true),
            () => new TextBox());
        var toolboxDock = new ContentControl { Content = view!.ToolboxContent };
        var window = new Window
        {
            Content = new DockPanel { Children = { toolboxDock, host } },
            Width = 1100,
            Height = 700,
        };
        DockPanel.SetDock(toolboxDock, Avalonia.Controls.Dock.Left);
        window.Show();

        try
        {
            var designer = Assert.IsType<WinFormsDesignerView>(view);
            var surface = designer.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0]);
            var toolbox = Assert.IsType<ToolboxList>(toolboxDock.Content).List;
            toolbox.SelectedItem = toolbox.ItemsSource!.OfType<WinFormsToolboxItem>()
                .Single(item => item.ControlType == typeof(Forms.Label));
            toolbox.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            await WaitUntilAsync(() => document.Text.Contains("Friend WithEvents Label1", StringComparison.Ordinal));
            Assert.Equal("Panel1", surface.SelectedControl!.Parent!.Name);
            Assert.Equal("Label1", surface.SelectedControl.Name);

            ClickUndo(designer);
            await WaitUntilAsync(() => document.Text == original);
        }
        finally
        {
            toolboxDock.Content = null;
            window.Close();
        }
    }

    private static Window CreateWindow(DesignerHost host, ContentControl toolbox,
        ContentControl properties, ContentControl events)
    {
        var right = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"),
            Children = { properties, events },
        };
        Grid.SetRow(events, 1);
        var layout = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*,300"),
            Children = { toolbox, host, right },
        };
        Grid.SetColumn(host, 1);
        Grid.SetColumn(right, 2);
        return new Window { Content = layout, Width = 1200, Height = 720 };
    }

    private static WinFormsDesignerSession SessionOf(WinFormsDesignerView view)
    {
        var field = typeof(WinFormsDesignerView).GetField("_session",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        return Assert.IsType<WinFormsDesignerSession>(field.GetValue(view));
    }

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = view.GetVisualDescendants().OfType<Button>()
            .Single(button => Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
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

        Assert.True(condition(), "The docked WinForms designer did not settle.");
    }
}
