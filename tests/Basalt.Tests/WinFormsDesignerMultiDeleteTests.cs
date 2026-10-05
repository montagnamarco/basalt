using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
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

public class WinFormsDesignerMultiDeleteTests
{
    [AvaloniaFact]
    public async Task Deleting_selected_parent_child_and_sibling_is_one_undoable_transaction()
    {
        var source = WinFormsDesignerDocumentTests.Source;
        source = new WinFormsDesignerDocument(source).AddControl("Panel2", "Global.System.Windows.Forms.Panel", "Panel1");
        source = new WinFormsDesignerDocument(source).AddControl("Label1", "Global.System.Windows.Forms.Label", "Panel2");
        source = new WinFormsDesignerDocument(source).AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        source = new WinFormsDesignerDocument(source).AddComponent("Timer1", "Global.System.Windows.Forms.Timer");
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        var parent = session.FindControl("Panel2")!;
        var child = session.FindControl("Label1")!;
        var sibling = session.FindControl("Button2")!;
        var selection = new[] { child, sibling, parent };
        var changed = 0;
        session.Changed += () => changed++;

        Assert.Equal(new[] { "Button2", "Label1", "Panel2" }, session.DeletedControlNames(selection).Order());
        await session.DeleteControlsAsync(selection);

        Assert.Equal(1, changed);
        Assert.Null(session.FindControl("Panel2"));
        Assert.Null(session.FindControl("Label1"));
        Assert.Null(session.FindControl("Button2"));
        Assert.NotNull(session.FindControl("Button1"));
        Assert.NotNull(session.FindComponent("Timer1"));
        Assert.Contains("Handler code stays untouched", session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.NotNull(session.FindControl("Panel2"));
        Assert.NotNull(session.FindControl("Label1"));
        Assert.NotNull(session.FindControl("Button2"));
        await session.RedoAsync();
        Assert.Null(session.FindControl("Panel2"));
        Assert.ThrowsAny<Exception>(() => session.DeletedControlNames([parent]));
        Assert.Throws<ArgumentException>(() => session.DeletedControlNames([session.Form]));
    }

    [AvaloniaFact]
    public async Task Delete_key_removes_selected_siblings_and_only_their_companion_handles()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        source = new WinFormsDesignerDocument(source).SetProperty("Button2", "Location", "New System.Drawing.Point(130, 20)");
        source = new WinFormsDesignerDocument(source).AddComponent("Timer1", "Global.System.Windows.Forms.Timer");
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var companionSource = """
            Partial Class MainForm
                Private Sub SharedHandler(sender As Object, e As System.EventArgs) Handles Button1.Click, Button2.Click, Timer1.Tick, Me.Load
                    System.Console.WriteLine("Keep handler body")
                End Sub
            End Class
            """;
        var code = new EditorDocumentViewModel("MainForm.vb", companionSource);
        var view = new WinFormsDesignerView(designer, findCode: path => path == code.FilePath ? code : null,
            getCode: _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm?.Controls.Count == 1
                && surface.RootForm.Controls[0].Controls.Count == 2);
            var panel = surface.RootForm!.Controls[0];
            var first = panel.Controls.Cast<Forms.Control>().Single(control => control.Name == "Button1");
            var second = panel.Controls.Cast<Forms.Control>().Single(control => control.Name == "Button2");
            surface.Select(first);
            var secondPoint = surface.TranslatePoint(new Point(12 + second.Left + 20, 12 + second.Top + 15), window)!.Value;
            window.MouseDown(secondPoint, MouseButton.Left, RawInputModifiers.Shift);
            window.MouseUp(secondPoint, MouseButton.Left);
            Assert.Equal([first, second], surface.SelectedControls);

            surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() => !designer.Text.Contains("Friend WithEvents Button2", StringComparison.Ordinal));
            Assert.DoesNotContain("Friend WithEvents Button1", designer.Text);
            Assert.Contains("Friend WithEvents Timer1", designer.Text);
            // Roslyn may normalize comma trivia while rewriting Handles; the bindings are the contract.
            Assert.Equal("SharedHandler", WinFormsEventHandlers.Read(code.Text, "MainForm", "Timer1")["Tick"]);
            Assert.Equal("SharedHandler", WinFormsEventHandlers.Read(code.Text, "MainForm", "Me")["Load"]);
            Assert.DoesNotContain("Button1.Click", code.Text);
            Assert.DoesNotContain("Button2.Click", code.Text);
            Assert.Contains("Keep handler body", code.Text);

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => designer.Text == source);
            Assert.Equal(companionSource, code.Text);
        }
        finally { window.Close(); }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not finish the requested group deletion.");
    }
}
