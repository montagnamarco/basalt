using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Basalt.Shell.ViewModels;
using Basalt.Core.Localization;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public class WinFormsCollectionDesignerTests
{
    [AvaloniaFact]
    public async Task PropertyPanelButtonOpensModalAndCommitsTheDocumentWithOneUndo()
    {
        var document = new EditorDocumentViewModel("MainForm.Designer.vb", WinFormsCollectionDocumentTests.Source, true);
        var view = new WinFormsDesignerView(document);
        var owner = new Window { Content = view, Width = 1100, Height = 700 };
        owner.Show();
        try
        {
            var surface = view.GetVisualDescendants().OfType<WinFormsDesignSurface>().Single();
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(surface.RootForm!.Controls.Cast<Forms.Control>().Single(control => control.Name == "ListBox1"));
            Dispatcher.UIThread.RunJobs();
            var grid = view.GetVisualDescendants().OfType<PropertyGrid>().Single();
            var itemsRow = grid.Rows.Single(row => row.GetVisualDescendants().OfType<TextBlock>()
                .Any(label => label.Text == "Items"));
            var button = itemsRow.GetVisualDescendants().OfType<Button>()
                .Single(control => control.Name == "PropertyGridEllipsis");
            Assert.True(button.IsVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => owner.OwnedWindows.OfType<WinFormsCollectionEditor>().Any());
            var editor = owner.OwnedWindows.OfType<WinFormsCollectionEditor>().Single();
            Click(editor, "CollectionAdd");
            Find<TextBox>(editor, "CollectionText").Text = "Added from modal";
            Click(editor, "CollectionSave");
            await WaitUntilAsync(() => document.Text.Contains("Added from modal", StringComparison.Ordinal));
            Assert.True(document.IsModified);
            Assert.Equal("ListBox1", surface.SelectedControl!.Name);
            Assert.Equal("Added from modal", Assert.IsType<Forms.ListBox>(surface.SelectedControl).Items[2]);
            Assert.Contains("DangerousCall()", document.Text);
            var undo = view.GetVisualDescendants().OfType<Button>().Single(control =>
                Equals(control.Content, Localizer.Get(StringKeys.MenuEditUndo)));
            undo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WaitUntilAsync(() => document.Text == WinFormsCollectionDocumentTests.Source);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task ItemAndColumnEditsPreviewPersistReopenAndUndoAsSingleTransactions()
    {
        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(WinFormsCollectionDocumentTests.Source));
        Assert.Equal(["Alpha", "Quoted \"item\""], Assert.IsType<Forms.ListBox>(session.FindControl("ListBox1")).Items.Cast<string>());
        var grid = Assert.IsType<Forms.DataGridView>(session.FindControl("Grid1"));
        Assert.Equal(2, grid.Columns.Count);
        Assert.Equal("TitleData", grid.Columns[0].DataPropertyName);
        var original = session.Document.Source;
        await session.SetItemsAsync(session.FindControl("ListBox1")!, ["Third", "Second", "First"]);
        var itemSource = session.Document.Source;
        Assert.Equal(["Third", "Second", "First"], Assert.IsType<Forms.ListBox>(session.FindControl("ListBox1")).Items.Cast<string>());
        await session.UndoAsync();
        Assert.Equal(original, session.Document.Source);
        await session.RedoAsync();
        Assert.Equal(itemSource, session.Document.Source);
        var columns = session.Document.Collections.ReadColumns("Grid1").Columns;
        await session.SetColumnsAsync(session.FindControl("Grid1")!, [columns[1], columns[0] with { Name = "Renamed", HeaderText = "New title" }]);
        grid = Assert.IsType<Forms.DataGridView>(session.FindControl("Grid1"));
        Assert.Equal(["CheckColumn", "Renamed"], grid.Columns.Cast<Forms.DataGridViewColumn>().Select(column => column.Name));
        Assert.Equal("TitleData", grid.Columns[1].DataPropertyName);
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".Designer.vb");
        try
        {
            await File.WriteAllTextAsync(path, session.Document.Source);
            var reopenedModel = await Task.Run(() => new WinFormsDesignerDocument(File.ReadAllText(path)));
            using var reopened = new WinFormsDesignerSession(reopenedModel);
            Assert.Equal("New title", Assert.IsType<Forms.DataGridView>(reopened.FindControl("Grid1")).Columns[1].HeaderText);
            Assert.Contains("DangerousCall()", reopened.Document.Source);
            Assert.Null(reopened.FindControl("Grid1")!.Tag);
            WinFormsCompilationAssert.AssertCompiles(reopened.Document.Source);
        }
        finally { File.Delete(path); }
        await session.UndoAsync();
        Assert.Equal(itemSource, session.Document.Source);
    }

    [AvaloniaFact]
    public async Task ModalItemEditorAddsReordersRemovesAndReturnsAChangedDraft()
    {
        var owner = new Window();
        owner.Show();
        var editor = new WinFormsCollectionEditor(new[] { "First", "Second" });
        var pending = editor.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        Click(editor, "CollectionAdd");
        Find<TextBox>(editor, "CollectionText").Text = "Third";
        Click(editor, "CollectionUp");
        Assert.Equal(["First", "Third", "Second"], editor.Items);
        Click(editor, "CollectionRemove");
        Assert.Equal(["First", "Second"], editor.Items);
        Find<TextBox>(editor, "CollectionText").Text = "Edited";
        Click(editor, "CollectionSave");
        Assert.True(await pending);
        Assert.Equal(["First", "Edited"], editor.Items);
        owner.Close();
    }

    [AvaloniaFact]
    public async Task ModalColumnEditorValidatesNamesAndAllowsRealColumnTypes()
    {
        var owner = new Window();
        owner.Show();
        var editor = new WinFormsCollectionEditor(new[] { new WinFormsColumnDefinition("Title", "DataGridViewTextBoxColumn", "Title", OriginalName: "Title") });
        var pending = editor.ShowDialog<bool>(owner);
        Dispatcher.UIThread.RunJobs();
        Click(editor, "CollectionAdd");
        Find<TextBox>(editor, "CollectionName").Text = "Title";
        Assert.False(Find<Button>(editor, "CollectionSave").IsEnabled);
        Find<TextBox>(editor, "CollectionName").Text = "Flag";
        Find<ComboBox>(editor, "CollectionType").SelectedItem = "DataGridViewCheckBoxColumn";
        Find<TextBox>(editor, "CollectionText").Text = "Enabled";
        Find<TextBox>(editor, "CollectionWidth").Text = "75";
        Assert.True(Find<Button>(editor, "CollectionSave").IsEnabled);
        Click(editor, "CollectionSave");
        Assert.True(await pending);
        Assert.Equal(new WinFormsColumnDefinition("Flag", "DataGridViewCheckBoxColumn", "Enabled", 75), editor.Columns[1]);
        owner.Close();
    }

    private static T Find<T>(Window editor, string name) where T : Control => editor.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static void Click(Window editor, string name) => Find<Button>(editor, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The collection editor did not finish its operation.");
    }
}
