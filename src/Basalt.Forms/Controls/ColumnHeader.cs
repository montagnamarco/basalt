using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>One column of a <see cref="ListView"/> in <see cref="View.Details"/>: its caption, width and alignment.</summary>
public class ColumnHeader : Component, ICloneable
{
    private string _text = string.Empty;
    private int _width = 60;
    private HorizontalAlignment _textAlign = HorizontalAlignment.Left;

    /// <summary>The ListView this column belongs to, set by ColumnHeaderCollection when the column is added or removed.</summary>
    private ListView? _listView;

    public ColumnHeader()
    {
    }

    [DefaultValue(-1)]
    public int DisplayIndex
    {
        get => _listView is null ? -1 : _listView.Columns.IndexOf(this);
        set
        {
            if (_listView is null) return;
            _listView.MoveColumnDisplayIndex(this, value);
        }
    }

    [Browsable(false)]
    public int Index => _listView is null ? -1 : _listView.Columns.IndexOf(this);

    [Browsable(false)]
    public ListView? ListView => _listView;

    [Browsable(false)]
    [DefaultValue(null)]
    public string? Name { get; set; }

    [DefaultValue(null)]
    public object? Tag { get; set; }

    [DefaultValue("")]
    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue(HorizontalAlignment.Left)]
    public HorizontalAlignment TextAlign
    {
        get => _textAlign;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(HorizontalAlignment));

            _textAlign = value;
            _listView?.InvalidateHeader();
        }
    }

    [DefaultValue(60)]
    public int Width
    {
        get => _width;
        set
        {
            if (value == _width) return;

            _width = value;
            _listView?.NotifyColumnWidthChanged(this);
        }
    }

    public object Clone()
    {
        var clone = new ColumnHeader
        {
            Text = _text,
            Width = _width,
            TextAlign = _textAlign,
            Name = Name,
            Tag = Tag,
        };

        return clone;
    }

    /// <summary>Attaches or detaches this column from a ListView; called only by ColumnHeaderCollection.</summary>
    internal void SetListView(ListView? listView) => _listView = listView;

    public override string ToString() => $"ColumnHeader: Text: {_text}";
}
