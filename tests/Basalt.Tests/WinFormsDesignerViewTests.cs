using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Core.Localization;

namespace Basalt.Tests;

public class WinFormsDesignerViewTests
{
    [AvaloniaFact]
    public async Task DeleteUpdatesCompanionHandlesAndLinkedUndoProtectsLaterUserCode()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        var code = new EditorDocumentViewModel("MainForm.vb", """
            Partial Class MainForm
                Private Sub CompanionHandler(sender As Object, e As System.EventArgs) Handles Button1.Click, Me.Load
                    ' Keep the handler body after deleting its button.
                    System.Console.WriteLine("Preserved")
                End Sub
            End Class
            """);
        var originalCode = code.Text;
        var view = new WinFormsDesignerView(document, findCode: path => path == code.FilePath ? code : null,
            getCode: _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() => !document.Text.Contains("Friend WithEvents Button1", StringComparison.Ordinal));
            Assert.Contains("Handles Me.Load", code.Text);
            Assert.DoesNotContain("Handles Button1.Click", code.Text);
            Assert.Contains("' Keep the handler body after deleting its button.", code.Text);
            var deletedDesigner = document.Text;
            var deletedCode = code.Text;

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            var redo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditRedo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == WinFormsDesignerDocumentTests.Source);
            Assert.Equal(originalCode, code.Text);
            redo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == deletedDesigner);
            Assert.Equal(deletedCode, code.Text);

            code.Text += "\n' Later unsaved user change";
            var laterCode = code.Text;
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => view.IsEnabled);
            Assert.Equal(laterCode, code.Text);
            Assert.Equal(deletedDesigner, document.Text);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text?.Contains(Localizer.Get("WinFormsDesigner_CodeChanged"), StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task EventDoubleClickEditsUnsavedCompanionAndNavigatesToCreatedHandler()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        var code = new EditorDocumentViewModel("MainForm.vb", """
            Partial Class MainForm
                ' Preserve this unsaved user comment.
                Private Sub KeepMe()
                    System.Console.WriteLine("Unsaved body")
                End Sub
            End Class
            """);
        code.Text = code.Text.Replace("Unsaved body", "Latest unsaved body", StringComparison.Ordinal);
        string? openedPath = null;
        var view = new WinFormsDesignerView(document, path =>
        {
            openedPath = path;
            return Task.FromResult(code);
        }, path => path == code.FilePath ? code : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls[0].Controls[0]);
            var eventsButton = view.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "PropertyGridEvents");
            eventsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var panel = Assert.Single(view.GetVisualDescendants().OfType<WinFormsEventPanel>());
            var label = Assert.Single(panel.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == "MouseDown");
            label.RaiseEvent(new TappedEventArgs(Control.DoubleTappedEvent, null!));
            await WaitUntilAsync(() => code.Text.Contains("Handles Button1.MouseDown", StringComparison.Ordinal));

            Assert.Equal("MainForm.vb", openedPath);
            Assert.True(code.IsModified);
            var bodyLine = Microsoft.CodeAnalysis.Text.SourceText.From(code.Text).Lines.GetLineFromPosition(code.CaretOffset);
            Assert.Equal("        ", bodyLine.ToString());
            Assert.Equal(bodyLine.End, code.CaretOffset);
            Assert.Contains("' Preserve this unsaved user comment.", code.Text);
            Assert.Contains("Latest unsaved body", code.Text);
            Assert.Equal(WinFormsDesignerDocumentTests.Source, document.Text);
            Assert.Contains(panel.GetVisualDescendants().OfType<TextBox>(), editor => editor.Text == "Button1_MouseDown");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task PropertyEditUpdatesDocumentAndRetainsSelectedFieldAfterPreviewRebuild()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsDesignerDocumentTests.Source, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            var button = surface.RootForm!.Controls[0].Controls[0];
            surface.Select(button);
            Dispatcher.UIThread.RunJobs();

            var panel = Assert.Single(view.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
            var textEditor = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>(), box => box.Text == "Before");
            textEditor.Focus();
            textEditor.Text = "Edited in the designer";
            surface.Focus();
            await WaitUntilAsync(() => document.Text.Contains("\"Edited in the designer\"", StringComparison.Ordinal));

            Assert.True(document.IsModified);
            Assert.NotSame(button, surface.SelectedControl);
            Assert.Equal("Button1", surface.SelectedControl!.Name);
            Assert.Equal("Edited in the designer", surface.SelectedControl.Text);
            Assert.Contains("DangerousCall()", document.Text);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not finish the requested operation.");
    }
}
