using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;

namespace Basalt.Tests;

public class WinFormsDesignerLifecycleTests
{
    [AvaloniaFact]
    public async Task SwitchingToCodeRetainsSelectionAndDesignerUndo()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsCollectionDocumentTests.Source, true);
        WinFormsDesignerView? view = null;
        using var host = new DesignerHost(document, () => view = new WinFormsDesignerView(document), () => new TextBox());
        var window = new Window { Content = host, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = view!.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls.Cast<System.Windows.Forms.Control>().Single(control => control.Name == "ListBox1"));
            var initialForm = surface.RootForm;
            host.ShowText();
            Assert.False(initialForm!.IsDisposed);
            host.ShowDesigner();
            Assert.Same(initialForm, surface.RootForm);
            Assert.Equal("ListBox1", surface.SelectedControl!.Name);
            // Make a real property edit through the inspector's callback.
            var sessionField = typeof(WinFormsDesignerView).GetField("_session", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var session = Assert.IsType<WinFormsDesignerSession>(sessionField.GetValue(view));
            await session.SetItemsAsync(session.FindControl("ListBox1")!, ["Changed"]);
            host.ShowText();
            host.ShowDesigner();
            var undo = view!.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == WinFormsCollectionDocumentTests.Source);
            Assert.Same(session, sessionField.GetValue(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ExternalCodeEditRebuildsAndExplicitDisposalReleasesPreview()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsCollectionDocumentTests.Source, true);
        WinFormsDesignerView? view = null;
        var creations = 0;
        using var host = new DesignerHost(document, () => { creations++; return view = new WinFormsDesignerView(document); }, () => new TextBox());
        var window = new Window { Content = host, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = view!.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            var oldForm = surface.RootForm!;
            host.ShowText();
            document.Text = document.Text.Replace("\"Alpha\"", "\"External edit\"", StringComparison.Ordinal);
            var changed = document.Text;
            host.ShowDesigner();
            await WaitUntilAsync(() => surface.RootForm is not null && !ReferenceEquals(oldForm, surface.RootForm));
            Assert.True(oldForm.IsDisposed);
            Assert.Equal(1, creations);
            Assert.Equal(changed, document.Text);
            var rebuiltForm = surface.RootForm!;
            host.Dispose();
            host.Dispose();
            Assert.True(rebuiltForm.IsDisposed);
            Assert.Null(surface.RootForm);
        }
        finally { window.Close(); }
    }

    private static async Task WaitUntilAsync(Func<bool> predicate)
    {
        for (var i = 0; i < 200; i++)
        {
            Dispatcher.UIThread.RunJobs();
            if (predicate()) return;
            await Task.Delay(10);
        }
        Assert.True(predicate());
    }
}
