using Avalonia.Headless.XUnit;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

public sealed class WinFormsMenuDocumentTests
{
    private const string Source = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                Me.MenuStrip1.Name = "MenuStrip1"
                Me.Controls.Add(Me.MenuStrip1)
                ' Preserve unrelated application initialization.
            End Sub
            Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
        End Class
        """;

    [Theory]
    [InlineData("Public Property ToolStripMenuItem1 As String")]
    [InlineData("Public Sub ToolStripMenuItem1()\n    End Sub")]
    public void NewItemsCannotCollideWithExistingMembers(string member)
    {
        var source = Source.Replace("End Class", member + "\nEnd Class", StringComparison.Ordinal);
        var document = new WinFormsMenuDocument(source);
        Assert.Contains("ToolStripMenuItem1", document.DeclaredNames);
        Assert.Throws<ArgumentException>(() => document.Set("MenuStrip1",
            [new("ToolStripMenuItem1", "MenuStrip1", "File")]));
    }

    [AvaloniaFact]
    public void MenuStripItemsExposeTheCollectionEditor()
    {
        using var menu = new Forms.MenuStrip();
        var property = Assert.Single(Basalt.Designer.WinFormsPropertyInspector.DescribeDesigner(menu), property => property.Name == "Items");
        Assert.True(property.IsCollection);
        Assert.True(property.HasCustomEditor);
        Assert.False(property.IsReadOnly);
        using var toolbar = new Forms.ToolStrip();
        var toolbarItems = Assert.Single(Basalt.Designer.WinFormsPropertyInspector.DescribeDesigner(toolbar), property => property.Name == "Items");
        Assert.False(toolbarItems.HasCustomEditor);
    }

    [AvaloniaFact]
    public async Task MenuTreeRoundTripsAndParticipatesInOneUndo()
    {
        using var session = new WinFormsDesignerSession(new(Source));
        var definitions = new WinFormsMenuItem[]
        {
            new("FileMenu", "MenuStrip1", "&File"),
            new("OpenMenu", "FileMenu", "&Open"),
            new("Separator1", "FileMenu", "", true, "ToolStripSeparator"),
            new("ExitMenu", "FileMenu", "E&xit"),
        };
        await session.ApplyCollectionAsync(document => new WinFormsMenuDocument(document.Source).Set("MenuStrip1", definitions));
        Assert.Equal(definitions, new WinFormsMenuDocument(session.Document.Source).Read("MenuStrip1"));
        Assert.Contains("' Preserve unrelated application initialization.", session.Document.Source);
        var menu = Assert.IsType<Forms.MenuStrip>(session.FindControl("MenuStrip1"));
        var file = Assert.IsType<Forms.ToolStripMenuItem>(Assert.Single(menu.Items.Cast<Forms.ToolStripItem>()));
        Assert.Equal("&File", file.Text);
        Assert.Equal(3, file.DropDownItems.Count);
        Assert.Empty(session.PreviewProblems);
        await session.UndoAsync();
        Assert.Equal(Source, session.Document.Source);
        await session.RedoAsync();
        Assert.Equal(definitions, new WinFormsMenuDocument(session.Document.Source).Read("MenuStrip1"));
    }

    private const string ToolStripSource = """
        Partial Class MainForm
            Inherits Global.System.Windows.Forms.Form
            Private Sub InitializeComponent()
                Me.ToolStripDropDownButton1 = New Global.System.Windows.Forms.ToolStripDropDownButton()
                Me.ToolStripDropDownButton1.Name = "ToolStripDropDownButton1"
                Me.ToolStrip1 = New Global.System.Windows.Forms.ToolStrip()
                Me.ToolStrip1.Name = "ToolStrip1"
                Me.ToolStrip1.Items.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripDropDownButton1})
                Me.Controls.Add(Me.ToolStrip1)
                ' Preserve unrelated application initialization.
            End Sub
            Friend WithEvents ToolStrip1 As Global.System.Windows.Forms.ToolStrip
            Friend WithEvents ToolStripDropDownButton1 As Global.System.Windows.Forms.ToolStripDropDownButton
        End Class
        """;

    // A ToolStripDropDownButton (or ToolStripSplitButton) is a flat item of its owning
    // ToolStrip's own Items collection (WinFormsToolStripDocument's job), but it also owns a
    // cascading DropDownItems tree edited the same way a MenuStrip's is: this proves
    // WinFormsMenuDocument, not a third document type, now handles that tree too.
    [AvaloniaFact]
    public async Task DropDownButtonOwnsItsOwnDropDownItemsTreeLikeAMenuStripDoes()
    {
        using var session = new WinFormsDesignerSession(new(ToolStripSource));
        var definitions = new WinFormsMenuItem[]
        {
            new("ToolStripMenuItem1", "ToolStripDropDownButton1", "Open"),
            new("ToolStripSeparator1", "ToolStripDropDownButton1", "", true, "ToolStripSeparator"),
            new("ToolStripComboBox1", "ToolStripDropDownButton1", "", TypeName: "ToolStripComboBox"),
            new("ToolStripMenuItem2", "ToolStripMenuItem1", "Recent"),
        };
        await session.ApplyCollectionAsync(document =>
            new WinFormsMenuDocument(document.Source).Set("ToolStripDropDownButton1", definitions));

        Assert.Contains(
            "Me.ToolStripDropDownButton1.DropDownItems.AddRange(New Global.System.Windows.Forms.ToolStripItem() " +
            "{Me.ToolStripMenuItem1, Me.ToolStripSeparator1, Me.ToolStripComboBox1})",
            session.Document.Source);
        Assert.Contains(
            "Me.ToolStripMenuItem1.DropDownItems.AddRange(New Global.System.Windows.Forms.ToolStripItem() {Me.ToolStripMenuItem2})",
            session.Document.Source);
        Assert.DoesNotContain("Me.ToolStripComboBox1.Text", session.Document.Source);
        // Read() walks depth first, so a nested item's own children come right after it,
        // ahead of its remaining siblings.
        Assert.Equal(
            new[] { definitions[0], definitions[3], definitions[1], definitions[2] },
            new WinFormsMenuDocument(session.Document.Source).Read("ToolStripDropDownButton1"));

        var strip = Assert.IsType<Forms.ToolStrip>(session.FindControl("ToolStrip1"));
        var dropDown = Assert.IsType<Forms.ToolStripDropDownButton>(Assert.Single(strip.Items.Cast<Forms.ToolStripItem>()));
        Assert.Equal(3, dropDown.DropDownItems.Count);
        var open = Assert.IsType<Forms.ToolStripMenuItem>(dropDown.DropDownItems[0]);
        Assert.Equal("Open", open.Text);
        Assert.Single(open.DropDownItems.Cast<Forms.ToolStripItem>());
        Assert.IsType<Forms.ToolStripSeparator>(dropDown.DropDownItems[1]);
        Assert.IsType<Forms.ToolStripComboBox>(dropDown.DropDownItems[2]);

        await session.UndoAsync();
        Assert.Equal(ToolStripSource, session.Document.Source);
    }

    [Fact]
    public void ItemsAndTheirPropertiesAreWrittenBeforeTheFormResumesLayout()
    {
        // Visual Studio writes every item inside the form's SuspendLayout block.
        var source = Source
            .Replace("Me.MenuStrip1.Name", "Me.SuspendLayout()\n        Me.MenuStrip1.Name", StringComparison.Ordinal)
            .Replace("    End Sub", "    Me.ResumeLayout(False)\n        Me.PerformLayout()\n    End Sub", StringComparison.Ordinal);

        var withItem = new WinFormsMenuDocument(source).Set("MenuStrip1", [new("FileMenu", "MenuStrip1", "&File")]);
        var withProperty = new WinFormsMenuDocument(withItem).SetItemProperty("MenuStrip1", "FileMenu", "ShortcutKeyDisplayString", "\"F\"");

        var resume = withProperty.IndexOf("Me.ResumeLayout(False)", StringComparison.Ordinal);

        Assert.InRange(withProperty.IndexOf("Me.FileMenu = New", StringComparison.Ordinal), 0, resume);
        Assert.InRange(withProperty.IndexOf("Me.MenuStrip1.Items.AddRange(", StringComparison.Ordinal), 0, resume);
        Assert.InRange(withProperty.IndexOf("Me.FileMenu.ShortcutKeyDisplayString = ", StringComparison.Ordinal), 0, resume);
    }

    [Fact]
    public void RemoveItemDeletesTheWholeSubtreeAndTrimsOnlyMatchingHandlesClauses()
    {
        var withItems = new WinFormsMenuDocument(Source).Set("MenuStrip1",
        [
            new("FileMenu", "MenuStrip1", "&File"),
            new("OpenMenu", "FileMenu", "&Open"),
            new("ExitMenu", "FileMenu", "E&xit"),
            new("HelpMenu", "MenuStrip1", "&Help"),
        ]);
        withItems = withItems.Replace("End Class",
            "Private Sub Shared_Click(sender As Object, e As System.EventArgs) " +
            "Handles OpenMenu.Click, HelpMenu.Click\nEnd Sub\nEnd Class", StringComparison.Ordinal);

        var removal = new WinFormsMenuDocument(withItems).RemoveItem("MenuStrip1", "FileMenu");

        Assert.Equal(new HashSet<string> { "FileMenu", "OpenMenu", "ExitMenu" }, removal.RemovedNames);
        var result = removal.Source;
        Assert.DoesNotContain("FileMenu", result);
        Assert.DoesNotContain("OpenMenu", result);
        Assert.DoesNotContain("ExitMenu", result);
        Assert.Contains("HelpMenu", result);
        Assert.Contains("Private Sub Shared_Click", result);
        Assert.DoesNotContain("OpenMenu.Click", result);
        Assert.Contains("Handles HelpMenu.Click", result);
        Assert.Equal(new[] { new WinFormsMenuItem("HelpMenu", "MenuStrip1", "&Help") },
            new WinFormsMenuDocument(result).Read("MenuStrip1"));
    }

    /// <summary>
    /// One declaration naming a removed item and a surviving one cannot be deleted
    /// line-wise without losing the survivor's field: the delete is refused with the
    /// reason, before anything is rewritten.
    /// </summary>
    [Fact]
    public void DeletingAnItemDeclaredTogetherWithASurvivorIsRefused()
    {
        var withItems = new WinFormsMenuDocument(Source).Set("MenuStrip1", [
            new("FileMenu", "MenuStrip1", "&File"),
            new("HelpMenu", "MenuStrip1", "&Help"),
        ]);
        var shared = System.Text.RegularExpressions.Regex.Replace(withItems,
            @"\s*Friend WithEvents FileMenu As (?<type>[\w.]+)\s*Friend WithEvents HelpMenu As [\w.]+",
            m => $"\n    Friend WithEvents FileMenu, HelpMenu As {m.Groups["type"].Value}");
        Assert.Contains("Friend WithEvents FileMenu, HelpMenu As", shared);

        var refused = Assert.Throws<NotSupportedException>(
            () => new WinFormsMenuDocument(shared).RemoveItem("MenuStrip1", "FileMenu"));

        Assert.Equal(Basalt.Core.Localization.Localizer.Get(
            Basalt.Core.Localization.StringKeys.WinFormsMenuSplitSharedFieldDeleteItem), refused.Message);
    }

    [Fact]
    public void RemoveItemLeavesASeparatorAloneWhenOnlyItsSiblingIsDeleted()
    {
        var withItems = new WinFormsMenuDocument(Source).Set("MenuStrip1",
        [
            new("FileMenu", "MenuStrip1", "&File"),
            new("OpenMenu", "FileMenu", "&Open"),
            new("Separator1", "FileMenu", "", true, "ToolStripSeparator"),
            new("ExitMenu", "FileMenu", "E&xit"),
        ]);

        var removal = new WinFormsMenuDocument(withItems).RemoveItem("MenuStrip1", "OpenMenu");

        Assert.Equal(new HashSet<string> { "OpenMenu" }, removal.RemovedNames);
        Assert.Equal(
        [
            new WinFormsMenuItem("FileMenu", "MenuStrip1", "&File"),
            new WinFormsMenuItem("Separator1", "FileMenu", "", true, "ToolStripSeparator"),
            new WinFormsMenuItem("ExitMenu", "FileMenu", "E&xit"),
        ], new WinFormsMenuDocument(removal.Source).Read("MenuStrip1"));
    }

    [Fact]
    public void RejectsCyclesAndDynamicTextWithoutRewritingSource()
    {
        var menu = new WinFormsMenuDocument(Source);
        Assert.Throws<ArgumentException>(() => menu.Set("MenuStrip1", [new("One", "Two", "1"), new("Two", "One", "2")]));
        var generated = menu.Set("MenuStrip1", [new("One", "MenuStrip1", "1")]);
        var dynamic = generated.Replace("Me.One.Text = \"1\"", "Me.One.Text = GetText()", StringComparison.Ordinal);
        Assert.Throws<NotSupportedException>(() => new WinFormsMenuDocument(dynamic).Read("MenuStrip1"));
    }

    // Regression for a form whose InitializeComponent initializes one MenuStrip's Items with
    // something other than a single constant Add or array-literal AddRange (here, a call to a
    // method rather than an array literal). Previously WinFormsMenuDocument's constructor threw
    // for this shape, so WinFormsDesignerDocument's constructor threw too and the whole form
    // failed to open in the designer, even though every other control was perfectly readable.
    [Fact]
    public void UnusualMenuItemsInitializationKeepsFormOpenAndOtherControlsReadable()
    {
        const string source = """
            Partial Class MainForm
                Inherits Global.System.Windows.Forms.Form
                Private Sub InitializeComponent()
                    Me.MenuStrip1 = New Global.System.Windows.Forms.MenuStrip()
                    Me.MenuStrip1.Name = "MenuStrip1"
                    Me.MenuStrip1.Items.AddRange(BuildItems())
                    Me.Panel1 = New Global.System.Windows.Forms.Panel()
                    Me.Controls.Add(Me.MenuStrip1)
                    Me.Controls.Add(Me.Panel1)
                End Sub
                Friend WithEvents MenuStrip1 As Global.System.Windows.Forms.MenuStrip
                Friend WithEvents Panel1 As Global.System.Windows.Forms.Panel
                Private Function BuildItems() As Global.System.Windows.Forms.ToolStripItem()
                    Return New Global.System.Windows.Forms.ToolStripItem() {}
                End Function
            End Class
            """;

        var document = new WinFormsDesignerDocument(source);

        Assert.Contains("Panel1", document.Controls.Select(control => control.Name));
        Assert.False(document.Menus.IsSupported("MenuStrip1"));
        Assert.NotNull(document.Menus.UnsupportedReason("MenuStrip1"));
        Assert.Throws<NotSupportedException>(() => document.Menus.Read("MenuStrip1"));
    }
}
