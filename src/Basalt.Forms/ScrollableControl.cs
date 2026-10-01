using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// A control whose children can be scrolled into view, laid out inside its
/// padding.
/// </summary>
/// <remarks>
/// With AutoScroll on, the control measures how far its children reach and
/// shows the window's own scroll bars (in its frame, as WS_HSCROLL and
/// WS_VSCROLL do) when they reach beyond the client area. Scrolling moves the
/// display rectangle: children keep their places in it, and their Location
/// changes by the amount scrolled, as on Windows.
/// </remarks>
public class ScrollableControl : Control
{
    protected const int ScrollStateAutoScrolling = 0x0001;
    protected const int ScrollStateHScrollVisible = 0x0002;
    protected const int ScrollStateVScrollVisible = 0x0004;
    protected const int ScrollStateUserHasScrolled = 0x0008;
    protected const int ScrollStateFullDrag = 0x0010;

    private int _scrollState;

    /// <summary>The virtual area: its location is the (non-positive) scroll offset, its size the extent of the content.</summary>
    private Rectangle _displayRect;

    private Size _autoScrollMinSize;
    private Size _autoScrollMargin;
    private HScrollProperties? _horizontalScroll;
    private VScrollProperties? _verticalScroll;

    /// <summary>A press on one of the frame's scroll bars being tracked: which bar, and where the thumb was grabbed.</summary>
    private (bool Vertical, int GrabOffset)? _thumbDrag;

    public ScrollableControl()
    {
        SetStyle(ControlStyles.ContainerControl, true);
        SetStyle(ControlStyles.AllPaintingInWmPaint, false);
        SetScrollState(ScrollStateFullDrag, true);
    }

    /// <summary>Shows scroll bars when the children reach beyond the client area.</summary>
    [DefaultValue(false)]
    [Localizable(true)]
    public virtual bool AutoScroll
    {
        get => GetScrollState(ScrollStateAutoScrolling);
        set
        {
            if (value == AutoScroll) return;

            SetScrollState(ScrollStateAutoScrolling, value);
            if (!value)
            {
                _displayRect = Rectangle.Empty;
                SetBars(horizontal: false, vertical: false);
            }

            PerformLayout(this, nameof(AutoScroll));
        }
    }

    /// <summary>The smallest area the scroll bars cover, whatever the children.</summary>
    [Localizable(true)]
    public Size AutoScrollMinSize
    {
        get => _autoScrollMinSize;
        set
        {
            if (value == _autoScrollMinSize) return;

            _autoScrollMinSize = value;
            if (!value.IsEmpty) SetScrollState(ScrollStateAutoScrolling, true);
            PerformLayout(this, nameof(AutoScrollMinSize));
        }
    }

    /// <summary>Space kept beyond the farthest child when scrolling to it.</summary>
    [Localizable(true)]
    public Size AutoScrollMargin
    {
        get => _autoScrollMargin;
        set
        {
            if (value.Width < 0 || value.Height < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "AutoScrollMargin cannot be negative.");

            _autoScrollMargin = value;
            PerformLayout(this, nameof(AutoScrollMargin));
        }
    }

    public void SetAutoScrollMargin(int x, int y) => AutoScrollMargin = new Size(Math.Max(x, 0), Math.Max(y, 0));

    /// <summary>The scroll offset: negative when scrolled; set with the positive amount to scroll to.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point AutoScrollPosition
    {
        get => new(_displayRect.X, _displayRect.Y);
        set => SetDisplayRectLocation(-Math.Abs(value.X), -Math.Abs(value.Y));
    }

    [Browsable(false)]
    public HScrollProperties HorizontalScroll => _horizontalScroll ??= new HScrollProperties(this);

    [Browsable(false)]
    public VScrollProperties VerticalScroll => _verticalScroll ??= new VScrollProperties(this);

    protected bool HScroll
    {
        get => GetScrollState(ScrollStateHScrollVisible);
        set => SetBars(value, VScroll);
    }

    protected bool VScroll
    {
        get => GetScrollState(ScrollStateVScrollVisible);
        set => SetBars(HScroll, value);
    }

    protected bool GetScrollState(int bit) => (_scrollState & bit) == bit;

    protected void SetScrollState(int bit, bool value) => _scrollState = value ? _scrollState | bit : _scrollState & ~bit;

    /// <summary>The window's own scroll bars are part of its frame.</summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            if (HScroll) parameters.Style |= WindowStyles.HorizontalScroll;
            if (VScroll) parameters.Style |= WindowStyles.VerticalScroll;

            return parameters;
        }
    }

    /// <summary>The virtual area children are laid out in, scrolled and inside the padding.</summary>
    [Browsable(false)]
    public override Rectangle DisplayRectangle
    {
        get
        {
            var rectangle = ClientRectangle;

            if (!_displayRect.IsEmpty)
            {
                rectangle.X = _displayRect.X;
                rectangle.Y = _displayRect.Y;
                if (HScroll) rectangle.Width = _displayRect.Width;
                if (VScroll) rectangle.Height = _displayRect.Height;
            }

            var padding = Padding;

            return new Rectangle(
                rectangle.X + padding.Left,
                rectangle.Y + padding.Top,
                Math.Max(rectangle.Width - padding.Horizontal, 0),
                Math.Max(rectangle.Height - padding.Vertical, 0));
        }
    }

    public event ScrollEventHandler? Scroll
    {
        add => Events.AddHandler(s_scrollEvent, value);
        remove => Events.RemoveHandler(s_scrollEvent, value);
    }

    private static readonly object s_scrollEvent = new();

    protected virtual void OnScroll(ScrollEventArgs se) => ((ScrollEventHandler?)Events[s_scrollEvent])?.Invoke(this, se);

    /// <summary>Lays out the children, then shows the scroll bars their extent needs.</summary>
    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        var barsBefore = (HScroll, VScroll);
        AdjustFormScrollbars(AutoScroll);

        // A bar that appeared or went away changed the client area: lay out once more in the new one.
        if (barsBefore != (HScroll, VScroll))
        {
            LayoutEngine.Layout(this, levent);
            AdjustFormScrollbars(AutoScroll);
        }
    }

    /// <summary>
    /// Shows or hides the scroll bars for the children's extent: a bar is
    /// needed when the content is wider (or taller) than the client area,
    /// counting that the other bar, if shown, takes room too.
    /// </summary>
    protected virtual void AdjustFormScrollbars(bool displayScrollbars)
    {
        if (!displayScrollbars)
        {
            if (!_displayRect.IsEmpty) SetDisplayRectLocation(0, 0);

            _displayRect = Rectangle.Empty;
            SetBars(horizontal: false, vertical: false);
            return;
        }

        var extent = ContentExtent();

        // The client area as it would be without any bar.
        var full = ClientSize;
        if (VScroll) full.Width += NonClientArea.ScrollBarSize;
        if (HScroll) full.Height += NonClientArea.ScrollBarSize;

        var needHorizontal = extent.Width > full.Width;
        var needVertical = extent.Height > full.Height;
        if (needHorizontal && !needVertical) needVertical = extent.Height > full.Height - NonClientArea.ScrollBarSize;
        if (needVertical && !needHorizontal) needHorizontal = extent.Width > full.Width - NonClientArea.ScrollBarSize;

        SetBars(needHorizontal, needVertical);

        var client = ClientSize;
        var width = Math.Max(extent.Width, client.Width);
        var height = Math.Max(extent.Height, client.Height);
        var x = Math.Clamp(_displayRect.X, -(width - client.Width), 0);
        var y = Math.Clamp(_displayRect.Y, -(height - client.Height), 0);

        var offsetBefore = new Point(_displayRect.X, _displayRect.Y);
        _displayRect = new Rectangle(offsetBefore.X, offsetBefore.Y, width, height);
        if (x != offsetBefore.X || y != offsetBefore.Y) SetDisplayRectLocation(x, y);

        SyncScrollProperties();
    }

    /// <summary>How far the children reach in the virtual area, plus the margin; at least AutoScrollMinSize.</summary>
    private Size ContentExtent()
    {
        var right = 0;
        var bottom = 0;

        foreach (Control child in Controls)
        {
            if (!child.DesiredVisibility) continue;

            // Docked children follow the client area; they never ask for scrolling.
            if (child.Dock != DockStyle.None) continue;

            right = Math.Max(right, child.Right - _displayRect.X + child.Margin.Right);
            bottom = Math.Max(bottom, child.Bottom - _displayRect.Y + child.Margin.Bottom);
        }

        if (right > 0) right += _autoScrollMargin.Width + Padding.Right;
        if (bottom > 0) bottom += _autoScrollMargin.Height + Padding.Bottom;

        return new Size(Math.Max(right, _autoScrollMinSize.Width), Math.Max(bottom, _autoScrollMinSize.Height));
    }

    private void SetBars(bool horizontal, bool vertical)
    {
        if (horizontal == HScroll && vertical == VScroll) return;

        SetScrollState(ScrollStateHScrollVisible, horizontal);
        SetScrollState(ScrollStateVScrollVisible, vertical);

        // The frame changes, and with it the client area.
        UpdateStyles();
    }

    /// <summary>Scrolls to the offset (zero or negative), moving the children by the difference.</summary>
    protected void SetDisplayRectLocation(int x, int y)
    {
        var client = ClientSize;
        var width = Math.Max(_displayRect.Width, client.Width);
        var height = Math.Max(_displayRect.Height, client.Height);

        x = Math.Clamp(x, Math.Min(-(width - client.Width), 0), 0);
        y = Math.Clamp(y, Math.Min(-(height - client.Height), 0), 0);

        var dx = x - _displayRect.X;
        var dy = y - _displayRect.Y;
        if (dx == 0 && dy == 0) return;

        _displayRect.X = x;
        _displayRect.Y = y;

        foreach (Control child in Controls) child.ScrollBy(dx, dy);

        SyncScrollProperties();
        Invalidate(true);
    }

    /// <summary>Scrolls so that the control, a descendant, is in view, as focusing it does.</summary>
    public void ScrollControlIntoView(Control? activeControl)
    {
        if (activeControl is null || !AutoScroll || !Contains(activeControl) || (!HScroll && !VScroll)) return;

        var target = ScrollToControl(activeControl);
        SetDisplayRectLocation(target.X, target.Y);
    }

    /// <summary>The offset that brings the control into view with the least scrolling.</summary>
    protected virtual Point ScrollToControl(Control activeControl)
    {
        var client = ClientRectangle;
        var bounds = RectangleToClient(activeControl.RectangleToScreen(activeControl.ClientRectangle));
        var x = _displayRect.X;
        var y = _displayRect.Y;

        if (bounds.Right > client.Right) x -= bounds.Right - client.Right;
        if (bounds.Left - (x - _displayRect.X) < client.Left) x += client.Left - bounds.Left;
        if (bounds.Bottom > client.Bottom) y -= bounds.Bottom - client.Bottom;
        if (bounds.Top - (y - _displayRect.Y) < client.Top) y += client.Top - bounds.Top;

        return new Point(x, y);
    }

    /// <summary>The wheel scrolls three lines' worth a notch, when there is something to scroll.</summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (VScroll || HScroll)
        {
            var step = -e.Delta / 120 * SystemInformationLines * Math.Max(FontHeight, 1);

            if (VScroll) ScrollBy(ScrollEventType.ThumbPosition, vertical: true, -_displayRect.Y + step);
            else ScrollBy(ScrollEventType.ThumbPosition, vertical: false, -_displayRect.X + step);

            if (e is HandledMouseEventArgs handled) handled.Handled = true;
        }

        base.OnMouseWheel(e);
    }

    private const int SystemInformationLines = 3;

    /// <summary>Scrolls one bar to a position and raises Scroll, as the user's scrolling does.</summary>
    private void ScrollBy(ScrollEventType type, bool vertical, int position)
    {
        var properties = (ScrollProperties)(vertical ? VerticalScroll : HorizontalScroll);
        var oldValue = vertical ? -_displayRect.Y : -_displayRect.X;
        var maximum = Math.Max(properties.Maximum - properties.LargeChange + 1, 0);
        position = Math.Clamp(position, 0, maximum);

        if (vertical) SetDisplayRectLocation(_displayRect.X, -position);
        else SetDisplayRectLocation(-position, _displayRect.Y);

        SetScrollState(ScrollStateUserHasScrolled, true);
        OnScroll(new ScrollEventArgs(type, oldValue, position, vertical ? ScrollOrientation.VerticalScroll : ScrollOrientation.HorizontalScroll));
    }

    /// <summary>Keeps HorizontalScroll and VerticalScroll in step with the display rectangle.</summary>
    private void SyncScrollProperties()
    {
        var client = ClientSize;
        HorizontalScroll.Update(0, Math.Max(_displayRect.Width - 1, 0), Math.Max(client.Width, 1), -_displayRect.X, HScroll);
        VerticalScroll.Update(0, Math.Max(_displayRect.Height - 1, 0), Math.Max(client.Height, 1), -_displayRect.Y, VScroll);
    }

    internal void ScrollStateChanged()
    {
        if (AutoScroll) return;

        SetBars(HorizontalScroll.Visible, VerticalScroll.Visible);
        Invalidate();
    }

    internal void ScrollTo(ScrollProperties properties, int value)
    {
        if (properties.IsVertical) SetDisplayRectLocation(_displayRect.X, -value);
        else SetDisplayRectLocation(-value, _displayRect.Y);
    }

    internal override WindowScrollInfo? GetWindowScroll(bool vertical)
    {
        if (vertical ? !VScroll : !HScroll) return null;

        var properties = (ScrollProperties)(vertical ? VerticalScroll : HorizontalScroll);

        return new WindowScrollInfo(properties.Minimum, properties.Maximum, properties.LargeChange, properties.Value, Enabled && properties.Enabled);
    }

    internal override bool IsTrackingNonClient => _thumbDrag is not null;

    /// <summary>Arrows step a line, the track a page, the thumb is dragged: the window's own scroll bars.</summary>
    internal override bool NonClientMouseDown(MouseButtons button, Point outer)
    {
        if (button != MouseButtons.Left) return false;

        var parameters = CreateParams;

        foreach (var vertical in new[] { true, false })
        {
            var bar = vertical ? WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle) : WindowScrollBars.Horizontal(Size, parameters.Style, parameters.ExStyle);
            if (bar.IsEmpty || !bar.Contains(outer) || GetWindowScroll(vertical) is not { Enabled: true } info) continue;

            var orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
            var track = ScrollBarPainter.TrackBounds(orientation, bar);
            var thumb = info.ThumbBounds(track, vertical);
            var line = Math.Max(vertical ? FontHeight : 16, 1);

            if (ScrollBarPainter.LineUpButtonBounds(orientation, bar).Contains(outer))
            {
                ScrollBy(ScrollEventType.SmallDecrement, vertical, info.Position - line);
            }
            else if (ScrollBarPainter.LineDownButtonBounds(orientation, bar).Contains(outer))
            {
                ScrollBy(ScrollEventType.SmallIncrement, vertical, info.Position + line);
            }
            else if (thumb.Contains(outer))
            {
                _thumbDrag = (vertical, vertical ? outer.Y - thumb.Y : outer.X - thumb.X);
                return true;
            }
            else if (vertical ? outer.Y < thumb.Top : outer.X < thumb.Left)
            {
                ScrollBy(ScrollEventType.LargeDecrement, vertical, info.Position - info.Page);
            }
            else
            {
                ScrollBy(ScrollEventType.LargeIncrement, vertical, info.Position + info.Page);
            }

            OnScroll(new ScrollEventArgs(ScrollEventType.EndScroll, -(vertical ? _displayRect.Y : _displayRect.X), vertical ? ScrollOrientation.VerticalScroll : ScrollOrientation.HorizontalScroll));
            return false;
        }

        return false;
    }

    internal override void NonClientMouseMove(Point outer)
    {
        if (_thumbDrag is not { } drag || GetWindowScroll(drag.Vertical) is not { } info) return;

        var parameters = CreateParams;
        var bar = drag.Vertical ? WindowScrollBars.Vertical(Size, parameters.Style, parameters.ExStyle) : WindowScrollBars.Horizontal(Size, parameters.Style, parameters.ExStyle);
        var track = ScrollBarPainter.TrackBounds(drag.Vertical ? Orientation.Vertical : Orientation.Horizontal, bar);
        var thumb = info.ThumbBounds(track, drag.Vertical);
        var thumbLength = drag.Vertical ? thumb.Height : thumb.Width;
        var offset = (drag.Vertical ? outer.Y - track.Top : outer.X - track.Left) - drag.GrabOffset;

        ScrollBy(ScrollEventType.ThumbTrack, drag.Vertical, info.PositionFromThumb(offset, track, thumbLength, drag.Vertical));
        Invalidate();
    }

    internal override void NonClientMouseUp(Point outer)
    {
        if (_thumbDrag is not { } drag) return;

        _thumbDrag = null;
        var position = -(drag.Vertical ? _displayRect.Y : _displayRect.X);
        var orientation = drag.Vertical ? ScrollOrientation.VerticalScroll : ScrollOrientation.HorizontalScroll;
        OnScroll(new ScrollEventArgs(ScrollEventType.ThumbPosition, position, position, orientation));
        OnScroll(new ScrollEventArgs(ScrollEventType.EndScroll, position, orientation));
    }

    internal override void DefaultCaptureLost() => _thumbDrag = null;
}
