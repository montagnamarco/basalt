using System.Drawing;
using System.Windows.Forms;

namespace Basalt.Forms.Goldens;

/// <summary>TabControl, TreeView, ListView, Splitter and SplitContainer: controls that lay out other content.</summary>
public static partial class VisualScenarios
{
    private static readonly Size TabSize = new(160, 100);
    private static readonly Size TreeSize = new(140, 90);
    private static readonly Size ListViewSize = new(160, 90);
    private static readonly Size SplitterSize = new(6, 100);
    private static readonly Size SplitContainerSize = new(160, 100);

    private static IReadOnlyList<Scenario> ContainerScenarios() =>
    [
        Create<TabControl>("tabcontrol-top-selected", TabSize, tabControl =>
        {
            tabControl.Alignment = TabAlignment.Top;
            tabControl.TabPages.Add(new TabPage("First"));
            tabControl.TabPages.Add(new TabPage("Second"));
            tabControl.SelectedIndex = 0;
        }, FullArea(TabSize)),
        Create<TabControl>("tabcontrol-bottom-selected", TabSize, tabControl =>
        {
            tabControl.Alignment = TabAlignment.Bottom;
            tabControl.TabPages.Add(new TabPage("First"));
            tabControl.TabPages.Add(new TabPage("Second"));
            tabControl.SelectedIndex = 0;
        }, FullArea(TabSize)),

        Create<TreeView>("treeview-lines-plusminus", TreeSize, treeView =>
        {
            AsEditControl(treeView);
            var root = new TreeNode("Root");
            root.Nodes.Add(new TreeNode("Child one"));
            root.Nodes.Add(new TreeNode("Child two"));
            treeView.Nodes.Add(root);
            root.Expand();
        }, FullArea(TreeSize)),
        Create<TreeView>("treeview-checkboxes", TreeSize, treeView =>
        {
            AsEditControl(treeView);
            treeView.CheckBoxes = true;
            var root = new TreeNode("Root") { Checked = true };
            root.Nodes.Add(new TreeNode("Unchecked child"));
            treeView.Nodes.Add(root);
            root.Expand();
        }, FullArea(TreeSize)),

        Create<ListView>("listview-details-with-headers", ListViewSize, listView =>
        {
            AsEditControl(listView);
            listView.View = View.Details;
            listView.Columns.Add("Name", 80);
            listView.Columns.Add("Size", 60);
            listView.Items.Add(new ListViewItem(["File.txt", "1 KB"]));
            listView.Items.Add(new ListViewItem(["Other.txt", "2 KB"]));
        }, FullArea(ListViewSize)),
        Create<ListView>("listview-list", ListViewSize, listView =>
        {
            AsEditControl(listView);
            listView.View = View.List;
            listView.Items.Add(new ListViewItem("File.txt"));
            listView.Items.Add(new ListViewItem("Other.txt"));
        }, FullArea(ListViewSize)),
        Create<ListView>("listview-largeicon-no-images", ListViewSize, listView =>
        {
            AsEditControl(listView);
            listView.View = View.LargeIcon;
            listView.Items.Add(new ListViewItem("File.txt"));
            listView.Items.Add(new ListViewItem("Other.txt"));
        }, FullArea(ListViewSize)),

        Create<Splitter>("splitter-borderstyle-none", SplitterSize, _ => { }),
        Create<Splitter>("splitter-borderstyle-fixed3d", SplitterSize, splitter => splitter.BorderStyle = BorderStyle.Fixed3D),

        Create<SplitContainer>("splitcontainer-horizontal", SplitContainerSize, splitContainer =>
        {
            splitContainer.Orientation = Orientation.Vertical;
            splitContainer.SplitterDistance = 60;
        }),
        Create<SplitContainer>("splitcontainer-vertical-orientation", SplitContainerSize, splitContainer =>
        {
            splitContainer.Orientation = Orientation.Horizontal;
            splitContainer.SplitterDistance = 40;
        }),
    ];
}
