using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>TreeView and TreeNode: the hierarchy, classic drawing, mouse and keyboard, and scrolling.</summary>
public class TreeViewTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host!.KeyReleaseQwerty(key, modifiers);
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    // --- Nodes hierarchy: Add, Insert, Remove, Clear, Find, IndexOfKey, Item[key] ---

    [Fact]
    public void Nodes_hierarchy_supports_add_insert_remove_find_and_keyed_lookup()
    {
        var tree = new TreeView();
        var alpha = tree.Nodes.Add("alpha", "Alpha");
        var beta = tree.Nodes.Add("beta", "Beta");
        tree.Nodes.Insert(1, "gamma", "Gamma");

        Assert.Equal(["Alpha", "Gamma", "Beta"], tree.Nodes.Cast<TreeNode>().Select(n => n.Text));
        Assert.Equal(0, alpha.Index);
        Assert.Equal(2, beta.Index);
        Assert.Same(alpha, tree.Nodes["alpha"]);
        Assert.Equal(1, tree.Nodes.IndexOfKey("gamma"));

        tree.Nodes.RemoveByKey("gamma");
        Assert.Equal(2, tree.Nodes.Count);
        Assert.Equal([beta], tree.Nodes.Find("beta", false));

        tree.Nodes.Clear();
        Assert.Empty(tree.Nodes);
    }

    [Fact]
    public void TreeNode_exposes_its_place_in_the_tree()
    {
        var tree = new TreeView { PathSeparator = "/" };
        var root = tree.Nodes.Add("Root");
        var child = root.Nodes.Add("Child");
        var grandchild = child.Nodes.Add("Grandchild");

        Assert.Equal(0, root.Level);
        Assert.Equal(1, child.Level);
        Assert.Equal(2, grandchild.Level);
        Assert.Same(root, child.Parent);
        Assert.Same(tree, root.TreeView);
        Assert.Same(tree, grandchild.TreeView);
        Assert.Equal("Root/Child/Grandchild", grandchild.FullPath);
        Assert.Same(child, root.FirstNode);
        Assert.Null(child.PrevNode);
    }

    [Fact]
    public void Removing_a_node_detaches_it_and_clears_the_selection_if_it_held_it()
    {
        var tree = new TreeView();
        var node = tree.Nodes.Add("Node");
        tree.SelectedNode = node;

        node.Remove();

        Assert.Null(node.Parent);
        Assert.Empty(tree.Nodes);
        Assert.Null(tree.SelectedNode);
    }

    [Fact]
    public void GetNodeCount_counts_either_only_children_or_every_descendant()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        var child = root.Nodes.Add("Child");
        child.Nodes.Add("Grandchild1");
        child.Nodes.Add("Grandchild2");

        Assert.Equal(1, tree.GetNodeCount(false));
        Assert.Equal(4, tree.GetNodeCount(true));
        Assert.Equal(1, root.GetNodeCount(false));
        Assert.Equal(3, root.GetNodeCount(true));
    }

    // --- Expand/Collapse/Toggle and the cancelable Before/After events ---

    [Fact]
    public void Expand_raises_before_and_after_expand_in_order()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        root.Nodes.Add("Child");
        var order = new List<string>();
        tree.BeforeExpand += (_, e) => order.Add($"before:{e.Action}");
        tree.AfterExpand += (_, e) => order.Add($"after:{e.Action}");

        root.Expand();

        Assert.True(root.IsExpanded);
        Assert.Equal(["before:Expand", "after:Expand"], order);
    }

    [Fact]
    public void Collapse_reports_the_Collapse_action_whatever_caused_it()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        root.Nodes.Add("Child");
        root.Expand();
        var actions = new List<TreeViewAction>();
        tree.BeforeCollapse += (_, e) => actions.Add(e.Action);
        tree.AfterCollapse += (_, e) => actions.Add(e.Action);

        root.Collapse();

        Assert.Equal([TreeViewAction.Collapse, TreeViewAction.Collapse], actions);
    }

    [Fact]
    public void A_node_from_another_tree_cannot_replace_a_node_through_the_indexer()
    {
        var first = new TreeView();
        var borrowed = first.Nodes.Add("A");
        var second = new TreeView();
        second.Nodes.Add("B");

        Assert.Throws<ArgumentException>(() => second.Nodes[0] = borrowed);
        Assert.Equal("B", second.Nodes[0].Text);
        Assert.Same(first, borrowed.TreeView);
    }

    [Fact]
    public void BeforeExpand_can_cancel_the_expansion()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        root.Nodes.Add("Child");
        tree.BeforeExpand += (_, e) => e.Cancel = true;

        root.Expand();

        Assert.False(root.IsExpanded);
    }

    [Fact]
    public void BeforeCollapse_can_cancel_the_collapse()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        root.Nodes.Add("Child");
        root.Expand();
        tree.BeforeCollapse += (_, e) => e.Cancel = true;

        root.Collapse();

        Assert.True(root.IsExpanded);
    }

    [Fact]
    public void Toggle_flips_between_expanded_and_collapsed()
    {
        var root = new TreeNode("Root");
        root.Nodes.Add("Child");

        root.Toggle();
        Assert.True(root.IsExpanded);

        root.Toggle();
        Assert.False(root.IsExpanded);
    }

    [Fact]
    public void ExpandAll_expands_every_descendant()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        var child = root.Nodes.Add("Child");
        child.Nodes.Add("Grandchild");

        tree.ExpandAll();

        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);
    }

    [Fact]
    public void NextVisibleNode_and_PrevVisibleNode_skip_collapsed_subtrees()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        var hiddenChild = root.Nodes.Add("HiddenChild"); // root stays collapsed, so this is not in the sequence
        var second = tree.Nodes.Add("Second");

        Assert.Same(second, root.NextVisibleNode);
        Assert.Same(root, second.PrevVisibleNode);
        Assert.Same(root, hiddenChild.Parent); // still in the tree, just hidden behind its collapsed parent

        root.Expand();
        Assert.Same(hiddenChild, root.NextVisibleNode);
        Assert.Same(hiddenChild, second.PrevVisibleNode);
    }

    [Fact]
    public void EnsureVisible_expands_collapsed_ancestors()
    {
        var tree = new TreeView();
        var root = tree.Nodes.Add("Root");
        var child = root.Nodes.Add("Child");
        var grandchild = child.Nodes.Add("Grandchild");

        grandchild.EnsureVisible();

        Assert.True(root.IsExpanded);
        Assert.True(child.IsExpanded);
    }

    // --- CheckBoxes with cancelable Before/After check ---

    [Fact]
    public void Checked_raises_cancelable_before_and_after_check()
    {
        var tree = new TreeView { CheckBoxes = true };
        var node = tree.Nodes.Add("Node");
        var afterFired = false;
        tree.AfterCheck += (_, _) => afterFired = true;

        node.Checked = true;

        Assert.True(node.Checked);
        Assert.True(afterFired);
    }

    [Fact]
    public void BeforeCheck_can_refuse_the_new_state()
    {
        var tree = new TreeView { CheckBoxes = true };
        var node = tree.Nodes.Add("Node");
        tree.BeforeCheck += (_, e) => e.Cancel = true;

        node.Checked = true;

        Assert.False(node.Checked);
    }

    // --- SelectedNode with cancelable Before/After select ---

    [Fact]
    public void SelectedNode_raises_before_and_after_select()
    {
        var tree = new TreeView();
        var a = tree.Nodes.Add("A");
        var afterNode = (TreeNode?)null;
        tree.AfterSelect += (_, e) => afterNode = e.Node;

        tree.SelectedNode = a;

        Assert.Same(a, tree.SelectedNode);
        Assert.Same(a, afterNode);
    }

    [Fact]
    public void BeforeSelect_can_keep_the_previous_selection()
    {
        var tree = new TreeView();
        var a = tree.Nodes.Add("A");
        var b = tree.Nodes.Add("B");
        tree.SelectedNode = a;
        tree.BeforeSelect += (_, e) => e.Cancel = true;

        tree.SelectedNode = b;

        Assert.Same(a, tree.SelectedNode);
    }

    // --- Clone ---

    [Fact]
    public void Clone_copies_text_state_and_children_recursively()
    {
        var root = new TreeNode("Root") { Tag = "tag", Checked = true };
        root.Nodes.Add("Child");

        var clone = (TreeNode)root.Clone();

        Assert.Equal("Root", clone.Text);
        Assert.Equal("tag", clone.Tag);
        Assert.True(clone.Checked);
        Assert.Single(clone.Nodes);
        Assert.NotSame(root.Nodes[0], clone.Nodes[0]);
        Assert.Null(clone.Parent);
    }

    // --- Sorted and TreeViewNodeSorter ---

    [Fact]
    public void Sorted_inserts_new_nodes_in_alphabetical_order()
    {
        var tree = new TreeView { Sorted = true };
        tree.Nodes.Add("Banana");
        tree.Nodes.Add("Apple");
        tree.Nodes.Add("Cherry");

        Assert.Equal(["Apple", "Banana", "Cherry"], tree.Nodes.Cast<TreeNode>().Select(n => n.Text));
    }

    [Fact]
    public void TreeViewNodeSorter_controls_the_order_Sort_applies()
    {
        var tree = new TreeView();
        tree.Nodes.Add("Banana");
        tree.Nodes.Add("Apple");

        tree.TreeViewNodeSorter = Comparer<TreeNode>.Create((x, y) => string.Compare(y.Text, x.Text, StringComparison.Ordinal));

        Assert.Equal(["Banana", "Apple"], tree.Nodes.Cast<TreeNode>().Select(n => n.Text));
    }

    // --- HitTest / GetNodeAt ---

    [Fact]
    public void HitTest_finds_the_plus_minus_the_label_and_outside_the_client_area()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        branch.Nodes.Add("Child");

        var glyphHit = tree.HitTest(new Point((tree.Indent - 9) / 2 + 4, tree.ItemHeight / 2));
        Assert.Same(branch, glyphHit.Node);
        Assert.Equal(TreeViewHitTestLocations.PlusMinus, glyphHit.Location);

        var labelHit = tree.HitTest(new Point(tree.Indent + 2, tree.ItemHeight / 2));
        Assert.Same(branch, labelHit.Node);
        Assert.Equal(TreeViewHitTestLocations.Label, labelHit.Location);

        var below = tree.HitTest(new Point(5, tree.ClientSize.Height + 10));
        Assert.Equal(TreeViewHitTestLocations.BelowClientArea, below.Location);

        Assert.Same(branch, tree.GetNodeAt(tree.Indent + 2, tree.ItemHeight / 2));
    }

    // --- Classic painting ---

    [Fact]
    public void Background_is_the_system_window_color_unless_a_color_was_set()
    {
        var tree = new TreeView { Bounds = new Rectangle(0, 0, 150, 100) };

        using var plain = Draw(tree);
        Assert.Equal(SystemColors.Window.ToArgb(), plain.GetPixel(5, 5).ToArgb());

        tree.BackColor = Color.Red;
        using var colored = Draw(tree);
        Assert.Equal(Color.Red.ToArgb(), colored.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void A_node_with_children_shows_a_plus_minus_glyph_a_leaf_does_not()
    {
        // BorderStyle.None keeps the bitmap's (0, 0) exactly at the client area's
        // top-left: with Fixed3D, DrawToBitmap includes the sunken frame too.
        var tree = new TreeView { Font = Font, BorderStyle = BorderStyle.None, ShowLines = false, Bounds = new Rectangle(0, 0, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        branch.Nodes.Add("Child"); // gives Branch a glyph; Branch itself stays collapsed
        tree.Nodes.Add("Leaf"); // no children: no glyph

        using var bitmap = Draw(tree);

        var glyphX = (tree.Indent - 9) / 2;
        var branchGlyphTop = tree.ItemHeight / 2 - 4;
        var leafGlyphTop = tree.ItemHeight + tree.ItemHeight / 2 - 4;

        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(glyphX, branchGlyphTop).ToArgb());
        Assert.NotEqual(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(glyphX, leafGlyphTop).ToArgb());
    }

    /// <summary>Whether any pixel of the region is exactly this colour: robust to exactly where inside it a glyph's strokes fall.</summary>
    private static bool RegionContains(Bitmap bitmap, Rectangle region, Color color)
    {
        for (var y = region.Top; y < region.Bottom; y++)
        {
            for (var x = region.Left; x < region.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == color.ToArgb()) return true;
            }
        }

        return false;
    }

    [Fact]
    public void CheckBoxes_draw_a_glyph_that_changes_when_Checked_changes()
    {
        var tree = new TreeView { Font = Font, BorderStyle = BorderStyle.None, CheckBoxes = true, ShowPlusMinus = false, ShowLines = false, Bounds = new Rectangle(0, 0, 150, 100) };
        var node = tree.Nodes.Add("Node");
        var box = new Rectangle(tree.Indent, tree.ItemHeight / 2 - 6, 13, 13);

        using var unchecked_ = Draw(tree);
        node.Checked = true;
        using var checked_ = Draw(tree);

        var changed = false;
        for (var y = box.Top; y < box.Bottom && !changed; y++)
        {
            for (var x = box.Left; x < box.Right; x++)
            {
                if (unchecked_.GetPixel(x, y).ToArgb() != checked_.GetPixel(x, y).ToArgb()) { changed = true; break; }
            }
        }

        Assert.True(changed, "Expected the check box glyph to differ once Checked was set.");
    }

    [AvaloniaFact]
    public void Selected_node_highlights_only_while_focused_when_HideSelection_is_true()
    {
        var tree = new TreeView { Font = Font, BorderStyle = BorderStyle.None, Bounds = new Rectangle(10, 10, 150, 100) };
        var node = tree.Nodes.Add("Node");
        var other = new Button { Bounds = new Rectangle(10, 120, 75, 23) }; // takes the initial focus instead of the tree
        var form = new Form { ClientSize = new Size(300, 200) };
        form.Controls.Add(tree);
        form.Controls.Add(other);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        other.Focus();
        tree.SelectedNode = node;

        var label = new Rectangle(tree.Indent, 0, 40, tree.ItemHeight);

        using var unfocused = Draw(tree);
        Assert.False(RegionContains(unfocused, label, SystemColors.Highlight));

        tree.Focus();
        Dispatcher.UIThread.RunJobs();
        using var focused = Draw(tree);
        Assert.True(RegionContains(focused, label, SystemColors.Highlight));

        form.Close();
    }

    [AvaloniaFact]
    public void FullRowSelect_highlights_the_entire_row_not_just_the_label()
    {
        var tree = new TreeView { Font = Font, BorderStyle = BorderStyle.None, FullRowSelect = true, Bounds = new Rectangle(10, 10, 150, 100) };
        var node = tree.Nodes.Add("Node");
        var form = ShowWith(tree);
        tree.SelectedNode = node;
        tree.Focus();
        Dispatcher.UIThread.RunJobs();

        using var bitmap = Draw(tree);
        Assert.Equal(SystemColors.Highlight.ToArgb(), bitmap.GetPixel(2, tree.ItemHeight / 2).ToArgb());

        form.Close();
    }

    // --- Mouse: click selects, click on +/- toggles, double-click toggles ---

    [AvaloniaFact]
    public void Click_selects_the_node_under_the_pointer_and_raises_NodeMouseClick()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        tree.Nodes.Add("First");
        var second = tree.Nodes.Add("Second");
        var form = ShowWith(tree);

        TreeNode? clicked = null;
        tree.NodeMouseClick += (_, e) => clicked = e.Node;

        var point = new Avalonia.Point(10 + tree.Indent + 5, 10 + tree.ItemHeight + tree.ItemHeight / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.Same(second, tree.SelectedNode);
        Assert.Same(second, clicked);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_plus_minus_glyph_toggles_expansion_without_changing_selection()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        branch.Nodes.Add("Child");
        var form = ShowWith(tree);

        var point = new Avalonia.Point(10 + (tree.Indent - 9) / 2 + 4, 10 + tree.ItemHeight / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(branch.IsExpanded);
        Assert.Null(tree.SelectedNode);

        form.Close();
    }

    [AvaloniaFact]
    public void Double_click_on_the_label_toggles_expansion_and_raises_NodeMouseDoubleClick()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        branch.Nodes.Add("Child");
        var form = ShowWith(tree);

        var doubleClicked = false;
        tree.NodeMouseDoubleClick += (_, _) => doubleClicked = true;

        var point = new Avalonia.Point(10 + tree.Indent + 5, 10 + tree.ItemHeight / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(doubleClicked);
        Assert.True(branch.IsExpanded);

        form.Close();
    }

    // --- Keyboard: arrows, Home/End, PageUp/PageDown, +/-/* ---

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_through_the_expanded_order()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        var child = branch.Nodes.Add("Child");
        var second = tree.Nodes.Add("Second");
        var form = ShowWith(tree);
        tree.Focus();
        tree.SelectedNode = branch;

        Press(form, PhysicalKey.ArrowRight); // expands Branch, selection stays
        Assert.True(branch.IsExpanded);
        Assert.Same(branch, tree.SelectedNode);

        Press(form, PhysicalKey.ArrowRight); // now expanded: moves into the first child
        Assert.Same(child, tree.SelectedNode);

        Press(form, PhysicalKey.ArrowDown);
        Assert.Same(second, tree.SelectedNode);

        Press(form, PhysicalKey.ArrowUp);
        Assert.Same(child, tree.SelectedNode);

        Press(form, PhysicalKey.ArrowLeft); // Child has no children: moves to its parent
        Assert.Same(branch, tree.SelectedNode);

        form.Close();
    }

    [AvaloniaFact]
    public void Home_and_End_select_the_first_and_last_nodes_in_the_expanded_order()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        var first = tree.Nodes.Add("First");
        var last = tree.Nodes.Add("Last");
        var form = ShowWith(tree);
        tree.Focus();
        tree.SelectedNode = first;

        Press(form, PhysicalKey.End);
        Assert.Same(last, tree.SelectedNode);

        Press(form, PhysicalKey.Home);
        Assert.Same(first, tree.SelectedNode);

        form.Close();
    }

    [AvaloniaFact]
    public void Plus_minus_and_asterisk_keys_expand_collapse_and_expand_all()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 150, 100) };
        var branch = tree.Nodes.Add("Branch");
        var child = branch.Nodes.Add("Child");
        child.Nodes.Add("Grandchild");
        var form = ShowWith(tree);
        tree.Focus();
        tree.SelectedNode = branch;

        Type(form, "+");
        Assert.True(branch.IsExpanded);

        Type(form, "-");
        Assert.False(branch.IsExpanded);

        Type(form, "*");
        Assert.True(branch.IsExpanded);
        Assert.True(child.IsExpanded);

        form.Close();
    }

    // --- Vertical scrolling with TopNode ---

    [Fact]
    public void Enough_nodes_show_a_vertical_scroll_bar_sized_to_the_visible_rows()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");

        var info = tree.GetWindowScroll(vertical: true);

        Assert.NotNull(info);
        Assert.Equal(29, info!.Value.Maximum);
        Assert.Equal(tree.VisibleCount, info.Value.Page);
        Assert.Equal(0, info.Value.Position);
    }

    [Fact]
    public void Few_nodes_show_no_vertical_scroll_bar()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        tree.Nodes.Add("Only one node");

        Assert.Null(tree.GetWindowScroll(vertical: true));
    }

    [Fact]
    public void TopNode_can_be_set_directly_to_any_root_node()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");

        tree.TopNode = tree.Nodes[5];

        Assert.Same(tree.Nodes[5], tree.TopNode);
    }

    [AvaloniaFact]
    public void PageDown_moves_the_selection_by_a_full_page_and_scrolls_to_follow_it()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(10, 10, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");
        var form = ShowWith(tree);
        tree.Focus();
        tree.SelectedNode = tree.Nodes[0];

        Press(form, PhysicalKey.PageDown);

        Assert.Equal(tree.VisibleCount, tree.Nodes.IndexOf(tree.SelectedNode!));
        Assert.NotSame(tree.Nodes[0], tree.TopNode);

        form.Close();
    }

    [Fact]
    public void Clicking_the_scroll_bars_down_arrow_moves_TopNode_down_one_row()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");
        var before = tree.Nodes.IndexOf(tree.TopNode!);

        var taken = tree.NonClientMouseDown(MouseButtons.Left, new Point(92, 72));

        Assert.False(taken); // the arrow steps once; it does not start a thumb drag
        Assert.Equal(before + 1, tree.Nodes.IndexOf(tree.TopNode!));
    }

    [Fact]
    public void Clicking_the_scroll_bars_up_arrow_moves_TopNode_up_one_row()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");
        tree.TopNode = tree.Nodes[10];

        tree.NonClientMouseDown(MouseButtons.Left, new Point(92, 8));

        Assert.Equal(9, tree.Nodes.IndexOf(tree.TopNode!));
    }

    [Fact]
    public void Dragging_the_thumb_scrolls_proportionally_to_where_it_is_dropped()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");

        var info = tree.GetWindowScroll(vertical: true)!.Value;
        var bar = WindowScrollBars.Vertical(tree.Size, WindowStyles.VerticalScroll, WindowStyles.ExClientEdge);
        var track = ScrollBarPainter.TrackBounds(Orientation.Vertical, bar);
        var thumb = info.ThumbBounds(track, vertical: true);

        var started = tree.NonClientMouseDown(MouseButtons.Left, new Point(thumb.X + 1, thumb.Y + 1));
        Assert.True(started);
        Assert.True(tree.IsTrackingNonClient);

        tree.NonClientMouseMove(new Point(thumb.X + 1, track.Bottom - 1));
        tree.NonClientMouseUp(new Point(thumb.X + 1, track.Bottom - 1));

        Assert.False(tree.IsTrackingNonClient);
        Assert.True(tree.Nodes.IndexOf(tree.TopNode!) > 10);
    }

    // --- BeginUpdate/EndUpdate ---

    [Fact]
    public void BeginUpdate_defers_the_scroll_bar_recalculation_until_EndUpdate()
    {
        var tree = new TreeView { Font = Font, Bounds = new Rectangle(0, 0, 100, 80) };

        tree.BeginUpdate();
        for (var i = 0; i < 30; i++) tree.Nodes.Add($"Node {i}");
        Assert.Null(tree.GetWindowScroll(vertical: true)); // not recalculated yet

        tree.EndUpdate();
        Assert.NotNull(tree.GetWindowScroll(vertical: true));
    }
}
