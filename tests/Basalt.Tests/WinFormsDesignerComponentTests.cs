using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Basalt.Core.Localization;
using Microsoft.CodeAnalysis.VisualBasic;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsDesignerComponentTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.components = New System.ComponentModel.Container()
                Me.Timer1 = New Global.System.Windows.Forms.Timer(Me.components)
                Me.ImageList1 = New Global.System.Windows.Forms.ImageList(Me.components)
                Me.ToolTip1 = New Global.System.Windows.Forms.ToolTip(Me.components)
                Me.ContextMenuStrip1 = New Global.System.Windows.Forms.ContextMenuStrip(Me.components)
                Me.Button1 = New Global.System.Windows.Forms.Button()
                Me.Timer1.Interval = 10
                Me.Timer1.Enabled = True
                Me.Timer1.Tag = DangerousCall()
                Me.ContextMenuStrip1.Visible = True
                Me.Button1.Text = "Keep me"
                Me.Controls.Add(Me.Button1)
                Me.ResumeLayout(False)
            End Sub
            Private components As System.ComponentModel.IContainer
            Friend WithEvents Timer1 As System.Windows.Forms.Timer
            Friend WithEvents ImageList1 As System.Windows.Forms.ImageList
            Friend WithEvents ToolTip1 As System.Windows.Forms.ToolTip
            Friend WithEvents ContextMenuStrip1 As System.Windows.Forms.ContextMenuStrip
            Friend WithEvents Button1 As System.Windows.Forms.Button
            Private Sub Timer1_Tick(sender As Object, e As System.EventArgs) Handles Timer1.Tick
                ' Handler body must survive deletion.
            End Sub
            Private Function DangerousCall() As Object
                Throw New System.Exception("The designer must not execute application code")
            End Function
        End Class
        """;

    [Fact]
    public void Finds_tray_components_without_treating_them_as_controls()
    {
        var document = new WinFormsDesignerDocument(Source);

        Assert.Equal(new[] { "Timer1", "ImageList1", "ToolTip1", "ContextMenuStrip1" },
            document.Components.Select(component => component.Name));
        Assert.Equal("Button1", Assert.Single(document.Controls).Name);
        Assert.Equal("True", document.Components[0].Properties["Enabled"]);
        Assert.Contains("DangerousCall()", document.Source);
    }

    [Fact]
    public void Add_edit_and_delete_preserve_unknown_source_without_bounds_or_parenting()
    {
        var source = new WinFormsDesignerDocument(Source)
            .AddComponent("Timer2", "Global.System.Windows.Forms.Timer");
        Assert.Contains("Friend WithEvents Timer2 As Global.System.Windows.Forms.Timer", source);
        Assert.Contains("Me.Timer2 = New Global.System.Windows.Forms.Timer()", source);
        Assert.DoesNotContain("Me.Timer2.Location", source);
        Assert.DoesNotContain("Me.Controls.Add(Me.Timer2)", source);
        Assert.False(VisualBasicSyntaxTree.ParseText(source).GetRoot().ContainsDiagnostics);

        var withProperty = new WinFormsDesignerDocument(source)
            .SetComponentProperty("Timer2", "Interval", "25");
        Assert.Contains("Me.Timer2.Interval = 25", withProperty);
        var deleted = new WinFormsDesignerDocument(withProperty).RemoveComponent("Timer1");
        var document = new WinFormsDesignerDocument(deleted);
        Assert.DoesNotContain(document.Components, component => component.Name == "Timer1");
        Assert.Contains(document.Components, component => component.Name == "Timer2");
        Assert.Contains("Me.Button1.Text = \"Keep me\"", deleted);
        Assert.Contains("' Handler body must survive deletion.", deleted);
        Assert.DoesNotContain("Handles Timer1.Tick", deleted);
        Assert.Contains("Private Function DangerousCall()", deleted);
        Assert.False(VisualBasicSyntaxTree.ParseText(deleted).GetRoot().ContainsDiagnostics);
    }

    [AvaloniaFact]
    public async Task Generated_name_skips_unknown_declared_fields()
    {
        var source = Source.Replace("Friend WithEvents Timer1 As System.Windows.Forms.Timer",
            "Private Timer2 As Object\n    Friend WithEvents Timer1 As System.Windows.Forms.Timer", StringComparison.Ordinal);
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));

        await session.AddComponentAsync(typeof(Forms.Timer));

        Assert.Null(session.FindComponent("Timer2"));
        Assert.IsType<Forms.Timer>(session.FindComponent("Timer3"));
        Assert.Contains("Private Timer2 As Object", session.Document.Source);
    }

    [AvaloniaFact]
    public async Task Preview_never_starts_timer_and_component_edits_are_undoable()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(Source));
        var timer = Assert.IsType<Forms.Timer>(session.FindComponent("Timer1"));
        Assert.False(timer.Enabled);
        Assert.Null(typeof(Forms.Timer).GetField("_timer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(timer));
        Assert.Equal("True", session.ExplicitComponentValue(timer, "Enabled"));
        var menu = Assert.IsType<Forms.ContextMenuStrip>(session.FindComponent("ContextMenuStrip1"));
        Assert.False(menu.Visible);
        Assert.Equal("True", session.ExplicitComponentValue(menu, "Visible"));
        Assert.Contains(session.PreviewProblems, problem => problem.StartsWith("Timer1.Tag", StringComparison.Ordinal));

        await session.AddComponentAsync(typeof(Forms.Timer));
        Assert.IsType<Forms.Timer>(session.FindComponent("Timer2"));
        Assert.DoesNotContain("Timer2", session.Form.Controls.Cast<Forms.Control>().Select(control => control.Name));
        await session.SetComponentPropertyAsync(session.FindComponent("Timer2")!, "Interval", "42");
        Assert.Equal(42, ((Forms.Timer)session.FindComponent("Timer2")!).Interval);
        await session.DeleteComponentAsync(session.FindComponent("Timer2")!);
        Assert.Null(session.FindComponent("Timer2"));
        await session.UndoAsync();
        Assert.Equal(42, ((Forms.Timer)session.FindComponent("Timer2")!).Interval);
        await session.UndoAsync();
        Assert.Equal(100, ((Forms.Timer)session.FindComponent("Timer2")!).Interval);
        await session.UndoAsync();
        Assert.Null(session.FindComponent("Timer2"));
        Assert.Equal(Source, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task View_tray_selection_and_delete_persist_with_undo()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", Source, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var tray = Assert.Single(view.GetVisualDescendants().OfType<WinFormsComponentTray>());
            await WaitUntilAsync(() => tray.GetVisualDescendants().OfType<ListBox>()
                .SingleOrDefault()?.ItemsSource?.Cast<object>().Count() == 4);
            var list = Assert.Single(tray.GetVisualDescendants().OfType<ListBox>());
            list.SelectedIndex = 0;
            Assert.Equal("Timer1", tray.SelectedName);
            var propertyPanel = Assert.Single(view.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
            var intervalRow = Assert.Single(propertyPanel.GetVisualDescendants().OfType<Border>(), row =>
                row.Name == "PropertyGridRow"
                && row.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Interval"));
            var intervalEditor = Assert.Single(intervalRow.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("10", intervalEditor.Text);

            list.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() => !document.Text.Contains("Friend WithEvents Timer1", StringComparison.Ordinal));
            Assert.Contains("Me.ImageList1 = New", document.Text);
            Assert.Contains("DangerousCall()", document.Text);

            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == Source);
            Assert.Equal(4, list.ItemsSource?.Cast<object>().Count());
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
        Assert.True(condition(), "The designer did not finish the requested component operation.");
    }
}
