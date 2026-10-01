using System.Collections;
using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A hierarchy of <see cref="TreeNode"/> items, each expandable to show its
/// own children, painted in the classic Windows style with dotted
/// connecting lines and plus/minus boxes.
/// </summary>
/// <remarks>
/// TreeView is native in WinForms: like <see cref="ScrollBar"/> it paints
/// and handles its mouse and keys through <see cref="Control"/>'s
/// default-procedure hooks, and its vertical scroll bar is part of its
/// frame (WS_VSCROLL), not a child control.
/// </remarks>
[DefaultProperty("Nodes")]
[DefaultEvent("AfterSelect")]
public partial class TreeView : Control
{
    private readonly TreeNodeCollection _nodes;

    private BorderStyle _borderStyle = BorderStyle.Fixed3D;
    private bool _checkBoxes;
    private bool _fullRowSelect;
    private bool _hideSelection = true;
    private int _imageIndex = -1;
    private int _selectedImageIndex = -1;
    private ImageList? _imageList;
    private int _indent = -1;
    private int _itemHeight = -1;
    private string _pathSeparator = "\\";
    private bool _scrollable = true;
    private TreeNode? _selectedNode;
    private bool _showLines = true;
    private bool _showPlusMinus = true;
    private bool _showRootLines = true;
    private bool _sorted;
    private IComparer? _treeViewNodeSorter;
    private Color _lineColor = Color.Black;
    private TreeNode? _topNode;
    private int _updateCount;
    private bool _vScrollVisible;

    public TreeView()
    {
        SetStyle(ControlStyles.UserPaint, false);
        _nodes = new TreeNodeCollection(this);
    }

    [DefaultValue(BorderStyle.Fixed3D)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (_borderStyle == value) return;

            _borderStyle = value;
            UpdateStyles();
        }
    }

    [DefaultValue(false)]
    public bool CheckBoxes
    {
        get => _checkBoxes;
        set
        {
            if (_checkBoxes == value) return;

            _checkBoxes = value;
            Invalidate();
        }
    }

    protected override Size DefaultSize => new(121, 97);

    [DefaultValue(false)]
    public bool FullRowSelect
    {
        get => _fullRowSelect;
        set
        {
            if (_fullRowSelect == value) return;

            _fullRowSelect = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool HideSelection
    {
        get => _hideSelection;
        set
        {
            if (_hideSelection == value) return;

            _hideSelection = value;
            Invalidate();
        }
    }

    [DefaultValue(-1)]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value) return;

            _imageIndex = value;
            Invalidate();
        }
    }

    [DefaultValue(null)]
    public ImageList? ImageList
    {
        get => _imageList;
        set
        {
            if (_imageList == value) return;

            _imageList = value;
            Invalidate();
        }
    }

    /// <summary>Horizontal space given to each level; auto-sized from the font until set explicitly.</summary>
    [Localizable(true)]
    public int Indent
    {
        get => _indent >= 0 ? _indent : Math.Max(FontHeight + 3, 19);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            if (_indent == value) return;

            _indent = value;
            Invalidate();
        }
    }

    /// <summary>The height of one row; auto-sized from the font until set explicitly.</summary>
    public int ItemHeight
    {
        get => _itemHeight >= 0 ? _itemHeight : Math.Max(FontHeight + 4, 16);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (value > short.MaxValue) throw new ArgumentOutOfRangeException(nameof(value));
            if (_itemHeight == value) return;

            _itemHeight = value;
            AdjustScrollbar();
            Invalidate();
        }
    }

    public Color LineColor
    {
        get => _lineColor;
        set
        {
            if (_lineColor == value) return;

            _lineColor = value;
            Invalidate();
        }
    }

    /// <summary>The root nodes; every other node is reached through one of theirs.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    [MergableProperty(false)]
    public TreeNodeCollection Nodes => _nodes;

    [DefaultValue("\\")]
    public string PathSeparator
    {
        get => _pathSeparator;
        set => _pathSeparator = value ?? "\\";
    }

    [DefaultValue(true)]
    public bool Scrollable
    {
        get => _scrollable;
        set
        {
            if (_scrollable == value) return;

            _scrollable = value;
            AdjustScrollbar();
        }
    }

    [DefaultValue(-1)]
    public int SelectedImageIndex
    {
        get => _selectedImageIndex;
        set
        {
            if (_selectedImageIndex == value) return;

            _selectedImageIndex = value;
            Invalidate();
        }
    }

    /// <summary>The highlighted node; setting it raises BeforeSelect and AfterSelect like a user's click.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreeNode? SelectedNode
    {
        get => _selectedNode;
        set => SelectNode(value, TreeViewAction.Unknown);
    }

    [DefaultValue(true)]
    public bool ShowLines
    {
        get => _showLines;
        set
        {
            if (_showLines == value) return;

            _showLines = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowPlusMinus
    {
        get => _showPlusMinus;
        set
        {
            if (_showPlusMinus == value) return;

            _showPlusMinus = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowRootLines
    {
        get => _showRootLines;
        set
        {
            if (_showRootLines == value) return;

            _showRootLines = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool Sorted
    {
        get => _sorted;
        set
        {
            if (_sorted == value) return;

            _sorted = value;
            if (value) Sort();
        }
    }

    /// <summary>The first node shown in the client area; scrolling changes it.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreeNode? TopNode
    {
        get => EffectiveTopNode();
        set => SetTopNode(value);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IComparer? TreeViewNodeSorter
    {
        get => _treeViewNodeSorter;
        set
        {
            _treeViewNodeSorter = value;
            if (value is not null) Sort();
        }
    }

    /// <summary>How many rows fit, fully, in the client area without scrolling.</summary>
    [Browsable(false)]
    public int VisibleCount => Math.Max(ClientSize.Height / ItemHeight, 0);

    public void BeginUpdate() => _updateCount++;

    public void EndUpdate()
    {
        if (_updateCount > 0) _updateCount--;
        if (_updateCount == 0)
        {
            AdjustScrollbar();
            Invalidate();
        }
    }

    public void CollapseAll()
    {
        foreach (TreeNode node in _nodes) CollapseRecursive(node);
    }

    private static void CollapseRecursive(TreeNode node)
    {
        node.Collapse(true);
        foreach (TreeNode child in node.Nodes) CollapseRecursive(child);
    }

    public void ExpandAll()
    {
        foreach (TreeNode node in _nodes) node.ExpandAll();
    }

    /// <summary>The total number of nodes; with includeSubTrees true, counting every descendant too.</summary>
    public int GetNodeCount(bool includeSubTrees)
    {
        var count = 0;
        foreach (TreeNode node in _nodes) count += includeSubTrees ? node.GetNodeCount(true) + 1 : 1;

        return count;
    }

    public void Sort()
    {
        SortRecursive(_nodes);
        Invalidate();
    }

    private void SortRecursive(TreeNodeCollection nodes)
    {
        var ordered = new List<TreeNode>(nodes.Count);
        foreach (TreeNode node in nodes) ordered.Add(node);

        ordered.Sort((left, right) => _treeViewNodeSorter?.Compare(left, right) ?? string.Compare(left.Text, right.Text, StringComparison.CurrentCulture));

        nodes.ReorderInternal(ordered);
        foreach (TreeNode node in nodes) SortRecursive(node.Nodes);
    }

    public override string ToString()
    {
        var count = _nodes.Count;
        return count == 0 ? base.ToString() : $"{base.ToString()}, Nodes.Count: {count}, Nodes[0]: {_nodes[0]}";
    }

    /// <summary>Changes the node and raises BeforeSelect/AfterSelect; false (and no change) when BeforeSelect cancels it.</summary>
    private bool SelectNode(TreeNode? node, TreeViewAction action)
    {
        if (_selectedNode == node) return true;

        var cancel = new TreeViewCancelEventArgs(node, false, action);
        OnBeforeSelect(cancel);
        if (cancel.Cancel) return false;

        _selectedNode = node;
        Invalidate();
        OnAfterSelect(new TreeViewEventArgs(node, action));
        return true;
    }

    /// <summary>
    /// Fires BeforeExpand/BeforeCollapse and, unless cancelled, changes the flag and fires After*.
    /// The action is always Expand or Collapse, whatever caused it, as the native tree view reports it.
    /// </summary>
    internal bool SetNodeExpanded(TreeNode node, bool value)
    {
        var action = value ? TreeViewAction.Expand : TreeViewAction.Collapse;
        var cancel = new TreeViewCancelEventArgs(node, false, action);
        if (value) OnBeforeExpand(cancel);
        else OnBeforeCollapse(cancel);
        if (cancel.Cancel) return false;

        node.ExpandedCore = value;
        AdjustScrollbar();
        Invalidate();

        if (value) OnAfterExpand(new TreeViewEventArgs(node, action));
        else OnAfterCollapse(new TreeViewEventArgs(node, action));

        return true;
    }

    /// <summary>Fires BeforeCheck/AfterCheck and, unless cancelled, changes the flag.</summary>
    internal bool SetNodeChecked(TreeNode node, bool value, TreeViewAction action)
    {
        var cancel = new TreeViewCancelEventArgs(node, false, action);
        OnBeforeCheck(cancel);
        if (cancel.Cancel) return false;

        node.CheckedCore = value;
        Invalidate();
        OnAfterCheck(new TreeViewEventArgs(node, action));
        return true;
    }

    /// <summary>A node structure change (added, removed, replaced): the scroll range and painting follow.</summary>
    internal void StructureChanged()
    {
        if (_updateCount > 0) return;

        AdjustScrollbar();
        Invalidate();
    }

    /// <summary>A node (and its subtree) left the tree: anything pointing at it must let go.</summary>
    internal void NodeRemoved(TreeNode node)
    {
        if (_selectedNode is { } selected && (selected == node || IsDescendant(node, selected))) _selectedNode = null;
        if (_topNode is { } top && (top == node || IsDescendant(node, top))) _topNode = null;
    }

    private static bool IsDescendant(TreeNode ancestor, TreeNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current == ancestor) return true;
        }

        return false;
    }

    protected virtual void OnAfterCheck(TreeViewEventArgs e) => ((TreeViewEventHandler?)Events[s_afterCheckEvent])?.Invoke(this, e);

    protected virtual void OnAfterCollapse(TreeViewEventArgs e) => ((TreeViewEventHandler?)Events[s_afterCollapseEvent])?.Invoke(this, e);

    protected virtual void OnAfterExpand(TreeViewEventArgs e) => ((TreeViewEventHandler?)Events[s_afterExpandEvent])?.Invoke(this, e);

    protected virtual void OnAfterSelect(TreeViewEventArgs e) => ((TreeViewEventHandler?)Events[s_afterSelectEvent])?.Invoke(this, e);

    protected virtual void OnBeforeCheck(TreeViewCancelEventArgs e) => ((TreeViewCancelEventHandler?)Events[s_beforeCheckEvent])?.Invoke(this, e);

    protected virtual void OnBeforeCollapse(TreeViewCancelEventArgs e) => ((TreeViewCancelEventHandler?)Events[s_beforeCollapseEvent])?.Invoke(this, e);

    protected virtual void OnBeforeExpand(TreeViewCancelEventArgs e) => ((TreeViewCancelEventHandler?)Events[s_beforeExpandEvent])?.Invoke(this, e);

    protected virtual void OnBeforeSelect(TreeViewCancelEventArgs e) => ((TreeViewCancelEventHandler?)Events[s_beforeSelectEvent])?.Invoke(this, e);

    protected virtual void OnNodeMouseClick(TreeNodeMouseClickEventArgs e) => ((TreeNodeMouseClickEventHandler?)Events[s_nodeMouseClickEvent])?.Invoke(this, e);

    protected virtual void OnNodeMouseDoubleClick(TreeNodeMouseClickEventArgs e) => ((TreeNodeMouseClickEventHandler?)Events[s_nodeMouseDoubleClickEvent])?.Invoke(this, e);

    public event TreeViewEventHandler? AfterCheck
    {
        add => Events.AddHandler(s_afterCheckEvent, value);
        remove => Events.RemoveHandler(s_afterCheckEvent, value);
    }

    public event TreeViewEventHandler? AfterCollapse
    {
        add => Events.AddHandler(s_afterCollapseEvent, value);
        remove => Events.RemoveHandler(s_afterCollapseEvent, value);
    }

    public event TreeViewEventHandler? AfterExpand
    {
        add => Events.AddHandler(s_afterExpandEvent, value);
        remove => Events.RemoveHandler(s_afterExpandEvent, value);
    }

    public event TreeViewEventHandler? AfterSelect
    {
        add => Events.AddHandler(s_afterSelectEvent, value);
        remove => Events.RemoveHandler(s_afterSelectEvent, value);
    }

    public event TreeViewCancelEventHandler? BeforeCheck
    {
        add => Events.AddHandler(s_beforeCheckEvent, value);
        remove => Events.RemoveHandler(s_beforeCheckEvent, value);
    }

    public event TreeViewCancelEventHandler? BeforeCollapse
    {
        add => Events.AddHandler(s_beforeCollapseEvent, value);
        remove => Events.RemoveHandler(s_beforeCollapseEvent, value);
    }

    public event TreeViewCancelEventHandler? BeforeExpand
    {
        add => Events.AddHandler(s_beforeExpandEvent, value);
        remove => Events.RemoveHandler(s_beforeExpandEvent, value);
    }

    public event TreeViewCancelEventHandler? BeforeSelect
    {
        add => Events.AddHandler(s_beforeSelectEvent, value);
        remove => Events.RemoveHandler(s_beforeSelectEvent, value);
    }

    public event TreeNodeMouseClickEventHandler? NodeMouseClick
    {
        add => Events.AddHandler(s_nodeMouseClickEvent, value);
        remove => Events.RemoveHandler(s_nodeMouseClickEvent, value);
    }

    public event TreeNodeMouseClickEventHandler? NodeMouseDoubleClick
    {
        add => Events.AddHandler(s_nodeMouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_nodeMouseDoubleClickEvent, value);
    }

    private static readonly object s_afterCheckEvent = new();
    private static readonly object s_afterCollapseEvent = new();
    private static readonly object s_afterExpandEvent = new();
    private static readonly object s_afterSelectEvent = new();
    private static readonly object s_beforeCheckEvent = new();
    private static readonly object s_beforeCollapseEvent = new();
    private static readonly object s_beforeExpandEvent = new();
    private static readonly object s_beforeSelectEvent = new();
    private static readonly object s_nodeMouseClickEvent = new();
    private static readonly object s_nodeMouseDoubleClickEvent = new();
}
