using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Designer;
using Basalt.Core.Localization;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsDesignerGroupPropertyTests
{
    private static string Source()
    {
        var source = new WinFormsDesignerDocument(WinFormsDesignerDocumentTests.Source)
            .AddControl("Button2", "Global.System.Windows.Forms.Button", "Panel1");
        return new WinFormsDesignerDocument(source).SetProperty("Button2", "Enabled", "False");
    }

    [AvaloniaFact]
    public async Task Common_property_edit_preconverts_then_commits_once_and_undoes_once()
    {
        var original = Source();
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(original));
        var first = session.FindControl("Button1")!;
        var second = session.FindControl("Button2")!;
        var controls = new[] { first, second };
        var common = session.CommonProperties(controls);
        Assert.DoesNotContain(common, property => property.Name == "Name");
        Assert.True(Assert.Single(common, property => property.Name == "Text").IsMixed);
        Assert.True(Assert.Single(common, property => property.Name == "Enabled").IsMixed);
        Assert.DoesNotContain(common, property => property.Name == "Parent");
        Assert.Contains(common, property => property.Name == "TabIndex");
        var changes = 0;
        session.Changed += () => changes++;

        await Assert.ThrowsAnyAsync<Exception>(() => session.SetGroupPropertyAsync(controls, "TabIndex", "invalid"));
        Assert.Equal(original, session.Document.Source);
        Assert.Same(first, session.FindControl("Button1"));
        Assert.Equal(0, changes);
        await Assert.ThrowsAsync<ArgumentException>(() => session.SetGroupPropertyAsync(controls, "Name", "Shared"));

        await session.SetGroupPropertyAsync(controls, "Text", "Shared text");
        Assert.Equal(1, changes);
        Assert.Equal("Shared text", session.FindControl("Button1")!.Text);
        Assert.Equal("Shared text", session.FindControl("Button2")!.Text);
        Assert.Contains("Me.Button1.Text = \"Shared text\"", session.Document.Source);
        Assert.Contains("Me.Button2.Text = \"Shared text\"", session.Document.Source);
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        await session.RedoAsync();
        Assert.Equal("Shared text", session.FindControl("Button2")!.Text);
    }

    [AvaloniaFact]
    public async Task Mixed_boolean_editor_is_indeterminate_until_one_group_edit_sets_both_controls()
    {
        var original = Source();
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", original, true);
        var view = new WinFormsDesignerView(document);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm?.Controls.Count == 1
                && surface.RootForm.Controls[0].Controls.Count == 2);
            var buttons = surface.RootForm!.Controls[0].Controls.Cast<Forms.Control>().ToArray();
            surface.SelectControls(buttons);

            var panel = Assert.Single(view.GetVisualDescendants().OfType<WinFormsPropertyPanel>());
            var grid = Assert.Single(panel.GetVisualDescendants().OfType<PropertyGrid>());
            var enabled = Assert.Single(grid.Properties, property => property.Name == "Enabled");
            Assert.True(enabled.IsMixed);
            Assert.DoesNotContain(grid.Properties, property => property.Name == "Name");
            var row = Assert.Single(grid.Rows.OfType<Border>(), item =>
                item.GetVisualDescendants().OfType<TextBlock>().Any(label => label.Text == "Enabled"));
            var valueEditor = Assert.Single(row.GetVisualDescendants().OfType<ComboBox>());
            Assert.Null(valueEditor.SelectedItem);

            valueEditor.SelectedItem = "True";
            await WaitUntilAsync(() => document.Text.Contains("Me.Button1.Enabled = True", StringComparison.Ordinal)
                && document.Text.Contains("Me.Button2.Enabled = True", StringComparison.Ordinal));
            Assert.All(surface.SelectedControls, control => Assert.True(control.Enabled));
            var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
                Equals(button.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == original);
            Assert.Equal(2, surface.SelectedControls.Count);
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
        Assert.True(condition(), "The designer did not finish the group property edit.");
    }
}
