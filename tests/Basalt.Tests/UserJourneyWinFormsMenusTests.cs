using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basalt.Core.Localization;
using Basalt.Designer.Toolbox;
using Basalt.Shell.Controls;
using Basalt.Shell.ViewModels;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// A user builds a menu bar and a tool strip in the WinForms designer of a real
/// <see cref="MainWindow"/>: double-clicks MenuStrip and ToolStrip in the Toolbox, types items
/// into each strip's own "Type Here" box with real text and Enter key input, clicks through the
/// inline editor's rows, renames an item in the Properties grid, deletes an item from its context menu,
/// undoes and redoes with the designer's own toolbar, then saves and reopens the form. The Delete key
/// after a plain click is covered by the two tests at the end of this file.
///
/// The menu's top-level item and its sub-items are typed into <see cref="WinFormsMenuDesignEditor"/>
/// (the inline editor a MenuStrip shows). <see cref="WinFormsToolStripDropDownPanel"/> is the panel
/// of a ToolStripDropDownButton, which this journey does not add.
///
/// Built loose (a Designer.vb and its code file, no project): the designer reads and writes the
/// same files Visual Studio does, and the journey needs no MSBuild evaluation.
/// </summary>
[Collection(nameof(WinFormsDesignerClipboardCollection))]
public sealed class UserJourneyWinFormsMenusTests : IDisposable
{
    public UserJourneyWinFormsMenusTests() => WinFormsDesignerView.ResetClipboardForTests();
    public void Dispose() => WinFormsDesignerView.ResetClipboardForTests();

    // An empty form, as a New Windows Forms project writes it: nothing on it to collide with
    // the strips the journey adds.
    private const string EmptyDesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub
        End Class
        """;

    private const string CodeSource = """
        Partial Class Form1
        End Class
        """;

    private sealed class Fixture : IDisposable
    {
        public required TestWindow Host { get; init; }
        public required MainWindowViewModel Vm { get; init; }
        public required WinFormsDesignSurface Surface { get; init; }
        public required string DesignerPath { get; init; }
        public required string Root { get; init; }

        public string DesignerText => Vm.OpenDocuments.Single(d => d.FilePath == DesignerPath).Text;

        public void Dispose()
        {
            Host.Dispose();
            try { Directory.Delete(Root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task<Fixture> OpenAsync(string? designerSource = null)
    {
        // A temporary folder holding only the form's two files: removed when the journey ends.
        var root = Directory.CreateTempSubdirectory("basalt-journey-menus-").FullName;
        var designerPath = Path.Combine(root, "Form1.Designer.vb");
        var codePath = Path.Combine(root, "Form1.vb");
        await File.WriteAllTextAsync(designerPath, designerSource ?? EmptyDesignerSource);
        await File.WriteAllTextAsync(codePath, CodeSource);

        var host = new TestWindow();
        var vm = (MainWindowViewModel)host.Window.DataContext!;
        await vm.OpenFileAsync(designerPath, inDesigner: true);
        await host.SettleAsync(10);

        var surface = Assert.Single(host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => surface.RootForm is not null);

        return new Fixture { Host = host, Vm = vm, Surface = surface, DesignerPath = designerPath, Root = root };
    }

    [AvaloniaFact]
    public async Task BuildingAMenuAndAToolStripTypedClickedRenamedDeletedUndoneSavedAndReopenedKeepsEveryItem()
    {
        using var f = await OpenAsync();
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());

        // 1. Toolbox: a double-click adds a MenuStrip, which becomes the selected control.
        await DoubleClickToolboxItemAsync(f, "MenuStrip");
        await WaitUntilAsync(() => f.Surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Any(), "The MenuStrip was not added.");
        var menu = LiveMenu(f);
        await WaitUntilAsync(() => ReferenceEquals(f.Surface.SelectedControl, menu), "The MenuStrip is not selected.");

        // 2. The top-level item, typed into the menu's own "Type Here" box.
        var top = await WaitForBoxAsync(f, "MenuInlineTopPlaceholder");
        TypeAndCommit(f, top, "&File");
        await AwaitDesignerAsync(f, view, "Me.ToolStripMenuItem1.Text = \"&File\"", "The top-level item was not written.");
        await WaitUntilAsync(() => Named<Button>(f, "MenuInlineItem_ToolStripMenuItem1") is not null, "The top-level item has no button.");

        // 3. Its sub-items: click the item to open its drop-down, then type under it, add a
        //    separator and type a last one. Each Rebuild replaces the child box, so it is found again.
        await ClickAsync(f, Named<Button>(f, "MenuInlineItem_ToolStripMenuItem1"));
        await WaitUntilAsync(() => Named<Border>(f, "MenuInlineDropdown")?.IsVisible == true, "The drop-down did not open.");
        TypeAndCommit(f, await WaitForBoxAsync(f, "MenuInlineChildPlaceholder"), "&Open");
        await AwaitDesignerAsync(f, view, "Me.ToolStripMenuItem2.Text = \"&Open\"", "The first sub-item was not written.");

        await ClickAsync(f, Named<Button>(f, "MenuInlineSeparator"));
        await AwaitDesignerAsync(f, view, "Friend WithEvents ToolStripSeparator1 As", "The separator was not written.");

        TypeAndCommit(f, await WaitForBoxAsync(f, "MenuInlineChildPlaceholder"), "E&xit");
        await AwaitDesignerAsync(f, view, "Me.ToolStripMenuItem3.Text = \"E&xit\"", "The last sub-item was not written.");

        // 4. Rename: the item's Text is the name a user sees and edits in the Properties grid.
        //    Name is read-only for a menu item (MenuItemReadOnlyProperties), so Text is the row.
        await ClickAsync(f, Named<Button>(f, "MenuInlineItem_ToolStripMenuItem1"));
        var grid = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<PropertyGrid>());
        await WaitUntilAsync(() => grid.Properties.Any(property => property.Name == "Text" && property.CurrentValue == "&File"),
            "The Properties grid did not show the File item's Text.");
        var textEditor = Assert.Single(FindRow(grid, "Text").GetVisualDescendants().OfType<TextBox>(),
            box => box.Name == "PropertyGridValue");
        Assert.True(textEditor.Focus(), "The Text value box did not take focus.");
        f.Host.Window.KeyPress(Key.End, RawInputModifiers.None, PhysicalKey.End, null);
        for (var i = 0; i < "&File".Length; i++)
        {
            f.Host.Window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);
        }
        f.Host.Window.KeyTextInput("&Archive");
        f.Host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        await AwaitDesignerAsync(f, view, "Me.ToolStripMenuItem1.Text = \"&Archive\"", "The rename did not reach the designer file.");
        await WaitUntilAsync(() => Named<Button>(f, "MenuInlineItem_ToolStripMenuItem1") is not null
            && LiveMenu(f).Items.Cast<Forms.ToolStripItem>().Any(item => item.Name == "ToolStripMenuItem1" && item.Text == "&Archive"),
            "The renamed item did not reach the live menu.");

        // 5. ToolStrip: a double-click adds it, then "Type Here" makes a button and the type
        //    picker's arrow makes a separator.
        await DoubleClickToolboxItemAsync(f, "ToolStrip");
        await WaitUntilAsync(() => f.Surface.SelectedControl is Forms.ToolStrip { } selected && selected is not Forms.MenuStrip,
            "The ToolStrip was not added and selected.");

        TypeAndCommit(f, await WaitForBoxAsync(f, "ToolStripInlineTypeHere"), "Save");
        await AwaitDesignerAsync(f, view, "Me.ToolStripButton1.Text = \"Save\"", "The button was not written.");

        await ClickAsync(f, Named<ToggleButton>(f, "ToolStripInlineTypeArrow"));
        await ClickAsync(f, Named<Button>(f, "ToolStripInlineType_ToolStripSeparator"));
        await AwaitDesignerAsync(f, view, "Friend WithEvents ToolStripSeparator2 As", "The ToolStrip separator was not written.");
        await WaitUntilAsync(() => Named<Button>(f, "ToolStripInlineItem_ToolStripSeparator2") is not null,
            "The ToolStrip separator has no button.");

        // 6. Delete: right-click the separator and choose Delete from its menu, as a user does.
        //    The Delete key after a click is covered by the two tests at the end of this file.
        var beforeDelete = f.DesignerText;
        var separatorButton = Named<Button>(f, "ToolStripInlineItem_ToolStripSeparator2")!;
        await RightClickAsync(f, separatorButton);
        Assert.True(separatorButton.ContextMenu?.IsOpen == true, "Right-clicking the separator did not open its menu.");
        var deleteEntry = Assert.Single(separatorButton.ContextMenu!.Items.OfType<MenuItem>(),
            entry => entry.Name == "ToolStripInlineDelete_ToolStripSeparator2");
        deleteEntry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await AwaitGoneAsync(f, view, "ToolStripSeparator2", "Delete did not remove the separator from the designer file.");
        var afterDelete = f.DesignerText;
        Assert.DoesNotContain("ToolStripInlineItem_ToolStripSeparator2",
            f.Host.Window.GetVisualDescendants().OfType<Button>().Select(button => button.Name));


        // 7. Undo returns the file to its text before Delete, redo removes the separator again.
        ClickDesignerButton(view, StringKeys.MenuEditUndo);
        await WaitUntilAsync(() => view.IsEnabled && f.DesignerText == beforeDelete, "Undo did not restore the separator.");
        Assert.Contains(LiveStrip(f).Items.Cast<Forms.ToolStripItem>(), item => item.Name == "ToolStripSeparator2");

        ClickDesignerButton(view, StringKeys.MenuEditRedo);
        await WaitUntilAsync(() => view.IsEnabled && f.DesignerText == afterDelete, "Redo did not remove the separator again.");

        // 8. Save, close the form and open it from disk: the file keeps every item, and the
        //    reopened designer shows the same items.
        await f.Vm.SaveCommand.ExecuteAsync(null);
        var saved = await File.ReadAllTextAsync(f.DesignerPath);
        Assert.Equal(afterDelete, saved);
        AssertVisualStudioFormat(saved);

        f.Vm.CloseActiveDocument();
        await f.Host.SettleAsync(3);
        await f.Vm.OpenFileAsync(f.DesignerPath, inDesigner: true);
        await f.Host.SettleAsync(5);

        var reopened = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignSurface>());
        await WaitUntilAsync(() => reopened.RootForm is not null, "The reopened form did not appear.");
        Assert.Equal(saved, f.DesignerText);

        var reopenedMenu = Assert.Single(reopened.RootForm!.Controls.OfType<Forms.MenuStrip>());
        Assert.Equal(new[] { "ToolStripMenuItem1" }, reopenedMenu.Items.Cast<Forms.ToolStripItem>().Select(item => item.Name));
        var file = Assert.IsType<Forms.ToolStripMenuItem>(reopenedMenu.Items[0]);
        Assert.Equal("&Archive", file.Text);
        Assert.Equal(
            new[] { "ToolStripMenuItem2", "ToolStripSeparator1", "ToolStripMenuItem3" },
            file.DropDownItems.Cast<Forms.ToolStripItem>().Select(item => item.Name));
        Assert.Single(file.DropDownItems.OfType<Forms.ToolStripSeparator>());
        Assert.Equal(
            new[] { "&Open", "E&xit" },
            file.DropDownItems.OfType<Forms.ToolStripMenuItem>().Select(item => item.Text));

        var reopenedStrip = Assert.Single(reopened.RootForm.Controls.OfType<Forms.ToolStrip>(),
            control => control is not Forms.MenuStrip);
        Assert.Equal(new[] { "ToolStripButton1" }, reopenedStrip.Items.Cast<Forms.ToolStripItem>().Select(item => item.Name));
        Assert.Equal("Save", reopenedStrip.Items[0].Text);

        // The reopened menu's editor lists the same items, so the user sees what was saved.
        reopened.Select(reopenedMenu);
        await WaitUntilAsync(() => Named<Button>(f, "MenuInlineItem_ToolStripMenuItem1") is not null,
            "The reopened menu's editor does not list its top-level item.");
    }

    /// <summary>Visual Studio's layout for the saved items: each item has its field, its
    /// Name and Text, and each collection is one AddRange whose items keep their display order.
    /// A top-level item goes into its strip's Items; a sub-item into its owner's DropDownItems.</summary>
    private static void AssertVisualStudioFormat(string saved)
    {
        var lines = saved.Split('\n').Select(line => line.Trim()).ToArray();

        Assert.Contains("Friend WithEvents ToolStripMenuItem1 As Global.System.Windows.Forms.ToolStripMenuItem", lines);
        Assert.Contains("Friend WithEvents ToolStripMenuItem2 As Global.System.Windows.Forms.ToolStripMenuItem", lines);
        Assert.Contains("Friend WithEvents ToolStripSeparator1 As Global.System.Windows.Forms.ToolStripSeparator", lines);
        Assert.Contains("Friend WithEvents ToolStripMenuItem3 As Global.System.Windows.Forms.ToolStripMenuItem", lines);
        Assert.Contains("Friend WithEvents ToolStripButton1 As Global.System.Windows.Forms.ToolStripButton", lines);
        Assert.DoesNotContain(lines, line => line.Contains("ToolStripSeparator2", StringComparison.Ordinal));

        Assert.Contains("Me.ToolStripMenuItem1.Name = \"ToolStripMenuItem1\"", lines);
        Assert.Contains("Me.ToolStripMenuItem1.Text = \"&Archive\"", lines);
        Assert.Contains("Me.ToolStripMenuItem2.Text = \"&Open\"", lines);
        Assert.Contains("Me.ToolStripMenuItem3.Text = \"E&xit\"", lines);
        Assert.Contains("Me.ToolStripButton1.Text = \"Save\"", lines);

        var topLevel = Array.IndexOf(lines, "Me.MenuStrip1.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem1})");
        var subItems = Array.IndexOf(lines, "Me.ToolStripMenuItem1.DropDownItems.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem2, Me.ToolStripSeparator1, Me.ToolStripMenuItem3})");
        var strip = Array.IndexOf(lines, "Me.ToolStrip1.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripButton1})");
        Assert.True(topLevel >= 0, "The menu's top-level AddRange is not in VS form.");
        Assert.True(subItems >= 0, "The File item's DropDownItems AddRange is not in VS form.");
        Assert.True(strip >= 0, "The ToolStrip's Items AddRange is not in VS form.");
        Assert.True(topLevel < subItems, "Visual Studio writes the top-level Items AddRange before the DropDownItems AddRange.");
    }

    // ------------------------------------------------------------------
    // The Delete key after a plain click on an item. A click selects the item and rebuilds the bar;
    // the editors give keyboard focus back to the rebuilt button, so Delete reaches the editor. The
    // journey above deletes through the context menu; these two tests cover the keyboard path.
    // ------------------------------------------------------------------

    private const string ToolStripDesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.ToolStrip1 = New System.Windows.Forms.ToolStrip()
                Me.ToolStripButton1 = New System.Windows.Forms.ToolStripButton()
                Me.ToolStripSeparator1 = New System.Windows.Forms.ToolStripSeparator()
                Me.ToolStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripButton1, Me.ToolStripSeparator1})
                Me.ToolStripButton1.Text = "Save"
                Me.Controls.Add(Me.ToolStrip1)
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents ToolStrip1 As System.Windows.Forms.ToolStrip
            Friend WithEvents ToolStripButton1 As System.Windows.Forms.ToolStripButton
            Friend WithEvents ToolStripSeparator1 As System.Windows.Forms.ToolStripSeparator
        End Class
        """;

    private const string MenuDesignerSource = """
        Partial Class Form1
            Inherits System.Windows.Forms.Form

            Private Sub InitializeComponent()
                Me.MenuStrip1 = New System.Windows.Forms.MenuStrip()
                Me.ToolStripMenuItem1 = New System.Windows.Forms.ToolStripMenuItem()
                Me.ToolStripMenuItem2 = New System.Windows.Forms.ToolStripMenuItem()
                Me.MenuStrip1.Items.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem1})
                Me.ToolStripMenuItem1.DropDownItems.AddRange(New System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem2})
                Me.ToolStripMenuItem1.Text = "&File"
                Me.ToolStripMenuItem2.Text = "&Open"
                Me.Controls.Add(Me.MenuStrip1)
                Me.ClientSize = New System.Drawing.Size(500, 400)
                Me.Text = "Form1"
                Me.ResumeLayout(False)
            End Sub

            Friend WithEvents MenuStrip1 As System.Windows.Forms.MenuStrip
            Friend WithEvents ToolStripMenuItem1 As System.Windows.Forms.ToolStripMenuItem
            Friend WithEvents ToolStripMenuItem2 As System.Windows.Forms.ToolStripMenuItem
        End Class
        """;

    [AvaloniaFact]
    public async Task DeleteKeyAfterClickingAToolStripItemRemovesIt()
    {
        using var f = await OpenAsync(ToolStripDesignerSource);
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());
        f.Surface.Select(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));
        var separator = await WaitForNamedAsync<Button>(f, "ToolStripInlineItem_ToolStripSeparator1");
        await ClickAsync(f, separator);

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await AwaitGoneAsync(f, view, "ToolStripSeparator1", "Delete did not remove the separator after a click selected it.");
    }

    [AvaloniaFact]
    public async Task DeleteKeyAfterClickingAMenuItemRemovesIt()
    {
        using var f = await OpenAsync(MenuDesignerSource);
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());
        f.Surface.Select(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));
        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "MenuInlineItem_ToolStripMenuItem1"));
        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "MenuInlineItem_ToolStripMenuItem2"));

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await AwaitGoneAsync(f, view, "ToolStripMenuItem2", "Delete did not remove the sub-item after a click selected it.");
    }

    // ------------------------------------------------------------------
    // The Delete key after the other clicks that rebuild an editor: Back, the type-picker arrow, and
    // a child of a ToolStripDropDownButton. Each rebuilds the rows, so each gives the focus back.
    // ------------------------------------------------------------------

    /// <summary>A sub-item opened with its chevron, then Back: the sub-item is selected again in its
    /// parent's drop-down, and Delete removes it.</summary>
    [AvaloniaFact]
    public async Task DeleteKeyAfterGoingBackRemovesTheSelectedSubItem()
    {
        using var f = await OpenAsync(MenuDesignerSource);
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());
        f.Surface.Select(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.MenuStrip>()));

        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "MenuInlineItem_ToolStripMenuItem1"));
        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "MenuInlineOpen_ToolStripMenuItem2"));
        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "MenuInlineBack"));
        await WaitUntilAsync(() => Named<Button>(f, "MenuInlineItem_ToolStripMenuItem2") is not null,
            "Back did not show Open again in File's drop-down.");

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await AwaitGoneAsync(f, view, "ToolStripMenuItem2", "Delete did not remove Open after Back selected it.");
        Assert.Empty(((Forms.ToolStripMenuItem)LiveMenu(f).Items[0]).DropDownItems);
    }

    /// <summary>A separator selected, then the type-picker arrow opened: the separator is still
    /// selected, and Delete removes it.</summary>
    [AvaloniaFact]
    public async Task DeleteKeyAfterOpeningTheTypePickerRemovesTheSelectedItem()
    {
        using var f = await OpenAsync(ToolStripDesignerSource);
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());
        f.Surface.Select(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "ToolStripInlineItem_ToolStripSeparator1"));
        await ClickAsync(f, await WaitForNamedAsync<ToggleButton>(f, "ToolStripInlineTypeArrow"));
        await WaitUntilAsync(() => Named<Button>(f, "ToolStripInlineItem_ToolStripSeparator1") is not null,
            "The arrow did not rebuild the strip's bar.");

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await AwaitGoneAsync(f, view, "ToolStripSeparator1", "Delete did not remove the separator after the arrow was clicked.");
    }

    /// <summary>A ToolStripDropDownButton with two children typed into its own drop-down; one child is
    /// clicked, Delete removes it from the designer, and the saved Designer.vb no longer holds it.</summary>
    [AvaloniaFact]
    public async Task DeleteKeyAfterClickingADropDownChildRemovesItFromTheDesignerAndTheFile()
    {
        using var f = await OpenAsync(ToolStripDesignerSource);
        var view = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<WinFormsDesignerView>());
        f.Surface.Select(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.ToolStrip>()));

        await ClickAsync(f, await WaitForNamedAsync<ToggleButton>(f, "ToolStripInlineTypeArrow"));
        await ClickAsync(f, await WaitForNamedAsync<Button>(f, "ToolStripInlineType_ToolStripDropDownButton"));
        await AwaitDesignerAsync(f, view, "Friend WithEvents ToolStripDropDownButton1 As",
            "The DropDownButton was not written.");

        TypeAndCommit(f, await WaitForNamedAsync<TextBox>(f, "ToolStripDropDownTypeHere"), "&One");
        await AwaitDesignerAsync(f, view, "Text = \"&One\"", "The first child was not written.");
        TypeAndCommit(f, await WaitForNamedAsync<TextBox>(f, "ToolStripDropDownTypeHere"), "&Two");
        await AwaitDesignerAsync(f, view, "Text = \"&Two\"", "The second child was not written.");

        var one = await WaitForDropDownChildAsync(f, "One");
        var oneName = one.Name!["ToolStripDropDownItem_".Length..];
        await ClickAsync(f, one);

        f.Host.Window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        await AwaitGoneAsync(f, view, oneName, "Delete did not remove the clicked child from the designer.");
        var owner = Assert.Single(Assert.Single(f.Surface.RootForm!.Controls.OfType<Forms.ToolStrip>()).Items
            .OfType<Forms.ToolStripDropDownButton>());
        Assert.Equal(new[] { "&Two" }, owner.DropDownItems.OfType<Forms.ToolStripMenuItem>().Select(item => item.Text));

        await f.Vm.SaveCommand.ExecuteAsync(null);
        var saved = await File.ReadAllTextAsync(f.DesignerPath);
        Assert.DoesNotContain(oneName, saved);
        Assert.Contains("Text = \"&Two\"", saved);
    }

    private static async Task<Button> WaitForDropDownChildAsync(Fixture f, string text)
    {
        Button? found = null;
        await WaitUntilAsync(() => (found = f.Host.Window.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(button => button.Name?.StartsWith("ToolStripDropDownItem_", StringComparison.Ordinal) == true
                && button.Content is string content && content.Contains(text, StringComparison.Ordinal))) is not null,
            "No drop-down child shows " + text + ".");
        return found!;
    }

    // ------------------------------------------------------------------
    // Helpers: each one does what a person does, and waits for what the person would see.
    // ------------------------------------------------------------------

    /// <summary>A double-click on a Toolbox row. A pause first, so the press is not taken for the
    /// second click of an earlier double-click; the list virtualises its rows, so the row is
    /// scrolled to as a user would scroll to it.</summary>
    private static async Task DoubleClickToolboxItemAsync(Fixture f, string displayName)
    {
        await Task.Delay(700);
        var list = Assert.Single(f.Host.Window.GetVisualDescendants().OfType<ListBox>(),
            box => box.ItemsSource?.Cast<object>().OfType<WinFormsToolboxItem>().Any(item => item.DisplayName == displayName) == true);
        var item = list.ItemsSource!.Cast<object>().OfType<WinFormsToolboxItem>().Single(entry => entry.DisplayName == displayName);
        list.ScrollIntoView(item);
        f.Host.Window.UpdateLayout();
        var row = Assert.Single(list.GetVisualDescendants().OfType<ListBoxItem>(),
            candidate => ReferenceEquals(candidate.DataContext, item));
        row.BringIntoView();
        f.Host.Window.UpdateLayout();
        var bounds = Raster.BoundsIn(row, f.Host.Window);
        var at = new Point(bounds.X + 10, bounds.Y + bounds.Height / 2.0);

        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>A left click with the mouse, at the centre of the control as it is on screen. A
    /// pause first: a second press close in time and place is a double-click, which in the menu
    /// editor opens the item's default event in code.</summary>
    /// <summary>A right click, which opens the control's own context menu as it does for a user.</summary>
    private static async Task RightClickAsync(Fixture f, Control control)
    {
        Assert.NotNull(control);
        await Task.Delay(700);
        f.Host.Window.UpdateLayout();
        control.BringIntoView();
        f.Host.Window.UpdateLayout();
        var bounds = Raster.BoundsIn(control, f.Host.Window);
        var at = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        f.Host.Window.MouseDown(at, MouseButton.Right);
        f.Host.Window.MouseUp(at, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task ClickAsync(Fixture f, Control? control)
    {
        Assert.NotNull(control);
        await Task.Delay(700);
        f.Host.Window.UpdateLayout();
        control.BringIntoView();
        f.Host.Window.UpdateLayout();
        var bounds = Raster.BoundsIn(control, f.Host.Window);
        var at = new Point(bounds.X + bounds.Width / 2.0, bounds.Y + bounds.Height / 2.0);
        f.Host.Window.MouseDown(at, MouseButton.Left);
        f.Host.Window.MouseUp(at, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Focuses a text box and types text with the keyboard, then presses Enter, as a
    /// person does in a "Type Here" box. Focus must be taken, or the keys go nowhere.</summary>
    private static void TypeAndCommit(Fixture f, TextBox box, string text)
    {
        Assert.True(box.Focus(), "The Type Here box did not take focus.");
        f.Host.Window.KeyTextInput(text);
        f.Host.Window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickDesignerButton(WinFormsDesignerView view, string key)
    {
        var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(),
            candidate => Equals(candidate.Content, Localizer.Get(key)));
        button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    private static Forms.MenuStrip LiveMenu(Fixture f) =>
        f.Surface.RootForm!.Controls.OfType<Forms.MenuStrip>().Single();

    private static Forms.ToolStrip LiveStrip(Fixture f) =>
        f.Surface.RootForm!.Controls.OfType<Forms.ToolStrip>().Single(control => control is not Forms.MenuStrip);

    private static T? Named<T>(Fixture f, string name) where T : Control =>
        f.Host.Window.GetVisualDescendants().OfType<T>().SingleOrDefault(control => control.Name == name);

    private static async Task<TextBox> WaitForBoxAsync(Fixture f, string name)
    {
        await WaitUntilAsync(() => Named<TextBox>(f, name) is not null, "The box " + name + " did not appear.");
        return Named<TextBox>(f, name)!;
    }

    private static async Task<T> WaitForNamedAsync<T>(Fixture f, string name) where T : Control
    {
        await WaitUntilAsync(() => Named<T>(f, name) is not null, name + " did not appear.");
        return Named<T>(f, name)!;
    }

    private static Border FindRow(PropertyGrid grid, string label) =>
        Assert.Single(grid.Rows.OfType<Border>(), row => row.Name == "PropertyGridRow"
            && row.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == label));

    private static async Task AwaitDesignerAsync(Fixture f, WinFormsDesignerView view, string expected, string what)
    {
        await WaitUntilAsync(() => view.IsEnabled && f.DesignerText.Contains(expected, StringComparison.Ordinal), what);
    }

    private static async Task AwaitGoneAsync(Fixture f, WinFormsDesignerView view, string name, string what)
    {
        await WaitUntilAsync(() => view.IsEnabled && !f.DesignerText.Contains(name, StringComparison.Ordinal), what);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string? what = null)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
        Assert.True(condition(), "The designer did not reach the expected state in time" + (what is null ? "." : ": " + what));
    }
}
