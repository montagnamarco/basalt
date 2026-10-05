using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Basalt.Tests;

public sealed class WinFormsDesignerContextMenuTests
{
    [AvaloniaFact]
    public void RightClickKeepsSelectedGroupAndRetargetsUnselectedControls()
    {
        using var form = new Forms.Form { ClientSize = new Drawing.Size(300, 180) };
        var first = new Forms.Button { Name = "First", Bounds = new Drawing.Rectangle(20, 20, 60, 30) };
        var second = new Forms.Button { Name = "Second", Bounds = new Drawing.Rectangle(110, 20, 60, 30) };
        var third = new Forms.Button { Name = "Third", Bounds = new Drawing.Rectangle(200, 20, 60, 30) };
        form.Controls.AddRange([first, second, third]);
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Content = surface, Width = 400, Height = 260 };
        window.Show();
        try
        {
            surface.SelectControls([first, second]);
            RightClick(window, new Point(20 + 30 + 12, 20 + 15 + 12));
            Assert.Equal(new[] { first, second }, surface.SelectedControls);
            var delete = Item(surface, "WinFormsDesigner_DeleteSelection");
            Assert.True(delete.IsEnabled);
            Assert.True(Item(surface, "WinFormsDesigner_AlignSelection").IsEnabled);
            var deleted = new List<Forms.Control>();
            surface.DeleteRequested += deleted.Add;
            delete.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Single(deleted);
            Assert.Same(second, deleted[0]);
            surface.ContextMenu!.Close();

            RightClick(window, new Point(200 + 30 + 12, 20 + 15 + 12));
            Assert.Same(third, Assert.Single(surface.SelectedControls));
            Assert.False(Item(surface, "WinFormsDesigner_AlignSelection").IsEnabled);
            surface.ContextMenu!.Close();

            RightClick(window, new Point(290 + 12, 150 + 12));
            Assert.Same(form, surface.SelectedControl);
            Assert.False(Item(surface, "WinFormsDesigner_DeleteSelection").IsEnabled);
        }
        finally
        {
            surface.ContextMenu?.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ContextActionsChangeGridModesAndRequestPropertiesWithoutEditingTheForm()
    {
        using var form = new Forms.Form { ClientSize = new Drawing.Size(300, 180) };
        var surface = new WinFormsDesignSurface { RootForm = form };
        var window = new Window { Content = surface, Width = 400, Height = 260 };
        window.Show();
        try
        {
            surface.Select(form);
            var before = form.Bounds;
            var requests = 0;
            surface.PropertiesRequested += () => requests++;
            var codeRequests = 0;
            surface.CodeRequested += () => codeRequests++;
            surface.ContextMenu!.Open(surface);
            Click(surface, "WinFormsDesigner_ViewCode");
            Assert.Equal(1, codeRequests);
            Click(surface, "WinFormsDesigner_Properties");
            Click(surface, "WinFormsDesigner_ShowGrid");
            Click(surface, "WinFormsDesigner_SnapToGrid");
            Click(surface, "WinFormsDesigner_SnapToControls");
            Assert.Equal(1, requests);
            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.F4 });
            Assert.Equal(2, requests);
            Assert.False(surface.ShowGrid);
            Assert.True(surface.SnapToGrid);
            Assert.False(surface.SnapToControls);
            Assert.Equal(before, form.Bounds);
        }
        finally
        {
            surface.ContextMenu?.Close();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ContextDeleteCommitsTheWholeSelectionWithOneUndo()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var controls = surface.RootForm!.Controls[0].Controls.Cast<Forms.Control>().ToArray();
            surface.SelectControls(controls);
            surface.ContextMenu!.Open(surface);
            await WaitUntilAsync(() => surface.ContextMenu.IsOpen);
            Assert.False(Item(surface, "WinFormsDesigner_ViewCode").IsEnabled);
            Click(surface, "WinFormsDesigner_DeleteSelection");
            await WaitUntilAsync(() => !document.Text.Contains("Friend WithEvents Button1", StringComparison.Ordinal));
            Assert.DoesNotContain("Friend WithEvents Button2", document.Text);
            Assert.Empty(surface.RootForm!.Controls[0].Controls);
            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == source);
            Assert.Equal(2, surface.RootForm!.Controls[0].Controls.Count);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 150 && !condition(); attempt++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition());
    }

    private static void RightClick(Window window, Point point)
    {
        window.MouseDown(point, MouseButton.Right);
        window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static MenuItem Item(WinFormsDesignSurface surface, string key) =>
        Assert.Single(surface.ContextMenu!.ItemsSource!.OfType<MenuItem>(),
            item => Equals(item.Header, Localizer.Get(key)));

    private static void Click(WinFormsDesignSurface surface, string key) =>
        Item(surface, key).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
}
