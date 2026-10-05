using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Basalt.Shell.ViewModels;
using Basalt.Core.Localization;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public sealed class WinFormsMenuPropertyTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                Me.FileMenu = New Global.System.Windows.Forms.ToolStripMenuItem()
                Me.FileMenu.Text = "File"
                ' Keep the leading property comment.
                Me.FileMenu.Checked = True ' Keep the trailing property comment.
                Me.FileMenu.ShortcutKeys = Global.System.Windows.Forms.Keys.Control Or Global.System.Windows.Forms.Keys.O
                Me.MenuStrip1.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.FileMenu})
                Me.Controls.Add(Me.MenuStrip1)
            End Sub
            Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
            Friend WithEvents FileMenu As Global.System.Windows.Forms.ToolStripMenuItem
        End Class
        """;

    [Fact]
    public void ResetPreservesCommentsAndUnrelatedProperties()
    {
        var document = new WinFormsDesignerDocument(Source);
        Assert.Same(document.Menus, document.Menus);
        var updated = document.Menus.SetItemProperty("MenuStrip1", "FileMenu", "Checked", null);
        Assert.Contains("' Keep the leading property comment.", updated);
        Assert.Contains("' Keep the trailing property comment.", updated);
        Assert.DoesNotContain("Me.FileMenu.Checked =", updated);
        Assert.Contains("Me.FileMenu.ShortcutKeys = Global.System.Windows.Forms.Keys.Control Or", updated);
    }

    [Fact]
    public void EditingOnlyChangesTheLastEffectiveAssignment()
    {
        var source = Source.Replace("Me.FileMenu.Checked = True",
            "Me.FileMenu.Checked = ApplicationSpecificValue()\n        Me.FileMenu.Checked = True",
            StringComparison.Ordinal);
        var updated = new WinFormsDesignerDocument(source).Menus
            .SetItemProperty("MenuStrip1", "FileMenu", "Checked", "False");
        Assert.Contains("Me.FileMenu.Checked = ApplicationSpecificValue()", updated);
        Assert.Contains("Me.FileMenu.Checked = False ' Keep the trailing property comment.", updated);
        Assert.Equal("False", new WinFormsDesignerDocument(updated).Menus
            .ReadItemProperties("MenuStrip1", "FileMenu")["Checked"].ToString());
    }

    [AvaloniaFact]
    public async Task CheckedAndShortcutPropertiesPreviewAndUndoWithoutExecutingApplicationCode()
    {
        using var session = new WinFormsDesignerSession(new(Source));
        var file = MenuItem(session);
        Assert.True(file.Checked);
        Assert.Equal(Forms.Keys.Control | Forms.Keys.O, file.ShortcutKeys);
        await session.ApplyCollectionAsync(document =>
            document.Menus.SetItemProperty("MenuStrip1", "FileMenu", "Checked", "False"));
        Assert.False(MenuItem(session).Checked);
        await session.UndoAsync();
        Assert.True(MenuItem(session).Checked);
        Assert.Equal(Source, session.Document.Source);
        await session.RedoAsync();
        Assert.False(MenuItem(session).Checked);
    }

    [AvaloniaFact]
    public void ReservedMenuNameCannotBeEditedOrResetInThePropertyGrid()
    {
        using var item = new Forms.ToolStripMenuItem("File") { Name = "FileMenu" };
        var panel = new WinFormsPropertyPanel();
        panel.Show(item, componentName: "FileMenu", readOnlyProperties: new HashSet<string> { "Name" });
        var owner = new Window { Content = panel, Width = 320, Height = 420 };
        owner.Show();
        try
        {
            var grid = panel.GetVisualDescendants().OfType<PropertyGrid>().Single();
            var name = grid.Properties.Single(property => property.Name == "Name");
            Assert.True(name.IsReadOnly);
            Assert.False(name.CanReset);
            Assert.False(grid.Properties.Single(property => property.Name == "Checked").IsReadOnly);
        }
        finally
        {
            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task AMenuColorDialogCannotEditAnItemAfterTheSelectionChanges()
    {
        var source = new WinFormsDesignerDocument(Source)
            .AddControl("Button1", "Global.System.Windows.Forms.Button");
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(document);
        var owner = new Window { Content = view, Width = 950, Height = 620 };
        owner.Show();
        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls.Find("MenuStrip1", true).Single());
            owner.UpdateLayout();
            var editor = view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>().Single();
            var button = editor.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "MenuInlineItem_FileMenu");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var grid = view.GetVisualDescendants().OfType<PropertyGrid>().Single();
            await WaitUntilAsync(() => grid.Properties.Any(property => property.Name == "Checked"));
            owner.UpdateLayout();
            var row = Assert.Single(grid.Rows.OfType<Border>(), control =>
                control.Name == "PropertyGridRow" &&
                control.GetVisualDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == "BackColor"));
            row.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "PropertyGridEllipsis")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsColorEditor>().Any());
            var dialog = owner.OwnedWindows.OfType<WinFormsColorEditor>().Single();
            dialog.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => control.Name == "ColorKind").SelectedIndex = 2;
            dialog.GetVisualDescendants().OfType<TextBox>()
                .Single(control => control.Name == "ColorHex").Text = "#FF123456";

            surface.Select(surface.RootForm.Controls.Find("Button1", true).Single());
            dialog.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "ColorOk")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var expectedError = Localizer.Get("WinFormsDesigner_Error",
                Localizer.Get("WinFormsDesigner_CodeChanged"));
            await WaitUntilAsync(() => !owner.OwnedWindows.Any() && view.IsEnabled &&
                view.GetVisualDescendants().OfType<TextBlock>()
                    .Any(label => label.Text == expectedError));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(source, document.Text);
            Assert.Equal("Button1", surface.SelectedControl?.Name);
            await WaitUntilAsync(() => grid.Properties.Any(property =>
                property.Name == "Name" && Equals(property.CurrentValue ?? property.DefaultValue, "Button1")));
        }
        finally
        {
            owner.Close();
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
        Assert.True(condition(), "The menu designer operation did not complete.");
    }

    private static Forms.ToolStripMenuItem MenuItem(WinFormsDesignerSession session) =>
        Assert.IsType<Forms.ToolStripMenuItem>(Assert.Single(
            Assert.IsType<Forms.MenuStrip>(session.FindControl("MenuStrip1")).Items.Cast<Forms.ToolStripItem>()));
}
