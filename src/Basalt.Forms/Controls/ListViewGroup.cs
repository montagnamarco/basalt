using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>A header a ListView's Items in <see cref="View.Details"/> can be clustered under, when <see cref="ListView.ShowGroups"/> is set.</summary>
public sealed class ListViewGroup
{
    private string _header = string.Empty;
    private HorizontalAlignment _headerAlignment = HorizontalAlignment.Left;

    /// <summary>The ListView this group belongs to, set by ListViewGroupCollection when the group is added or removed.</summary>
    private ListView? _listView;

    public ListViewGroup()
    {
    }

    public ListViewGroup(string? header)
    {
        _header = header ?? string.Empty;
    }

    public ListViewGroup(string? key, string? headerText)
    {
        Name = key;
        _header = headerText ?? string.Empty;
    }

    public ListViewGroup(string? header, HorizontalAlignment headerAlignment)
    {
        _header = header ?? string.Empty;
        _headerAlignment = headerAlignment;
    }

    [DefaultValue("")]
    public string Header
    {
        get => _header;
        set
        {
            _header = value ?? string.Empty;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue(HorizontalAlignment.Left)]
    public HorizontalAlignment HeaderAlignment
    {
        get => _headerAlignment;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(HorizontalAlignment));

            _headerAlignment = value;
            _listView?.InvalidateHeader();
        }
    }

    /// <summary>The items currently assigned to this group, live over the owning ListView's own Items.</summary>
    [Browsable(false)]
    public ListView.ListViewItemCollection Items => _listView is null
        ? new ListView.ListViewItemCollection(null, this)
        : _listView.ItemsInGroup(this);

    [Browsable(false)]
    public ListView? ListView => _listView;

    [DefaultValue("")]
    public string? Name { get; set; }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    /// <summary>Attaches or detaches this group from a ListView; called only by ListViewGroupCollection.</summary>
    internal void SetListView(ListView? listView) => _listView = listView;

    public override string ToString() => Header;
}
