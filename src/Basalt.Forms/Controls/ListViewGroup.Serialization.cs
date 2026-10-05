using System.ComponentModel;
using System.Runtime.Serialization;

namespace System.Windows.Forms;

public sealed partial class ListViewGroup
{
    private string _footer = string.Empty;
    private HorizontalAlignment _footerAlignment = HorizontalAlignment.Left;
    private string _subtitle = string.Empty;
    private string _taskLink = string.Empty;
    private ListViewGroupCollapsedState _collapsedState;
    private int _titleImageIndex = -1;
    private string _titleImageKey = string.Empty;

    private ListViewGroup(SerializationInfo info, StreamingContext context) : this()
    {
        ArgumentNullException.ThrowIfNull(info);
        foreach (SerializationEntry entry in info)
        {
            switch (entry.Name)
            {
                case nameof(Header): Header = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(HeaderAlignment): HeaderAlignment = (HorizontalAlignment)info.GetValue(entry.Name, typeof(HorizontalAlignment))!; break;
                case nameof(Footer): Footer = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(FooterAlignment): FooterAlignment = (HorizontalAlignment)info.GetValue(entry.Name, typeof(HorizontalAlignment))!; break;
                case nameof(Tag): Tag = entry.Value; break;
                case nameof(Name): Name = info.GetString(entry.Name); break;
                case nameof(Subtitle): Subtitle = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(TaskLink): TaskLink = info.GetString(entry.Name) ?? string.Empty; break;
                case nameof(CollapsedState): CollapsedState = (ListViewGroupCollapsedState)info.GetValue(entry.Name, typeof(ListViewGroupCollapsedState))!; break;
                case nameof(TitleImageIndex): TitleImageIndex = info.GetInt32(entry.Name); break;
                case nameof(TitleImageKey): TitleImageKey = info.GetString(entry.Name) ?? string.Empty; break;
            }
        }
    }

    [DefaultValue("")]
    public string Footer
    {
        get => _footer;
        set { _footer = value ?? string.Empty; _listView?.InvalidateHeader(); }
    }

    [DefaultValue(HorizontalAlignment.Left)]
    public HorizontalAlignment FooterAlignment
    {
        get => _footerAlignment;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(HorizontalAlignment));
            _footerAlignment = value;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue("")]
    public string Subtitle
    {
        get => _subtitle;
        set { _subtitle = value ?? string.Empty; _listView?.InvalidateHeader(); }
    }

    [DefaultValue("")]
    public string TaskLink
    {
        get => _taskLink;
        set { _taskLink = value ?? string.Empty; _listView?.InvalidateHeader(); }
    }

    [DefaultValue(ListViewGroupCollapsedState.Default)]
    public ListViewGroupCollapsedState CollapsedState
    {
        get => _collapsedState;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ListViewGroupCollapsedState));
            if (_collapsedState == value) return;
            _collapsedState = value;
            _listView?.GroupCollapsedStateChangedCore(this);
        }
    }

    [DefaultValue(-1)]
    [TypeConverter(typeof(ImageIndexConverter))]
    [RelatedImageList("ListView.GroupImageList")]
    public int TitleImageIndex
    {
        get => _titleImageIndex;
        set
        {
            if (value < -1) throw new ArgumentOutOfRangeException(nameof(value));
            _titleImageIndex = value;
            if (value >= 0) _titleImageKey = string.Empty;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue("")]
    [TypeConverter(typeof(ImageKeyConverter))]
    [RelatedImageList("ListView.GroupImageList")]
    public string TitleImageKey
    {
        get => _titleImageKey;
        set
        {
            _titleImageKey = value ?? string.Empty;
            if (_titleImageKey.Length > 0) _titleImageIndex = -1;
            _listView?.InvalidateHeader();
        }
    }

    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        ArgumentNullException.ThrowIfNull(info);
        info.AddValue(nameof(Header), Header);
        info.AddValue(nameof(HeaderAlignment), HeaderAlignment);
        info.AddValue(nameof(Footer), Footer);
        info.AddValue(nameof(FooterAlignment), FooterAlignment);
        info.AddValue(nameof(Tag), Tag);
        if (!string.IsNullOrEmpty(Name)) info.AddValue(nameof(Name), Name);
        if (Subtitle.Length > 0) info.AddValue(nameof(Subtitle), Subtitle);
        if (TaskLink.Length > 0) info.AddValue(nameof(TaskLink), TaskLink);
        if (CollapsedState != ListViewGroupCollapsedState.Default) info.AddValue(nameof(CollapsedState), CollapsedState);
        if (TitleImageIndex >= 0) info.AddValue(nameof(TitleImageIndex), TitleImageIndex);
        if (TitleImageKey.Length > 0) info.AddValue(nameof(TitleImageKey), TitleImageKey);
    }
}
