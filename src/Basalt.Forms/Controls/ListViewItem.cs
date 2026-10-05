using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

/// <summary>One row of a <see cref="ListView"/>: its label, icon, check state and the sub-items shown in the other columns.</summary>
[DefaultProperty("Text")]
[TypeConverter(typeof(ListViewItemConverter))]
[Serializable]
public partial class ListViewItem : ICloneable, ISerializable
{
    private readonly ListViewSubItemCollection _subItems;

    private bool _checked;
    private bool _selected;
    private bool _focused;
    private int _imageIndex = -1;
    private string _imageKey = string.Empty;
    private ListViewGroup? _group;
    private int _indentCount;
    private Point _position;
    private int _stateImageIndex = -1;

    /// <summary>The ListView this item belongs to, set by ListView.ListViewItemCollection when the item is added or removed.</summary>
    private ListView? _listView;

    public ListViewItem()
    {
        _subItems = new ListViewSubItemCollection(this);
        _subItems.AddDefault();
    }

    public ListViewItem(string? text)
        : this()
    {
        Text = text ?? string.Empty;
    }

    public ListViewItem(string[]? items)
        : this()
    {
        AddSubItemTexts(items);
    }

    public ListViewItem(ListViewGroup? group)
        : this()
    {
        Group = group;
    }

    public ListViewItem(string? text, int imageIndex)
        : this(text)
    {
        ImageIndex = imageIndex;
    }

    public ListViewItem(string? text, string? imageKey)
        : this(text)
    {
        ImageKey = imageKey ?? string.Empty;
    }

    public ListViewItem(string[]? items, int imageIndex)
        : this(items)
    {
        ImageIndex = imageIndex;
    }

    public ListViewItem(string? text, ListViewGroup? group)
        : this(text)
    {
        Group = group;
    }

    public ListViewItem(string[]? items, ListViewGroup? group)
        : this(items)
    {
        Group = group;
    }

    public ListViewItem(string[]? items, string? imageKey)
        : this(items)
    {
        ImageKey = imageKey ?? string.Empty;
    }

    public ListViewItem(ListViewSubItem[]? subItems, int imageIndex)
        : this()
    {
        AddListViewSubItems(subItems);
        ImageIndex = imageIndex;
    }

    public ListViewItem(ListViewSubItem[]? subItems, string? imageKey)
        : this()
    {
        AddListViewSubItems(subItems);
        ImageKey = imageKey ?? string.Empty;
    }

    public ListViewItem(string? text, int imageIndex, ListViewGroup? group)
        : this(text, imageIndex)
    {
        Group = group;
    }

    public ListViewItem(string[]? items, int imageIndex, ListViewGroup? group)
        : this(items, imageIndex)
    {
        Group = group;
    }

    public ListViewItem(ListViewSubItem[]? subItems, int imageIndex, ListViewGroup? group)
        : this(subItems, imageIndex)
    {
        Group = group;
    }

    public ListViewItem(string? text, string? imageKey, ListViewGroup? group)
        : this(text, imageKey)
    {
        Group = group;
    }

    public ListViewItem(string[]? items, string? imageKey, ListViewGroup? group)
        : this(items, imageKey)
    {
        Group = group;
    }

    public ListViewItem(ListViewSubItem[]? subItems, string? imageKey, ListViewGroup? group)
        : this(subItems, imageKey)
    {
        Group = group;
    }

    public ListViewItem(string[]? items, int imageIndex, Color foreColor, Color backColor, Font? font)
        : this(items, imageIndex)
    {
        ApplyStyleToAllSubItems(foreColor, backColor, font);
    }

    public ListViewItem(string[]? items, string? imageKey, Color foreColor, Color backColor, Font? font)
        : this(items, imageKey)
    {
        ApplyStyleToAllSubItems(foreColor, backColor, font);
    }

    public ListViewItem(string[]? items, int imageIndex, Color foreColor, Color backColor, Font? font, ListViewGroup? group)
        : this(items, imageIndex, group)
    {
        ApplyStyleToAllSubItems(foreColor, backColor, font);
    }

    public ListViewItem(string[]? items, string? imageKey, Color foreColor, Color backColor, Font? font, ListViewGroup? group)
        : this(items, imageKey, group)
    {
        ApplyStyleToAllSubItems(foreColor, backColor, font);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BackColor
    {
        get => _subItems[0].BackColor;
        set => _subItems[0].BackColor = value;
    }

    [Browsable(false)]
    public Rectangle Bounds => _listView?.GetItemRect(Index) ?? Rectangle.Empty;

    [DefaultValue(false)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (value == _checked) return;

            // A ListView attached item goes through ItemCheck, which may change the value that actually gets applied.
            if (_listView is not null) _listView.SetItemChecked(this, value);
            else _checked = value;
        }
    }

    [Browsable(false)]
    public bool Focused
    {
        get => _focused;
        set
        {
            if (value == _focused) return;

            _focused = value;
            _listView?.InvalidateItem(this);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Font Font
    {
        get => _subItems[0].Font ?? _listView?.Font ?? Control.DefaultFont;
        set => _subItems[0].Font = value;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ForeColor
    {
        get => _subItems[0].ForeColor;
        set => _subItems[0].ForeColor = value;
    }

    [DefaultValue(null)]
    public ListViewGroup? Group
    {
        get => _group;
        set
        {
            if (ReferenceEquals(_group, value)) return;

            _group = value;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue(-1)]
    [TypeConverter(typeof(ImageIndexConverter))]
    [RelatedImageList(nameof(ImageList))]
    public int ImageIndex
    {
        get => _imageIndex;
        set
        {
            if (value == _imageIndex) return;

            _imageIndex = value;
            _listView?.InvalidateItem(this);
        }
    }

    [DefaultValue("")]
    [TypeConverter(typeof(ImageKeyConverter))]
    [RelatedImageList(nameof(ImageList))]
    public string ImageKey
    {
        get => _imageKey;
        set
        {
            _imageKey = value ?? string.Empty;
            _listView?.InvalidateItem(this);
        }
    }

    [Browsable(false)]
    public ImageList? ImageList => _listView?.SmallImageList;

    /// <summary>How many icon widths this item's icon and label are indented; stored but not yet reflected in icon-view layout.</summary>
    [DefaultValue(0)]
    public int IndentCount
    {
        get => _indentCount;
        set
        {
            if (value == _indentCount) return;

            _indentCount = value;
            _listView?.InvalidateItem(this);
        }
    }

    [Browsable(false)]
    public int Index => _listView is null ? -1 : _listView.IndexOfItem(this);

    [Browsable(false)]
    public ListView? ListView => _listView;

    [Browsable(false)]
    public string Name { get; set; } = string.Empty;

    /// <summary>This item's own pixel position in icon views with AutoArrange off; stored but not yet used by the auto-arranging layout this ListView always runs.</summary>
    [Browsable(false)]
    public Point Position
    {
        get => _position;
        set
        {
            if (value == _position) return;

            _position = value;
            _listView?.InvalidateItem(this);
        }
    }

    [Browsable(false)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;

            if (_listView is not null) _listView.SetItemSelected(this, value);
            else _selected = value;
        }
    }

    /// <summary>The index into StateImageList this item's checkbox-like state glyph draws from; -1 for none.</summary>
    [DefaultValue(-1)]
    [TypeConverter(typeof(ListViewItemStateImageIndexConverter))]
    [RelatedImageList("ListView.StateImageList")]
    public int StateImageIndex
    {
        get => _stateImageIndex;
        set
        {
            if (value is < -1 or > 14) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _stateImageIndex) return;

            _stateImageIndex = value;
            _listView?.InvalidateItem(this);
        }
    }

    public ListViewSubItemCollection SubItems => _subItems;

    [DefaultValue(null)]
    public object? Tag { get; set; }

    public string Text
    {
        get => _subItems[0].Text;
        set => _subItems[0].Text = value ?? string.Empty;
    }

    [DefaultValue("")]
    public string ToolTipText { get; set; } = string.Empty;

    [DefaultValue(true)]
    public bool UseItemStyleForSubItems { get; set; } = true;

    public virtual object Clone()
    {
        var clone = new ListViewItem
        {
            ImageIndex = _imageIndex,
            ImageKey = _imageKey,
            Checked = _checked,
            Group = _group,
            Tag = Tag,
            ToolTipText = ToolTipText,
            UseItemStyleForSubItems = UseItemStyleForSubItems,
            Name = Name,
        };

        clone.SubItems.Clear();
        foreach (ListViewSubItem subItem in _subItems)
        {
            clone.SubItems.Add(new ListViewSubItem(clone, subItem.Text, subItem.ForeColor, subItem.BackColor, subItem.Font!));
        }

        return clone;
    }

    /// <summary>Opens LabelEdit's in-place TextBox over this item, same as the user pressing F2 on it; no-op outside a ListView.</summary>
    public void BeginEdit() => _listView?.BeginEditCore(this);

    public virtual void EnsureVisible() => _listView?.EnsureVisible(Index);

    /// <summary>The nearest other item to this one's current location, in the given direction; null outside a ListView.</summary>
    public ListViewItem FindNearestItem(SearchDirectionHint searchDirection) =>
        _listView?.FindNearestItem(searchDirection, Bounds.Location)!;

    public Rectangle GetBounds(ItemBoundsPortion portion) => _listView?.GetItemRect(Index, portion) ?? Rectangle.Empty;

    public ListViewSubItem GetSubItemAt(int x, int y)
    {
        if (_listView is null) return _subItems[0];

        var hit = _listView.HitTest(x, y);
        return hit.Item == this && hit.SubItem is not null ? hit.SubItem : _subItems[0];
    }

    public virtual void Remove() => _listView?.Items.Remove(this);

    public override string ToString() => $"ListViewItem: {{{Text}}}";

    /// <summary>Attaches or detaches this item from a ListView; called only by ListView.ListViewItemCollection.</summary>
    internal void SetListView(ListView? listView)
    {
        _listView = listView;

        // Carry the selection the item had while unattached into the ListView's own bookkeeping.
        if (listView is not null && _selected) listView.SetItemSelected(this, true);
    }

    /// <summary>The selection flag, read and written directly by ListView without going through the public setter's event.</summary>
    internal bool SelectedCore
    {
        get => _selected;
        set => _selected = value;
    }

    /// <summary>The check flag, read and written directly by ListView once ItemCheck has settled on the value to apply.</summary>
    internal bool CheckedCore
    {
        get => _checked;
        set => _checked = value;
    }

    private void AddSubItemTexts(string[]? items)
    {
        if (items is null || items.Length == 0) return;

        Text = items[0];
        for (var i = 1; i < items.Length; i++) _subItems.Add(items[i]);
    }

    /// <summary>Appends ready-made sub-items, as the ListViewSubItem[] constructors do; the default empty one from the parameterless constructor stays at index 0 only if none are given.</summary>
    private void AddListViewSubItems(ListViewSubItem[]? subItems)
    {
        if (subItems is null || subItems.Length == 0) return;

        _subItems.ReplaceAll(subItems);
    }

    /// <summary>Applies one fore colour, back colour and font to every sub-item, as the colour-and-font constructors do.</summary>
    private void ApplyStyleToAllSubItems(Color foreColor, Color backColor, Font? font)
    {
        foreach (ListViewSubItem subItem in _subItems)
        {
            subItem.ForeColor = foreColor;
            subItem.BackColor = backColor;
            subItem.Font = font;
        }
    }

    [DefaultProperty("Text")]
    [Serializable]
    public class ListViewSubItem
    {
        private string _text = string.Empty;
        private Color _foreColor;
        private Color _backColor;
        private Font? _font;

        public ListViewSubItem()
        {
        }

        public ListViewSubItem(ListViewItem? owner, string? text)
        {
            Owner = owner;
            _text = text ?? string.Empty;
        }

        public ListViewSubItem(ListViewItem? owner, string? text, Color foreColor, Color backColor, Font font)
        {
            Owner = owner;
            _text = text ?? string.Empty;
            _foreColor = foreColor;
            _backColor = backColor;
            _font = font;
        }

        public Color BackColor
        {
            get => _backColor.IsEmpty ? Owner?.ListView?.BackColor ?? SystemColors.Window : _backColor;
            set
            {
                _backColor = value;
                Owner?.ListView?.InvalidateItem(Owner);
            }
        }

        [Browsable(false)]
        public Rectangle Bounds => Owner?.ListView?.GetSubItemBounds(Owner, Owner.SubItems.IndexOf(this)) ?? Rectangle.Empty;

        public Font? Font
        {
            get => _font;
            set
            {
                _font = value;
                Owner?.ListView?.InvalidateItem(Owner);
            }
        }

        public Color ForeColor
        {
            get => _foreColor.IsEmpty ? Owner?.ListView?.ForeColor ?? SystemColors.WindowText : _foreColor;
            set
            {
                _foreColor = value;
                Owner?.ListView?.InvalidateItem(Owner);
            }
        }

        public string Name { get; set; } = string.Empty;

        [DefaultValue(null)]
        public object? Tag { get; set; }

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                Owner?.ListView?.InvalidateItem(Owner);
            }
        }

        /// <summary>The item this sub-item belongs to; null only for a sub-item not yet added to any item's collection.</summary>
        internal ListViewItem? Owner { get; set; }

        internal ListViewSubItem CloneFor(ListViewItem owner) => new(owner, _text)
        {
            _foreColor = _foreColor,
            _backColor = _backColor,
            _font = _font,
            Name = Name,
            Tag = Tag,
        };

        public void ResetStyle()
        {
            _foreColor = Color.Empty;
            _backColor = Color.Empty;
            _font = null;
        }

        public override string ToString() => $"ListViewSubItem: {{{Text}}}";
    }

    public class ListViewSubItemCollection : IList
    {
        private readonly ListViewItem _owner;
        private readonly List<ListViewSubItem> _subItems = [];

        public ListViewSubItemCollection(ListViewItem owner)
        {
            _owner = owner;
        }

        public int Count => _subItems.Count;

        public ListViewSubItem this[int index]
        {
            get
            {
                if (index < 0 || index >= _subItems.Count) throw new ArgumentOutOfRangeException(nameof(index));
                return _subItems[index];
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                if (index < 0 || index >= _subItems.Count) throw new ArgumentOutOfRangeException(nameof(index));

                value.Owner = _owner;
                _subItems[index] = value;
                _owner.ListView?.InvalidateItem(_owner);
            }
        }

        public virtual ListViewSubItem? this[string key] => _subItems.Find(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));

        public ListViewSubItem Add(ListViewSubItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            item.Owner = _owner;
            _subItems.Add(item);
            _owner.ListView?.InvalidateItem(_owner);

            return item;
        }

        public ListViewSubItem Add(string? text) => Add(new ListViewSubItem(_owner, text));

        public ListViewSubItem Add(string? text, Color foreColor, Color backColor, Font font) =>
            Add(new ListViewSubItem(_owner, text, foreColor, backColor, font));

        public void AddRange(params ListViewSubItem[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var item in items) Add(item);
        }

        public void AddRange(params string[] items)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var text in items) Add(text);
        }

        public void AddRange(string[] items, Color foreColor, Color backColor, Font font)
        {
            ArgumentNullException.ThrowIfNull(items);
            foreach (var text in items) Add(text, foreColor, backColor, font);
        }

        public void Clear()
        {
            _subItems.Clear();
            AddDefault();
            _owner.ListView?.InvalidateItem(_owner);
        }

        public bool Contains(ListViewSubItem? subItem) => subItem is not null && _subItems.Contains(subItem);

        public virtual bool ContainsKey(string? key) => IndexOfKey(key) >= 0;

        public virtual int IndexOfKey(string? key) => string.IsNullOrEmpty(key) ? -1 :
            _subItems.FindIndex(item => string.Equals(item.Name, key, StringComparison.OrdinalIgnoreCase));

        public virtual void RemoveByKey(string? key)
        {
            var index = IndexOfKey(key);
            if (index >= 0) RemoveAt(index);
        }

        public bool IsReadOnly => false;

        public IEnumerator GetEnumerator() => _subItems.GetEnumerator();

        public int IndexOf(ListViewSubItem? subItem) => subItem is null ? -1 : _subItems.IndexOf(subItem);

        public void Insert(int index, ListViewSubItem item)
        {
            ArgumentNullException.ThrowIfNull(item);

            item.Owner = _owner;
            _subItems.Insert(index, item);
            _owner.ListView?.InvalidateItem(_owner);
        }

        public void Remove(ListViewSubItem? item)
        {
            if (item is not null && _subItems.Remove(item)) _owner.ListView?.InvalidateItem(_owner);
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _subItems.Count) throw new ArgumentOutOfRangeException(nameof(index));

            _subItems.RemoveAt(index);
            _owner.ListView?.InvalidateItem(_owner);
        }

        /// <summary>The sub-item at column 0 always exists, standing for the item's own Text; created once, in the item's constructor.</summary>
        internal void AddDefault() => _subItems.Add(new ListViewSubItem(_owner, string.Empty));

        /// <summary>Replaces every sub-item with the given ones, without AddDefault's rule of always keeping an empty one; used by the ListViewSubItem[] constructors.</summary>
        internal void ReplaceAll(IEnumerable<ListViewSubItem> items)
        {
            _subItems.Clear();
            foreach (var item in items)
            {
                item.Owner = _owner;
                _subItems.Add(item);
            }

            if (_subItems.Count == 0) AddDefault();
        }

        bool IList.IsFixedSize => false;

        bool IList.IsReadOnly => false;

        bool ICollection.IsSynchronized => false;

        object ICollection.SyncRoot => this;

        object? IList.this[int index]
        {
            get => this[index];
            set => this[index] = (ListViewSubItem)value!;
        }

        int IList.Add(object? value)
        {
            var added = Add((ListViewSubItem)value!);
            return _subItems.IndexOf(added);
        }

        bool IList.Contains(object? value) => value is ListViewSubItem subItem && Contains(subItem);

        int IList.IndexOf(object? value) => value is ListViewSubItem subItem ? IndexOf(subItem) : -1;

        void IList.Insert(int index, object? value) => Insert(index, (ListViewSubItem)value!);

        void IList.Remove(object? value)
        {
            if (value is ListViewSubItem subItem) Remove(subItem);
        }

        void ICollection.CopyTo(Array array, int index) => ((ICollection)_subItems).CopyTo(array, index);
    }
}
