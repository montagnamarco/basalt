using System.Collections;
using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>
/// The <see cref="TreeNode"/> children of either a <see cref="TreeView"/> (its
/// root <see cref="TreeView.Nodes"/>) or of another node (its own <see cref="TreeNode.Nodes"/>).
/// </summary>
public class TreeNodeCollection : ICollection, IEnumerable, IList
{
    private readonly List<TreeNode> _nodes = [];

    /// <summary>The node this collection belongs to; null for a tree view's root collection.</summary>
    private readonly TreeNode? _ownerNode;

    /// <summary>The tree view this collection belongs to directly; set only for a root collection.</summary>
    private readonly TreeView? _ownerTreeView;

    internal TreeNodeCollection(TreeView ownerTreeView) => _ownerTreeView = ownerTreeView;

    internal TreeNodeCollection(TreeNode ownerNode) => _ownerNode = ownerNode;

    /// <summary>The tree view this collection ultimately belongs to, through its owner node when it has one.</summary>
    private TreeView? Owner => _ownerTreeView ?? _ownerNode?.TreeView;

    [Browsable(false)]
    public int Count => _nodes.Count;

    public bool IsReadOnly => false;

    public virtual TreeNode this[int index]
    {
        get => _nodes[index];
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(value, _nodes[index])) return;

            // As Add and Insert: a node lives in one place only.
            if (value.Owner is not null) throw new ArgumentException("The tree node already belongs to a tree view.", nameof(value));

            Detach(_nodes[index]);
            Attach(value);
            _nodes[index] = value;
            Owner?.StructureChanged();
        }
    }

    public virtual TreeNode? this[string? key]
    {
        get
        {
            var index = IndexOfKey(key);
            return index < 0 ? null : _nodes[index];
        }
    }

    public virtual TreeNode Add(string? text)
    {
        var node = new TreeNode(text);
        Add(node);
        return node;
    }

    public virtual TreeNode Add(string? key, string? text)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty };
        Add(node);
        return node;
    }

    public virtual TreeNode Add(string? key, string? text, int imageIndex)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty, ImageIndex = imageIndex };
        Add(node);
        return node;
    }

    public virtual TreeNode Add(string? key, string? text, int imageIndex, int selectedImageIndex)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty, ImageIndex = imageIndex, SelectedImageIndex = selectedImageIndex };
        Add(node);
        return node;
    }

    public virtual int Add(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = IsSorted ? SortedInsertionIndex(node) : _nodes.Count;
        InsertCore(index, node);
        return index;
    }

    public virtual void AddRange(params TreeNode[] nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        foreach (var node in nodes) Add(node);
    }

    public virtual void Clear()
    {
        var removed = _nodes.ToArray();
        _nodes.Clear();

        foreach (var node in removed)
        {
            Detach(node);
            Owner?.NodeRemoved(node);
        }

        Owner?.StructureChanged();
    }

    public bool Contains(TreeNode node) => _nodes.Contains(node);

    public virtual bool ContainsKey(string? key) => IndexOfKey(key) >= 0;

    public void CopyTo(Array dest, int index) => ((ICollection)_nodes).CopyTo(dest, index);

    /// <summary>Every node, in this collection and (when asked) its descendants, whose Name matches the key.</summary>
    public TreeNode[] Find(string key, bool searchAllChildren)
    {
        ArgumentNullException.ThrowIfNull(key);

        var found = new List<TreeNode>();
        FindCore(key, searchAllChildren, found);
        return found.ToArray();
    }

    private void FindCore(string key, bool searchAllChildren, List<TreeNode> found)
    {
        foreach (var node in _nodes)
        {
            if (string.Equals(node.Name, key, StringComparison.OrdinalIgnoreCase)) found.Add(node);
            if (searchAllChildren) node.Nodes.FindCore(key, true, found);
        }
    }

    public IEnumerator GetEnumerator() => _nodes.GetEnumerator();

    public int IndexOf(TreeNode node) => _nodes.IndexOf(node);

    public virtual int IndexOfKey(string? key)
    {
        if (string.IsNullOrEmpty(key)) return -1;

        for (var i = 0; i < _nodes.Count; i++)
        {
            if (string.Equals(_nodes[i].Name, key, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return -1;
    }

    public virtual void Insert(int index, TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        InsertCore(index, node);
    }

    public virtual TreeNode Insert(int index, string? text)
    {
        var node = new TreeNode(text);
        Insert(index, node);
        return node;
    }

    public virtual TreeNode Insert(int index, string? key, string? text)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty };
        Insert(index, node);
        return node;
    }

    public virtual TreeNode Insert(int index, string? key, string? text, int imageIndex)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty, ImageIndex = imageIndex };
        Insert(index, node);
        return node;
    }

    public virtual TreeNode Insert(int index, string? key, string? text, int imageIndex, int selectedImageIndex)
    {
        var node = new TreeNode(text) { Name = key ?? string.Empty, ImageIndex = imageIndex, SelectedImageIndex = selectedImageIndex };
        Insert(index, node);
        return node;
    }

    public void Remove(TreeNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var index = _nodes.IndexOf(node);
        if (index >= 0) RemoveAt(index);
    }

    public virtual void RemoveAt(int index)
    {
        var node = _nodes[index];
        _nodes.RemoveAt(index);
        Detach(node);

        Owner?.NodeRemoved(node);
        Owner?.StructureChanged();
    }

    public virtual void RemoveByKey(string? key)
    {
        var index = IndexOfKey(key);
        if (index >= 0) RemoveAt(index);
    }

    /// <summary>Whether a new node Added (not Inserted at an explicit position) should land in sorted order.</summary>
    private bool IsSorted => Owner is { } owner && (owner.Sorted || owner.TreeViewNodeSorter is not null);

    private int SortedInsertionIndex(TreeNode node)
    {
        var index = 0;
        while (index < _nodes.Count && Compare(_nodes[index], node) <= 0) index++;

        return index;
    }

    private int Compare(TreeNode left, TreeNode right)
    {
        if (Owner?.TreeViewNodeSorter is { } sorter) return sorter.Compare(left, right);

        return string.Compare(left.Text, right.Text, StringComparison.CurrentCulture);
    }

    /// <summary>Puts this collection's own nodes in a new order; they stay attached, so no ownership check applies.</summary>
    internal void ReorderInternal(IReadOnlyList<TreeNode> ordered)
    {
        _nodes.Clear();
        _nodes.AddRange(ordered);
        Owner?.StructureChanged();
    }

    private void InsertCore(int index, TreeNode node)
    {
        if (node.Owner is not null) throw new ArgumentException("The tree node already belongs to a tree view.", nameof(node));

        Attach(node);
        _nodes.Insert(index, node);
        Owner?.StructureChanged();
    }

    private void Attach(TreeNode node)
    {
        node.Owner = this;
        node.Parent = _ownerNode;
        node.RootTreeView = _ownerNode is null ? _ownerTreeView : null;
    }

    private static void Detach(TreeNode node)
    {
        node.Owner = null;
        node.Parent = null;
        node.RootTreeView = null;
    }

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    bool IList.IsFixedSize => false;

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (TreeNode)value!;
    }

    int IList.Add(object? value)
    {
        Add((TreeNode)value!);
        return Count - 1;
    }

    bool IList.Contains(object? value) => value is TreeNode node && Contains(node);

    int IList.IndexOf(object? value) => value is TreeNode node ? IndexOf(node) : -1;

    void IList.Insert(int index, object? value) => Insert(index, (TreeNode)value!);

    void IList.Remove(object? value)
    {
        if (value is TreeNode node) Remove(node);
    }
}
