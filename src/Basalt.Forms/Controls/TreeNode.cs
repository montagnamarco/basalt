using System.ComponentModel;
using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

/// <summary>
/// One item of a <see cref="TreeView"/>: a label, an optional picture, a
/// checked state, and the nested <see cref="Nodes"/> it owns.
/// </summary>
/// <remarks>
/// A node belongs to one collection at a time, either another node's
/// <see cref="Nodes"/> or a <see cref="TreeView"/>'s root <see cref="TreeView.Nodes"/>;
/// <see cref="Parent"/>, <see cref="Level"/> and <see cref="TreeView"/> all
/// follow from where it was inserted, not from fields the node keeps itself.
/// </remarks>
[TypeConverter(typeof(TreeNodeConverter))]
[DefaultProperty("Text")]
[Serializable]
public partial class TreeNode : MarshalByRefObject, ICloneable, ISerializable
{
    private string _text;
    private bool _expanded;
    private bool _checked;
    private int _imageIndex = -1;
    private int _selectedImageIndex = -1;
    private Color _backColor;
    private Color _foreColor;

    /// <summary>The collection this node was inserted into, or null when it belongs to none.</summary>
    internal TreeNodeCollection? Owner { get; set; }

    /// <summary>The root collection's tree view, set only on a node with no parent.</summary>
    internal TreeView? RootTreeView { get; set; }

    /// <summary>Its own nested nodes, created on first access as Microsoft's does.</summary>
    private TreeNodeCollection? _nodes;

    public TreeNode()
    {
        _text = string.Empty;
    }

    public TreeNode(string? text)
    {
        _text = text ?? string.Empty;
    }

    public TreeNode(string? text, TreeNode[] children)
        : this(text)
    {
        Nodes.AddRange(children);
    }

    public TreeNode(string? text, int imageIndex, int selectedImageIndex)
        : this(text)
    {
        _imageIndex = imageIndex;
        _selectedImageIndex = selectedImageIndex;
    }

    public TreeNode(string? text, int imageIndex, int selectedImageIndex, TreeNode[] children)
        : this(text, imageIndex, selectedImageIndex)
    {
        Nodes.AddRange(children);
    }

    public Color BackColor
    {
        get => _backColor;
        set
        {
            if (_backColor == value) return;

            _backColor = value;
            TreeView?.Invalidate();
        }
    }

    [Browsable(false)]
    public Rectangle Bounds => TreeView?.GetNodeLabelBounds(this) ?? Rectangle.Empty;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;

            if (TreeView is { } tree)
            {
                if (!tree.SetNodeChecked(this, value, TreeViewAction.Unknown)) return;
            }
            else
            {
                _checked = value;
            }
        }
    }

    /// <summary>Sets Checked without going through the TreeView's BeforeCheck/AfterCheck events.</summary>
    internal bool CheckedCore
    {
        get => _checked;
        set => _checked = value;
    }

    [Browsable(false)]
    public TreeNode? FirstNode => Nodes.Count > 0 ? Nodes[0] : null;

    public Color ForeColor
    {
        get => _foreColor;
        set
        {
            if (_foreColor == value) return;

            _foreColor = value;
            TreeView?.Invalidate();
        }
    }

    /// <summary>The ancestors' Text, from the root down to this node, joined by the tree's PathSeparator.</summary>
    [Browsable(false)]
    public string FullPath
    {
        get
        {
            var separator = TreeView?.PathSeparator ?? "\\";
            var segments = new List<string>();
            for (var node = this; node is not null; node = node.Parent) segments.Add(node.Text);

            segments.Reverse();
            return string.Join(separator, segments);
        }
    }

    [DefaultValue(-1)]
    [TypeConverter(typeof(TreeViewImageIndexConverter))]
    [RelatedImageList(nameof(TreeView.ImageList))]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (_imageIndex == value) return;

            _imageIndex = value;
            TreeView?.Invalidate();
        }
    }

    /// <summary>Where this node sits among its siblings, or -1 when it belongs to no collection.</summary>
    public int Index => Owner?.IndexOf(this) ?? -1;

    [Browsable(false)]
    public bool IsEditing => TreeView?.EditingNode == this;

    [Browsable(false)]
    public bool IsExpanded => _expanded;

    [Browsable(false)]
    public bool IsSelected => TreeView?.SelectedNode == this;

    /// <summary>
    /// True when every ancestor is expanded and the node's row falls within
    /// the tree view's current scroll position, as TVM_GETITEMRECT reports it.
    /// </summary>
    [Browsable(false)]
    public bool IsVisible
    {
        get
        {
            if (TreeView is not { IsDisposed: false } tree) return false;
            if (!AncestorsExpanded()) return false;

            var rowTop = tree.RowTop(this);
            if (rowTop is not { } top) return false;

            var client = tree.ClientSize;
            return top + tree.ItemHeight > 0 && top < client.Height;
        }
    }

    /// <summary>Whether every ancestor of this node (not counting itself) is expanded.</summary>
    private bool AncestorsExpanded()
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (!node._expanded) return false;
        }

        return true;
    }

    [Browsable(false)]
    public TreeNode? LastNode => Nodes.Count > 0 ? Nodes[^1] : null;

    [Browsable(false)]
    public int Level => Parent is null ? 0 : Parent.Level + 1;

    public string Name { get; set; } = string.Empty;

    [Browsable(false)]
    public TreeNode? NextNode => Owner is { } owner && Index >= 0 && Index + 1 < owner.Count ? owner[Index + 1] : null;

    /// <summary>The next node in document order whose ancestors are all expanded.</summary>
    [Browsable(false)]
    public TreeNode? NextVisibleNode => TreeView?.NextInExpandedOrder(this);

    [Browsable(false)]
    public TreeNodeCollection Nodes => _nodes ??= new TreeNodeCollection(this);

    [Browsable(false)]
    public TreeNode? Parent { get; internal set; }

    [Browsable(false)]
    public TreeNode? PrevNode => Owner is { } owner && Index > 0 ? owner[Index - 1] : null;

    [Browsable(false)]
    public TreeNode? PrevVisibleNode => TreeView?.PreviousInExpandedOrder(this);

    [DefaultValue(-1)]
    [TypeConverter(typeof(TreeViewImageIndexConverter))]
    [RelatedImageList(nameof(TreeView.ImageList))]
    public int SelectedImageIndex
    {
        get => _selectedImageIndex;
        set
        {
            if (_selectedImageIndex == value) return;

            _selectedImageIndex = value;
            TreeView?.Invalidate();
        }
    }

    [DefaultValue(null)]
    [TypeConverter(typeof(StringConverter))]
    public object? Tag { get; set; }

    [Localizable(true)]
    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text == value) return;

            _text = value;
            TreeView?.Invalidate();
        }
    }

    /// <summary>The tree view this node (or its root-most ancestor) was added to; null when it has none.</summary>
    [Browsable(false)]
    public TreeView? TreeView
    {
        get
        {
            var node = this;
            while (node.Parent is { } parent) node = parent;

            return node.RootTreeView;
        }
    }

    public virtual object Clone()
    {
        var clone = new TreeNode(_text, _imageIndex, _selectedImageIndex)
        {
            Name = Name,
            Tag = Tag,
            Checked = _checked,
            BackColor = _backColor,
            ForeColor = _foreColor,
            ImageKey = _imageKey,
            SelectedImageKey = _selectedImageKey,
            StateImageIndex = _stateImageIndex,
            StateImageKey = _stateImageKey,
            NodeFont = _nodeFont,
            ToolTipText = _toolTipText,
        };

        foreach (TreeNode child in Nodes) clone.Nodes.Add((TreeNode)child.Clone());

        return clone;
    }

    public void Collapse() => Collapse(false);

    /// <summary>Collapses this node; with ignoreChildren false (the default), its descendants collapse too.</summary>
    public void Collapse(bool ignoreChildren)
    {
        SetExpanded(false);

        if (!ignoreChildren)
        {
            foreach (TreeNode child in Nodes) child.Collapse(false);
        }
    }

    public void EnsureVisible()
    {
        for (var node = Parent; node is not null; node = node.Parent) node._expanded = true;

        TreeView?.ScrollNodeIntoView(this);
    }

    public void Expand()
    {
        if (Nodes.Count == 0) return;

        SetExpanded(true);
    }

    public void ExpandAll()
    {
        Expand();
        foreach (TreeNode child in Nodes) child.ExpandAll();
    }

    /// <summary>The number of child nodes; with includeSubTrees true, every descendant.</summary>
    public int GetNodeCount(bool includeSubTrees)
    {
        if (!includeSubTrees) return Nodes.Count;

        var count = Nodes.Count;
        foreach (TreeNode child in Nodes) count += child.GetNodeCount(true);

        return count;
    }

    public void BeginEdit()
    {
        if (TreeView is { } tree) tree.BeginEditNode(this);
    }

    public void EndEdit(bool cancel)
    {
        if (TreeView is { } tree) tree.EndEditNode(this, cancel);
    }

    public void Remove() => Owner?.Remove(this);

    public override string ToString() => $"TreeNode: {_text}";

    public void Toggle()
    {
        if (_expanded) Collapse();
        else Expand();
    }

    /// <summary>Changes the expanded flag, raising the tree view's cancelable Before/After events when attached.</summary>
    internal void SetExpanded(bool value)
    {
        if (_expanded == value) return;

        if (TreeView is { } tree)
        {
            if (!tree.SetNodeExpanded(this, value)) return;
        }
        else
        {
            _expanded = value;
        }
    }

    /// <summary>Sets the flag directly, bypassing the cancelable events: the tree view calls this once it decided not to cancel.</summary>
    internal bool ExpandedCore
    {
        get => _expanded;
        set => _expanded = value;
    }
}
