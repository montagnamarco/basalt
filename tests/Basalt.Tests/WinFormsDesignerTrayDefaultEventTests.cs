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

namespace Basalt.Tests;

public sealed class WinFormsDesignerTrayDefaultEventTests
{
    [AvaloniaFact]
    public async Task DoubleClickingTimerCreatesTickHandlerOnceAndUndoRestoresCode()
    {
        const string originalCode = "Partial Class MainForm\nEnd Class\n";
        var originalDesigner = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddComponent("Timer1", "Global.System.Windows.Forms.Timer");
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", originalDesigner, true);
        var code = new EditorDocumentViewModel("MainForm.vb", originalCode);
        var openRequests = 0;
        using var view = new WinFormsDesignerView(designer, _ =>
        {
            openRequests++;
            return Task.FromResult(code);
        });
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            await WaitUntilAsync(() => tray.GetVisualDescendants().OfType<ListBox>()
                .SingleOrDefault()?.ItemsSource?.Cast<object>().Count() == 1);
            var list = Assert.Single(tray.GetVisualDescendants().OfType<ListBox>());
            list.SelectedIndex = 0;
            Assert.Equal("Timer1", tray.SelectedName);

            DoubleClick(list);
            await WaitUntilAsync(() => code.Text.Contains("Handles Timer1.Tick", StringComparison.Ordinal));
            await WaitUntilAsync(() => openRequests == 1 && view.IsEnabled);
            var created = code.Text;
            Assert.Contains("Timer1_Tick", created);
            Assert.Equal("Timer1_Tick", WinFormsEventHandlers.Read(created, "MainForm", "Timer1")["Tick"]);
            Assert.Equal(originalDesigner, designer.Text);

            DoubleClick(list);
            await WaitUntilAsync(() => openRequests == 2 && view.IsEnabled);
            Assert.Equal(created, code.Text);
            Assert.Single(WinFormsEventHandlers.Read(code.Text, "MainForm", "Timer1"));

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), item =>
                Equals(item.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => code.Text == originalCode);
            Assert.Equal(originalDesigner, designer.Text);

            var redo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), item =>
                Equals(item.Content, Localizer.Get(StringKeys.MenuEditRedo)));
            redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => code.Text == created);
            Assert.Equal(originalDesigner, designer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ExistingTickHandlerNavigatesAndRefreshesEventsWithoutRewritingCode()
    {
        const string existingCode = """
            Partial Class MainForm
                Private Sub ExistingTick(sender As Object, e As System.EventArgs) Handles Timer1.Tick
                End Sub
            End Class
            """;
        var originalDesigner = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddComponent("Timer1", "Global.System.Windows.Forms.Timer");
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", originalDesigner, true);
        var code = new EditorDocumentViewModel("MainForm.vb", existingCode);
        using var view = new WinFormsDesignerView(designer, _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();

        try
        {
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            await WaitUntilAsync(() => tray.GetVisualDescendants().OfType<ListBox>()
                .SingleOrDefault()?.ItemsSource?.Cast<object>().Count() == 1);
            var list = Assert.Single(tray.GetVisualDescendants().OfType<ListBox>());
            list.SelectedIndex = 0;
            var grid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            grid.EventsMode = true;
            var panel = Assert.IsType<WinFormsEventPanel>(view.EventsContent);
            Assert.DoesNotContain(panel.GetVisualDescendants().OfType<TextBox>(),
                editor => editor.Text == "ExistingTick");

            DoubleClick(list);
            await WaitUntilAsync(() => code.CaretOffset > 0 && view.IsEnabled);
            Assert.Equal(existingCode, code.Text);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBox>(),
                editor => editor.Text == "ExistingTick");
            Assert.Equal(originalDesigner, designer.Text);

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), item =>
                Equals(item.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => view.IsEnabled);
            Assert.Equal(existingCode, code.Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static void DoubleClick(ListBox list)
    {
        list.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.True(condition(), "The tray did not complete the default event edit.");
    }
}
