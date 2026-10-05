using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

public partial class DataGridView
{
    private HScrollBar? _horizontalScrollBar;
    private VScrollBar? _verticalScrollBar;
    private int _horizontalScrollingOffset;
    private int _verticalScrollingOffset;
    private ScrollBars _scrollBars = ScrollBars.Both;
    private bool _updatingScrollBars;

    private int FrozenColumnsWidth => ColumnsInDisplayOrder().Where(column => column.Visible && column.Frozen).Sum(column => column.Width);
    private int FrozenRowsHeight => Rows.Cast<DataGridViewRow>().Where(row => row.Visible && row.Frozen).Sum(row => row.Height);

    protected ScrollBar HorizontalScrollBar => EnsureHorizontalScrollBar();
    protected ScrollBar VerticalScrollBar => EnsureVerticalScrollBar();

    [DefaultValue(ScrollBars.Both)]
    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ScrollBars));
            if (_scrollBars == value) return;
            _scrollBars = value;
            PerformLayout();
            Invalidate();
        }
    }

    public override Rectangle DisplayRectangle => new(OriginX, OriginY,
        Math.Max(0, ClientSize.Width - OriginX - (_verticalScrollBar?.Visible == true ? _verticalScrollBar.Width : 0)),
        Math.Max(0, ClientSize.Height - OriginY - (_horizontalScrollBar?.Visible == true ? _horizontalScrollBar.Height : 0)));

    public int HorizontalScrollingOffset
    {
        get => _horizontalScrollingOffset;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            SetScrollingOffset(value, true);
        }
    }

    public int VerticalScrollingOffset => _verticalScrollingOffset;

    public int FirstDisplayedScrollingColumnIndex
    {
        get => ColumnsInDisplayOrder().FirstOrDefault(column => column.Visible && !column.Frozen && ColumnLeft(column) + column.Width > OriginX + FrozenColumnsWidth)?.Index ?? -1;
        set
        {
            if (value < 0 || value >= Columns.Count) throw new ArgumentOutOfRangeException(nameof(value));
            var column = Columns[value];
            if (!column.Visible || column.Frozen) throw new InvalidOperationException("The scrolling column must be visible and not frozen.");
            HorizontalScrollingOffset = ColumnsInDisplayOrder().TakeWhile(candidate => candidate != column).Where(candidate => candidate.Visible && !candidate.Frozen).Sum(candidate => candidate.Width);
        }
    }

    public int FirstDisplayedScrollingRowIndex
    {
        get => Enumerable.Range(0, Rows.Count).FirstOrDefault(index => Rows[index].Visible && !Rows[index].Frozen && RowTop(index) + Rows[index].Height > OriginY + FrozenRowsHeight, -1);
        set
        {
            if (value < 0 || value >= Rows.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (!Rows[value].Visible || Rows[value].Frozen) throw new InvalidOperationException("The scrolling row must be visible and not frozen.");
            SetScrollingOffset(Rows.Cast<DataGridViewRow>().Take(value).Where(row => row.Visible && !row.Frozen).Sum(row => row.Height), false);
        }
    }

    public int FirstDisplayedScrollingColumnHiddenWidth => FirstDisplayedScrollingColumnIndex is var index && index >= 0
        ? Math.Max(0, OriginX + FrozenColumnsWidth - ColumnLeft(Columns[index])) : 0;

    public DataGridViewCell? FirstDisplayedCell
    {
        get
        {
            var row = FirstVisibleRowIndex();
            var column = ColumnsInDisplayOrder().FirstOrDefault(candidate => candidate.Visible && DisplayedColumnWidth(candidate) > 0);
            return row >= 0 && column is not null ? Rows[row].Cells[column.Index] : null;
        }
        set
        {
            if (value is null) { HorizontalScrollingOffset = 0; SetScrollingOffset(0, false); return; }
            if (value.DataGridView != this) throw new ArgumentException("The cell must belong to this grid.", nameof(value));
            if (!value.OwningColumn!.Frozen) FirstDisplayedScrollingColumnIndex = value.ColumnIndex;
            if (!value.OwningRow!.Frozen) FirstDisplayedScrollingRowIndex = value.RowIndex;
        }
    }

    public event ScrollEventHandler? Scroll;
    protected virtual void OnScroll(ScrollEventArgs e) => Scroll?.Invoke(this, e);

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        UpdateScrollBars();
        PositionEditingControlCore();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e is HandledMouseEventArgs { Handled: true }) return;
        var rowHeight = CurrentCell?.OwningRow?.Height ?? RowTemplate.Height;
        SetScrollingOffset(Math.Max(0, _verticalScrollingOffset - Math.Sign(e.Delta) * rowHeight * 3), false);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
    }

    private HScrollBar EnsureHorizontalScrollBar()
    {
        if (_horizontalScrollBar is not null) return _horizontalScrollBar;
        _horizontalScrollBar = new HScrollBar { Visible = false };
        _horizontalScrollBar.ValueChanged += (_, _) => { if (!_updatingScrollBars) SetScrollingOffset(_horizontalScrollBar.Value, true); };
        Controls.Add(_horizontalScrollBar);
        return _horizontalScrollBar;
    }

    private VScrollBar EnsureVerticalScrollBar()
    {
        if (_verticalScrollBar is not null) return _verticalScrollBar;
        _verticalScrollBar = new VScrollBar { Visible = false };
        _verticalScrollBar.ValueChanged += (_, _) => { if (!_updatingScrollBars) SetScrollingOffset(_verticalScrollBar.Value, false); };
        Controls.Add(_verticalScrollBar);
        return _verticalScrollBar;
    }

    private void UpdateScrollBars()
    {
        if (_updatingScrollBars || IsDisposed) return;
        _updatingScrollBars = true;
        try
        {
            var horizontal = EnsureHorizontalScrollBar();
            var vertical = EnsureVerticalScrollBar();
            var width = ColumnsInDisplayOrder().Where(column => column.Visible).Sum(column => column.Width);
            var height = Rows.Cast<DataGridViewRow>().Where(row => row.Visible).Sum(row => row.Height);
            var availableWidth = Math.Max(0, ClientSize.Width - OriginX);
            var availableHeight = Math.Max(0, ClientSize.Height - OriginY);
            var showHorizontal = (_scrollBars & ScrollBars.Horizontal) != 0 && width > availableWidth;
            var showVertical = (_scrollBars & ScrollBars.Vertical) != 0 && height > availableHeight - (showHorizontal ? horizontal.Height : 0);
            showHorizontal = (_scrollBars & ScrollBars.Horizontal) != 0 && width > availableWidth - (showVertical ? vertical.Width : 0);
            horizontal.Visible = showHorizontal;
            vertical.Visible = showVertical;
            horizontal.SetBounds(OriginX, ClientSize.Height - horizontal.Height, DisplayRectangle.Width, horizontal.Height);
            vertical.SetBounds(ClientSize.Width - vertical.Width, OriginY, vertical.Width, DisplayRectangle.Height);
            horizontal.Maximum = Math.Max(0, width - 1);
            vertical.Maximum = Math.Max(0, height - 1);
            horizontal.LargeChange = Math.Max(1, DisplayRectangle.Width);
            vertical.LargeChange = Math.Max(1, DisplayRectangle.Height);
            horizontal.SmallChange = 20;
            vertical.SmallChange = RowTemplate.Height;
            _horizontalScrollingOffset = Math.Clamp(_horizontalScrollingOffset, 0, Math.Max(0, width - DisplayRectangle.Width));
            _verticalScrollingOffset = Math.Clamp(_verticalScrollingOffset, 0, Math.Max(0, height - DisplayRectangle.Height));
            horizontal.Value = _horizontalScrollingOffset;
            vertical.Value = _verticalScrollingOffset;
            horizontal.BringToFront();
            vertical.BringToFront();
        }
        finally { _updatingScrollBars = false; }
    }

    private void SetScrollingOffset(int value, bool horizontal)
    {
        var oldValue = horizontal ? _horizontalScrollingOffset : _verticalScrollingOffset;
        if (horizontal) _horizontalScrollingOffset = value;
        else _verticalScrollingOffset = value;
        UpdateScrollBars();
        var current = horizontal ? _horizontalScrollingOffset : _verticalScrollingOffset;
        if (current == oldValue) return;
        PositionEditingControlCore();
        Invalidate();
        OnScroll(new ScrollEventArgs(ScrollEventType.ThumbPosition, oldValue, current,
            horizontal ? ScrollOrientation.HorizontalScroll : ScrollOrientation.VerticalScroll));
    }
}
