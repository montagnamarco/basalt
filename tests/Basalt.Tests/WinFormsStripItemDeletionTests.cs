using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Proves Visual Studio's Delete behaviour for the WinForms designer's inline strip editors:
/// Delete/Backspace and the item's own context menu "Delete" both remove the whole subtree
/// (fields, Me.X.* statements, Items/DropDownItems entries, any resx entries the removed names
/// owned) in one undo step, move the editor's and Properties panel's selection to the
/// neighbouring item the way Visual Studio does, and leave the companion code-behind's handler
/// methods in place with only their Handles clause for the removed names trimmed. See
/// <see cref="WinFormsMenuInlineTests"/>, <see cref="WinFormsToolStripInlineTests"/> and
/// <see cref="WinFormsToolStripDropDownInlineTests"/> for the add/edit side of the same editors.
/// </summary>
public sealed class WinFormsStripItemDeletionTests
{
    private const string MenuSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                Me.MenuStrip1.Name = "MenuStrip1"
                Me.Controls.Add(Me.MenuStrip1)
                ' Keep application initialization.
            End Sub
            Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
        End Class
        """;

    private const string ToolStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.Controls.Add(Me.ToolStrip1)
                ' Keep application initialization.
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
        End Class
        """;

    private const string CompanionSource = """
        Partial Class MainForm
            Private Sub OpenMenuItem_Click(sender As Object, e As System.EventArgs) Handles OpenMenuItem.Click
                System.Console.WriteLine("Keep handler body")
            End Sub
        End Class
        """;

    // A MenuStrip with two top-level items (File, Edit) and one nested item (Open) under
    // File that owns a companion Click handler: deleting File must take Open with it.
    private static string MenuFixture() => new WinFormsMenuDocument(MenuSource).Set("MenuStrip1",
        [
            new WinFormsMenuItem("FileMenu", "MenuStrip1", "&File"),
            new WinFormsMenuItem("EditMenu", "MenuStrip1", "&Edit"),
            new WinFormsMenuItem("OpenMenuItem", "FileMenu", "&Open"),
        ]);

    [AvaloniaFact]
    public async Task DeleteKeyRemovesATopLevelMenuItemsWholeSubtreeSelectsTheNeighbourAndUndoesCleanly()
    {
        var source = MenuFixture();
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var code = new EditorDocumentViewModel("MainForm.vb", CompanionSource);
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path == code.FilePath ? code : null, getCode: _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));

            var editor = MenuEditor(view);
            Click(Find<Button>(editor, "MenuInlineItem_FileMenu"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("FileMenu", editor.SelectedItemName);

            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() => !designer.Text.Contains("FileMenu", StringComparison.Ordinal) && view.IsEnabled);

            Assert.DoesNotContain("FileMenu", designer.Text);
            Assert.DoesNotContain("OpenMenuItem", designer.Text);
            Assert.Contains("EditMenu", designer.Text);
            Assert.DoesNotContain("Handles OpenMenuItem.Click", code.Text);
            Assert.Contains("Private Sub OpenMenuItem_Click", code.Text);
            Assert.Contains("Keep handler body", code.Text);

            var liveMenu = Assert.IsType<Forms.MenuStrip>(surface.SelectedControl);
            Assert.DoesNotContain(liveMenu.Items.Cast<Forms.ToolStripItem>(), item => item.Name == "FileMenu");
            Assert.Contains(liveMenu.Items.Cast<Forms.ToolStripItem>(), item => item.Name == "EditMenu");

            editor = MenuEditor(view);
            Assert.Equal("EditMenu", editor.SelectedItemName);
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => propertyGrid.Properties.Any(
                property => property.Name == "Name" && property.DefaultValue == "EditMenu"));

            ClickUndo(view);
            await WaitUntilAsync(() => designer.Text == source && view.IsEnabled);
            Assert.Equal(CompanionSource, code.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ContextMenuDeleteRemovesATopLevelMenuItemsWholeSubtreeSelectsTheNeighbourAndUndoesCleanly()
    {
        var source = MenuFixture();
        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var code = new EditorDocumentViewModel("MainForm.vb", CompanionSource);
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path == code.FilePath ? code : null, getCode: _ => Task.FromResult(code));
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));

            var editor = MenuEditor(view);
            var fileButton = Find<Button>(editor, "MenuInlineItem_FileMenu");
            var deleteEntry = Assert.Single(fileButton.ContextMenu!.Items.OfType<MenuItem>(),
                item => item.Name == "MenuInlineDelete_FileMenu");
            deleteEntry.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            await WaitUntilAsync(() => !designer.Text.Contains("FileMenu", StringComparison.Ordinal) && view.IsEnabled);

            Assert.DoesNotContain("FileMenu", designer.Text);
            Assert.DoesNotContain("OpenMenuItem", designer.Text);
            Assert.Contains("EditMenu", designer.Text);
            Assert.DoesNotContain("Handles OpenMenuItem.Click", code.Text);
            Assert.Contains("Private Sub OpenMenuItem_Click", code.Text);
            Assert.Contains("Keep handler body", code.Text);

            editor = MenuEditor(view);
            Assert.Equal("EditMenu", editor.SelectedItemName);
            var propertyGrid = Assert.Single(view.GetVisualDescendants().OfType<PropertyGrid>());
            await WaitUntilAsync(() => propertyGrid.Properties.Any(
                property => property.Name == "Name" && property.DefaultValue == "EditMenu"));

            ClickUndo(view);
            await WaitUntilAsync(() => designer.Text == source && view.IsEnabled);
            Assert.Equal(CompanionSource, code.Text);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DeletingAToolStripButtonRemovesItsLocalImageResourceAndUndoRestoresTheResxByteIdentically()
    {
        var withButton = new WinFormsToolStripDocument(ToolStripSource).Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "Save")]);
        var source = new WinFormsDesignerDocument(withButton).SetImageResource(
            "ToolStripButton1", "Image", "ToolStripButton1.Image", "System.Drawing.Image",
            stripName: "ToolStrip1", isMenu: false);
        const string resources = """
            <root>
              <data name="ToolStripButton1.Image" type="System.Drawing.Bitmap, System.Drawing"><value>Placeholder</value></data>
            </root>
            """;

        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        var resourceBuffer = new EditorDocumentViewModel("MainForm.resx", resources);
        using var view = new WinFormsDesignerView(designer,
            findCode: path => path.EndsWith(".resx", StringComparison.Ordinal) ? resourceBuffer : null);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

            var editor = ToolStripEditor(view);
            Click(Find<Button>(editor, "ToolStripInlineItem_ToolStripButton1"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ToolStripButton1", editor.SelectedItemName);

            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() =>
                !designer.Text.Contains("ToolStripButton1", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Null(new WinFormsResourceDocument(resourceBuffer.Text).TryGet("ToolStripButton1.Image"));

            ClickUndo(view);
            await WaitUntilAsync(() => designer.Text == source && view.IsEnabled);
            Assert.Equal(resources, resourceBuffer.Text);
        }
        finally
        {
            window.Close();
        }
    }

    // Regression for the bug just fixed in WinFormsDesignerSession.DeleteToolStripItemAsync: a
    // ToolStripDropDownButton/SplitButton's own cascading DropDownItems tree must be read from
    // the source *before* RemoveItem erases the "Me.<owner>.DropDownItems.AddRange" statement
    // that is its only link back to the owner - reading it afterward always finds it empty,
    // leaving every nested field and Me.X.* statement behind as an orphan.
    [AvaloniaFact]
    public async Task DeletingADropDownButtonAlsoRemovesItsOwnNestedTreeWithNoOrphanDeclarations()
    {
        var withOwner = new WinFormsToolStripDocument(ToolStripSource).Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripDropDownButton1", "ToolStripDropDownButton", "Menu")]);
        var source = new WinFormsMenuDocument(withOwner).Set("ToolStripDropDownButton1",
            [
                new WinFormsMenuItem("OpenItem", "ToolStripDropDownButton1", "Open"),
                new WinFormsMenuItem("RecentItem", "OpenItem", "Recent"),
                new WinFormsMenuItem("SaveItem", "ToolStripDropDownButton1", "Save"),
            ]);

        using var session = new WinFormsDesignerSession(new WinFormsDesignerDocument(source));
        var removed = await session.DeleteToolStripItemAsync("ToolStrip1", "ToolStripDropDownButton1");

        Assert.Equal(new[] { "OpenItem", "RecentItem", "SaveItem", "ToolStripDropDownButton1" },
            removed.Order(StringComparer.OrdinalIgnoreCase));
        foreach (var name in removed)
        {
            Assert.DoesNotContain("Friend WithEvents " + name, session.Document.Source, StringComparison.Ordinal);
            Assert.DoesNotContain("Me." + name, session.Document.Source, StringComparison.Ordinal);
        }

        await session.UndoAsync();
        Assert.Equal(source, session.Document.Source);
        Assert.Contains("OpenItem", session.Document.Source, StringComparison.Ordinal);
        Assert.Contains("RecentItem", session.Document.Source, StringComparison.Ordinal);
    }

    // Deleting a nested item inside the drop-down panel (the cascading DropDownItems tree a
    // ToolStripDropDownButton/SplitButton owns), through the full WinFormsDesignerView wiring
    // in WinFormsDesignerView.ToolStripDropDownInline.cs, must remove only that item's own
    // subtree, leaving its siblings and the owning item itself untouched.
    [AvaloniaFact]
    public async Task DeleteKeyInsideTheDropDownPanelRemovesOnlyThatItemsOwnSubtreeNotSiblingsOrTheOwner()
    {
        var withOwner = new WinFormsToolStripDocument(ToolStripSource).Set("ToolStrip1",
            [new WinFormsToolStripItem("ToolStripDropDownButton1", "ToolStripDropDownButton", "Menu")]);
        var source = new WinFormsMenuDocument(withOwner).Set("ToolStripDropDownButton1",
            [
                new WinFormsMenuItem("OpenItem", "ToolStripDropDownButton1", "Open"),
                new WinFormsMenuItem("RecentItem", "OpenItem", "Recent"),
                new WinFormsMenuItem("SaveItem", "ToolStripDropDownButton1", "Save"),
            ]);

        var designer = new EditorDocumentViewModel("MainForm.Designer.vb", source, true);
        using var view = new WinFormsDesignerView(designer);
        var window = new Window { Content = view, Width = 1100, Height = 700 };
        window.Show();
        try
        {
            var surface = Assert.Single(view.GetVisualDescendants().OfType<WinFormsDesignSurface>());
            await WaitUntilAsync(() => surface.RootForm is not null);
            surface.Select(Assert.Single(surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

            var editor = ToolStripEditor(view);
            Click(Find<Button>(editor, "ToolStripInlineItem_ToolStripDropDownButton1"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            Click(Find<Button>(panelHost, "ToolStripDropDownItem_OpenItem"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            panelHost = Find<Border>(editor, "ToolStripDropDownHost");
            // Delete must reach the nested panel's own OnKeyDown override (it owns the selected
            // nested item), not just the outer ToolStrip editor's: raise it on the panel itself,
            // the same control that painted the selected item's own context menu "Delete" above.
            var dropDownPanel = Assert.Single(panelHost.GetVisualDescendants().OfType<WinFormsToolStripDropDownPanel>());
            dropDownPanel.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });
            await WaitUntilAsync(() =>
                !designer.Text.Contains("OpenItem", StringComparison.Ordinal) && view.IsEnabled);

            Assert.Contains("SaveItem", designer.Text);
            Assert.Contains("ToolStripDropDownButton1", designer.Text);
            Assert.DoesNotContain("OpenItem", designer.Text);
            Assert.DoesNotContain("RecentItem", designer.Text);

            ClickUndo(view);
            await WaitUntilAsync(() => designer.Text == source && view.IsEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeleteKeyWhileTypingInTheMenuCaptionBoxDoesNotDeleteTheSelectedItem()
    {
        var editor = new WinFormsMenuDesignEditor();
        // Hosted in a real Window: a ScrollViewer's own Content (the dropdown's rows, holding
        // the caption TextBox) is only realised into the visual tree once its control template
        // is applied, which only happens once the control is attached to a live visual root.
        var window = new Window { Content = editor, Width = 400, Height = 400 };
        window.Show();
        try
        {
            var items = new[]
            {
                new WinFormsMenuItem("FileMenu", "MenuStrip1", "&File"),
                new WinFormsMenuItem("EditMenu", "MenuStrip1", "&Edit"),
            };
            editor.Show("MenuStrip1", items, 320);
            WinFormsMenuDesignChange? requested = null;
            editor.EditRequested += change => requested = change;
            Click(Find<Button>(editor, "MenuInlineItem_FileMenu"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("FileMenu", editor.SelectedItemName);
            var caption = Find<TextBox>(editor, "MenuInlineCaption");
            caption.Text = "&Files";

            // Raised on the focused TextBox itself, the same as a real keystroke: the KeyDown
            // bubbles up to the editor's own OnKeyDown override with Source still the TextBox -
            // the backstop WinFormsMenuDesignEditor.OnKeyDown checks explicitly, since the
            // TextBox's own text editing already consumes the key in the common case.
            caption.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

            Assert.Null(requested);
            Assert.Equal("FileMenu", editor.SelectedItemName);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeleteKeyWhileTypingInTheToolStripTypeHereBoxDoesNotDeleteTheSelectedItem()
    {
        var editor = new WinFormsToolStripDesignEditor();
        var window = new Window { Content = editor, Width = 400, Height = 400 };
        window.Show();
        try
        {
            var items = new[] { new WinFormsToolStripItem("ToolStripButton1", "ToolStripButton", "Save") };
            editor.Show("ToolStrip1", items, false, 320);
            string? requested = null;
            editor.DeleteItemRequested += name => requested = name;
            Click(Find<Button>(editor, "ToolStripInlineItem_ToolStripButton1"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("ToolStripButton1", editor.SelectedItemName);
            var typeHere = Find<TextBox>(editor, "ToolStripInlineTypeHere");
            typeHere.Text = "New";

            typeHere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

            Assert.Null(requested);
            Assert.Equal("ToolStripButton1", editor.SelectedItemName);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeleteKeyWhileTypingInTheDropDownPanelTypeHereBoxDoesNotDeleteTheSelectedItem()
    {
        var panel = new WinFormsToolStripDropDownPanel();
        var window = new Window { Content = panel, Width = 400, Height = 400 };
        window.Show();
        try
        {
            var items = new[] { new WinFormsMenuItem("OpenItem", "Owner1", "Open") };
            panel.Show("Owner1", items);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            WinFormsToolStripDropDownChange? requested = null;
            panel.EditRequested += change => requested = change;
            Click(Find<Button>(panel, "ToolStripDropDownItem_OpenItem"));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("OpenItem", panel.SelectedItemName);
            var typeHere = Find<TextBox>(panel, "ToolStripDropDownTypeHere");
            typeHere.Text = "Recent";

            typeHere.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete });

            Assert.Null(requested);
            Assert.Equal("OpenItem", panel.SelectedItemName);
        }
        finally
        {
            window.Close();
        }
    }

    private static WinFormsMenuDesignEditor MenuEditor(WinFormsDesignerView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<WinFormsMenuDesignEditor>());

    private static WinFormsToolStripDesignEditor ToolStripEditor(WinFormsDesignerView view) =>
        Assert.Single(view.GetVisualDescendants().OfType<WinFormsToolStripDesignEditor>());

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(root.GetVisualDescendants().OfType<T>(), control => control.Name == name);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static void ClickUndo(WinFormsDesignerView view)
    {
        var undo = Assert.Single(view.GetVisualDescendants().OfType<Button>(), button =>
            Equals(button.Content, Basalt.Core.Localization.Localizer.Get(
                Basalt.Core.Localization.StringKeys.MenuEditUndo)));
        Click(undo);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        // 20s: this shares WinFormsMenuInlineTests' rationale for the same deadline (every
        // AvaloniaFact test shares one dispatcher thread under a full-suite run, so a slow
        // neighbour test can starve this one's RunEditAsync of CPU for several seconds).
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The inline strip item deletion did not complete.");
    }
}
