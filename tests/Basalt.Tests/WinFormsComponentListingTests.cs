using System.ComponentModel;
using System.Drawing.Design;
using Basalt.Shell.Controls;
using Basalt.Workspace;
using Forms = System.Windows.Forms;

namespace Basalt.Tests;

/// <summary>
/// Which components the toolbox lists, and how the designer classifies the hand-written Visual Studio layouts of the
/// round-trip corpus: a SplitContainer's panels, a TableLayoutPanel's cells, a ListView's columns, and a components container.
/// </summary>
public sealed class WinFormsComponentListingTests
{
    public sealed class PlainComponent : Component { }

    [ToolboxItem(true)]
    public sealed class MarkedComponent : Component { }

    [DesignerCategory("Component")]
    public sealed class CategorizedComponent : Component { }

    public sealed class CustomMenuItem : Forms.ToolStripMenuItem { }

    public sealed class CustomColumn : Forms.DataGridViewTextBoxColumn { }

    public sealed class CustomButtonBar : Forms.ToolStripButton { }

    /// <summary>A project's own component is listed; an item of a Forms collection is never listed as a component.</summary>
    /// <remarks>
    /// Red-proof: with IsCollectionItem answering false, CustomMenuItem, CustomColumn and CustomButtonBar are listed and the
    /// DoesNotContain assertions fail.
    /// </remarks>
    [Fact]
    public void AProjectComponentIsListedAndAnItemOfACollectionIsNot()
    {
        Assert.True(WinFormsDesignTypes.IsListedComponent(typeof(PlainComponent), projectOwned: true));
        Assert.False(WinFormsDesignTypes.IsListedComponent(typeof(CustomMenuItem), projectOwned: true));
        Assert.False(WinFormsDesignTypes.IsListedComponent(typeof(CustomColumn), projectOwned: true));
        Assert.False(WinFormsDesignTypes.IsListedComponent(typeof(CustomButtonBar), projectOwned: true));
    }

    /// <summary>A library's component is listed only when it marks itself with ToolboxItem(true) or DesignerCategory("Component").</summary>
    /// <remarks>
    /// Red-proof: with MarksItselfAsComponent answering true for every type, PlainComponent is listed from a library and the
    /// first assertion fails. With IsCollectionItem removed, the last assertion fails.
    /// </remarks>
    [Fact]
    public void ALibraryComponentIsListedOnlyWhenItMarksItselfAsOne()
    {
        Assert.False(WinFormsDesignTypes.IsListedComponent(typeof(PlainComponent), projectOwned: false));
        Assert.True(WinFormsDesignTypes.IsListedComponent(typeof(MarkedComponent), projectOwned: false));
        Assert.True(WinFormsDesignTypes.IsListedComponent(typeof(CategorizedComponent), projectOwned: false));
        Assert.False(WinFormsDesignTypes.IsListedComponent(typeof(CustomMenuItem), projectOwned: false));
    }

    /// <summary>The SplitContainer's children are placed in their panels, so their parent is the container and their panel is named.</summary>
    /// <remarks>
    /// Red-proof: with OwnerOf answering the form for every collection, Button1 has the parent Me and no panel, and the
    /// assertions on ParentName and ParentPanel fail.
    /// </remarks>
    [Fact]
    public void AHandWrittenSplitContainerPlacesEachChildInItsPanel()
    {
        var document = new WinFormsDesignerDocument(ReadFixture("SplitContainer.Designer.vb"));

        var button = Assert.Single(document.Controls, control => control.Name == "Button1");
        Assert.Equal("SplitContainer1", button.ParentName);
        Assert.Equal("Panel1", button.ParentPanel);
        var text = Assert.Single(document.Controls, control => control.Name == "TextBox1");
        Assert.Equal("SplitContainer1", text.ParentName);
        Assert.Equal("Panel2", text.ParentPanel);
        Assert.Equal("Me", Assert.Single(document.Controls, control => control.Name == "SplitContainer1").ParentName);
        Assert.Empty(document.Components);
    }

    /// <summary>
    /// The TableLayoutPanel's children keep their cells, the ListView's ColumnHeader is an item of its Columns and neither a control
    /// nor a tray component, and the file reads and writes byte for byte.
    /// </summary>
    /// <remarks>
    /// Red-proof: with the Add rule accepting only a single argument, Label1 and Label2 have no cell or parent and the cell
    /// assertions fail. With the item rule removed, ColumnHeader1 is a tray component and the Assert.Empty fails.
    /// </remarks>
    [Fact]
    public void AHandWrittenTableLayoutAndListViewReadWithTheirCellsAndItems()
    {
        var source = ReadFixture("TableAndList.Designer.vb");

        var document = new WinFormsDesignerDocument(source);

        Assert.Equal(((int Column, int Row)?)(0, 0), Assert.Single(document.Controls, control => control.Name == "Label1").Cell);
        Assert.Equal(((int Column, int Row)?)(1, 0), Assert.Single(document.Controls, control => control.Name == "Label2").Cell);
        Assert.Equal("TableLayoutPanel1", Assert.Single(document.Controls, control => control.Name == "Label2").ParentName);
        Assert.Empty(document.Components);
        Assert.DoesNotContain(document.Controls, control => control.Name == "ColumnHeader1");
        Assert.Equal(source, document.Source);
    }

    /// <summary>The components container's timer, tool tip and binding source are in the tray; the button is the only control.</summary>
    /// <remarks>
    /// Red-proof: with ClassifyField answering None for every field that is not a Forms tray type, BindingSource1 is missing from
    /// the tray and the Assert.Equal on the component names fails.
    /// </remarks>
    [Fact]
    public void AHandWrittenComponentsContainerKeepsItsComponentsInTheTray()
    {
        var source = ReadFixture("ComponentsContainer.Designer.vb");

        var document = new WinFormsDesignerDocument(source);

        Assert.Equal(new[] { "BindingSource1", "Timer1", "ToolTip1" },
            document.Components.Select(component => component.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(new[] { "Button1" }, document.Controls.Select(control => control.Name));
        Assert.Equal(source, document.Source);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(WindowsDesktopSamples.FixturesRoot, "HandWritten", name));
}
