using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal.Skins;

namespace System.Windows.Forms;

/// <summary>
/// The base of <see cref="HScrollBar"/> and <see cref="VScrollBar"/>: a
/// range between Minimum and Maximum, a Value the thumb tracks, and the
/// classic look of two arrow buttons around a dithered track.
/// </summary>
/// <remarks>
/// The thumb never reaches the true Maximum: it represents a page
/// (LargeChange) of a longer document, so the farthest it can travel leaves
/// Value at Maximum - LargeChange + 1, exactly as Windows draws it. Only an
/// explicit assignment to <see cref="Value"/> can go all the way to Maximum.
/// </remarks>
[DefaultProperty("Value")]
[DefaultEvent("Scroll")]
public abstract partial class ScrollBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _smallChange = 1;
    private int _largeChange = 10;
    private int _value;

    private ScrollBarPainter.PressedPart _pressedPart = ScrollBarPainter.PressedPart.None;
    private bool _dragging;
    private int _dragAnchor;
    private Timer? _repeatTimer;
    private ScrollEventType _repeatType;
    private int _repeatDelta;

    private const int InitialRepeatDelay = 300;
    private const int RepeatInterval = 50;

    public ScrollBar()
    {
        // ScrollBar is native in WinForms: it paints and handles its mouse and
        // keys through Control's default-procedure hooks, not OnPaint/OnMouseDown/OnKeyDown.
        SetStyle(ControlStyles.UserPaint, false);
        TabStop = false;
    }

    /// <summary>Which axis this bar scrolls along; fixed by HScrollBar and VScrollBar.</summary>
    private protected abstract Orientation Direction { get; }

    [DefaultValue(0)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value) return;

            _minimum = value;
            if (_maximum < _minimum) _maximum = _minimum;

            if (_value < _minimum) SetValueCore(_minimum);
            else if (_value > _maximum) SetValueCore(_maximum);

            Invalidate();
        }
    }

    [DefaultValue(100)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value) return;

            _maximum = value;
            if (_minimum > _maximum) _minimum = _maximum;

            if (_value > _maximum) SetValueCore(_maximum);

            Invalidate();
        }
    }

    [DefaultValue(1)]
    public int SmallChange
    {
        get => _smallChange;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _smallChange = value;
        }
    }

    [DefaultValue(10)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public int LargeChange
    {
        get => _largeChange;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            if (_largeChange == value) return;

            _largeChange = value;
            Invalidate();
        }
    }

    /// <summary>The value the thumb's user interaction can reach: Maximum minus a page, plus one.</summary>
    private int ReachableMaximum() => Math.Max(_minimum, _maximum - Math.Max(_largeChange, 1) + 1);

    [DefaultValue(0)]
    [Bindable(true)]
    public int Value
    {
        get => _value;
        set
        {
            if (value < _minimum || value > _maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"'{value}' is not a valid value for 'Value'. 'Value' should be between 'Minimum' and 'Maximum'.");
            }

            SetValueCore(value);
        }
    }

    private void SetValueCore(int value)
    {
        if (_value == value) return;

        _value = value;
        Invalidate();
        OnValueChanged(EventArgs.Empty);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    /// <summary>Answers WM_GETDLGCODE: a scroll bar wants the keys that step and page it.</summary>
    internal override bool DialogCodeWantsKey(Keys keyData)
    {
        return keyData switch
        {
            Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
            _ => base.DialogCodeWantsKey(keyData),
        };
    }

    /// <summary>A key its KeyDown handlers left unhandled: the native scroll bar steps itself.</summary>
    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Up:
            case Keys.Left:
                PerformSingleStep(ScrollEventType.SmallDecrement, -_smallChange);
                e.Handled = true;
                break;

            case Keys.Down:
            case Keys.Right:
                PerformSingleStep(ScrollEventType.SmallIncrement, _smallChange);
                e.Handled = true;
                break;

            case Keys.PageUp:
                PerformSingleStep(ScrollEventType.LargeDecrement, -_largeChange);
                e.Handled = true;
                break;

            case Keys.PageDown:
                PerformSingleStep(ScrollEventType.LargeIncrement, _largeChange);
                e.Handled = true;
                break;

            case Keys.Home:
                JumpTo(ScrollEventType.First, _minimum);
                e.Handled = true;
                break;

            case Keys.End:
                JumpTo(ScrollEventType.Last, ReachableMaximum());
                e.Handled = true;
                break;
        }
    }

    /// <summary>A mouse press on the native scroll bar, before OnMouseDown: it hit-tests its own parts.</summary>
    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        var bounds = ClientRectangle;
        var upButton = ScrollBarPainter.LineUpButtonBounds(Direction, bounds);
        var downButton = ScrollBarPainter.LineDownButtonBounds(Direction, bounds);
        var thumb = ThumbBounds();

        if (upButton.Contains(e.Location))
        {
            BeginRepeat(ScrollBarPainter.PressedPart.LineUpButton, ScrollEventType.SmallDecrement, -_smallChange);
        }
        else if (downButton.Contains(e.Location))
        {
            BeginRepeat(ScrollBarPainter.PressedPart.LineDownButton, ScrollEventType.SmallIncrement, _smallChange);
        }
        else if (!thumb.IsEmpty && thumb.Contains(e.Location))
        {
            _dragging = true;
            _pressedPart = ScrollBarPainter.PressedPart.Thumb;
            _dragAnchor = Direction == Orientation.Horizontal ? e.Location.X - thumb.Left : e.Location.Y - thumb.Top;
            Invalidate();
        }
        else
        {
            var track = ScrollBarPainter.TrackBounds(Direction, bounds);

            int reference;
            if (!thumb.IsEmpty) reference = Direction == Orientation.Horizontal ? thumb.Left : thumb.Top;
            else reference = Direction == Orientation.Horizontal ? track.Left + track.Width / 2 : track.Top + track.Height / 2;

            var clickPosition = Direction == Orientation.Horizontal ? e.Location.X : e.Location.Y;

            if (clickPosition < reference) PerformSingleStep(ScrollEventType.LargeDecrement, -_largeChange);
            else PerformSingleStep(ScrollEventType.LargeIncrement, _largeChange);
        }
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_dragging || !Capture) return;

        var track = ScrollBarPainter.TrackBounds(Direction, ClientRectangle);
        var trackLength = Direction == Orientation.Horizontal ? track.Width : track.Height;
        var thumbLength = ThumbLength(trackLength);
        var travel = Math.Max(trackLength - thumbLength, 0);

        var mouseOffset = Direction == Orientation.Horizontal
            ? e.Location.X - track.Left - _dragAnchor
            : e.Location.Y - track.Top - _dragAnchor;
        mouseOffset = Math.Clamp(mouseOffset, 0, travel);

        var reachable = ReachableMaximum();
        var span = Math.Max(reachable - _minimum, 0);
        var target = span == 0 || travel == 0
            ? _minimum
            : _minimum + (int)Math.Round(mouseOffset * (double)span / travel);

        ApplyScroll(ScrollEventType.ThumbTrack, _value, Math.Clamp(target, _minimum, reachable));
    }

    internal override void DefaultMouseUp(MouseEventArgs e)
    {
        if (_repeatTimer is not null)
        {
            StopRepeat();
            RaiseEndScroll();
        }
        else if (_dragging)
        {
            _dragging = false;
            ApplyScroll(ScrollEventType.ThumbPosition, _value, _value);
            RaiseEndScroll();
        }

        if (_pressedPart != ScrollBarPainter.PressedPart.None)
        {
            _pressedPart = ScrollBarPainter.PressedPart.None;
            Invalidate();
        }
    }

    private void BeginRepeat(ScrollBarPainter.PressedPart part, ScrollEventType type, int delta)
    {
        _pressedPart = part;
        Invalidate();

        PerformScrollStep(type, delta);

        _repeatType = type;
        _repeatDelta = delta;
        _repeatTimer ??= new Timer();
        _repeatTimer.Tick -= OnRepeatTick;
        _repeatTimer.Tick += OnRepeatTick;
        _repeatTimer.Interval = InitialRepeatDelay;
        _repeatTimer.Start();
    }

    private void OnRepeatTick(object? sender, EventArgs e)
    {
        if (_repeatTimer is null) return;
        if (_repeatTimer.Interval != RepeatInterval) _repeatTimer.Interval = RepeatInterval;

        PerformScrollStep(_repeatType, _repeatDelta);
    }

    /// <summary>Capture taken away mid-press: the repeat stops and the drag ends where it is.</summary>
    internal override void DefaultCaptureLost()
    {
        StopRepeat();
        _dragging = false;
        Invalidate();
    }

    /// <summary>A scroll bar destroyed while an arrow is held must not keep its repeat timer ticking.</summary>
    internal override void OnHandleDestroyedInternal()
    {
        StopRepeat();
        _dragging = false;
        base.OnHandleDestroyedInternal();
    }

    private void StopRepeat()
    {
        if (_repeatTimer is null) return;

        _repeatTimer.Stop();
        _repeatTimer.Tick -= OnRepeatTick;
        _repeatTimer.Dispose();
        _repeatTimer = null;
    }

    private void PerformScrollStep(ScrollEventType type, int delta)
    {
        var reachable = ReachableMaximum();
        var proposed = Math.Clamp(_value + delta, _minimum, reachable);

        ApplyScroll(type, _value, proposed);
    }

    private void PerformSingleStep(ScrollEventType type, int delta)
    {
        PerformScrollStep(type, delta);
        RaiseEndScroll();
    }

    private void JumpTo(ScrollEventType type, int target)
    {
        ApplyScroll(type, _value, target);
        RaiseEndScroll();
    }

    /// <summary>Raises Scroll, then applies whatever NewValue the handler left, as WinForms lets it adjust the destination.</summary>
    private void ApplyScroll(ScrollEventType type, int oldValue, int proposedValue)
    {
        var orientation = Direction == Orientation.Horizontal ? ScrollOrientation.HorizontalScroll : ScrollOrientation.VerticalScroll;
        var args = new ScrollEventArgs(type, oldValue, proposedValue, orientation);
        OnScroll(args);

        var finalValue = Math.Clamp(args.NewValue, _minimum, _maximum);
        if (finalValue != _value) Value = finalValue;
    }

    private void RaiseEndScroll()
    {
        var orientation = Direction == Orientation.Horizontal ? ScrollOrientation.HorizontalScroll : ScrollOrientation.VerticalScroll;
        OnScroll(new ScrollEventArgs(ScrollEventType.EndScroll, _value, _value, orientation));
    }

    public event ScrollEventHandler? Scroll;

    protected virtual void OnScroll(ScrollEventArgs se) => Scroll?.Invoke(this, se);

    public event EventHandler? ValueChanged;

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    /// <summary>The length a thumb takes along the track, proportional to LargeChange, never below a usable minimum.</summary>
    private int ThumbLength(int trackLength)
    {
        if (trackLength <= 0) return 0;

        var range = Math.Max(_maximum - _minimum, 0);
        var largeChange = Math.Max(Math.Min(Math.Max(_largeChange, 0), range + 1), 1);

        var length = range == 0 ? trackLength : trackLength * largeChange / (range + 1);

        return Math.Clamp(length, Math.Min(ScrollBarPainter.MinimumThumbLength, trackLength), trackLength);
    }

    private Rectangle ThumbBounds()
    {
        var track = ScrollBarPainter.TrackBounds(Direction, ClientRectangle);
        var trackLength = Direction == Orientation.Horizontal ? track.Width : track.Height;
        if (trackLength <= 0) return Rectangle.Empty;

        var thumbLength = ThumbLength(trackLength);

        var reachable = ReachableMaximum();
        var span = Math.Max(reachable - _minimum, 0);
        var offset = span == 0 ? 0 : (_value - _minimum) * (trackLength - thumbLength) / span;

        return Direction == Orientation.Horizontal
            ? new Rectangle(track.Left + offset, track.Top, thumbLength, track.Height)
            : new Rectangle(track.Left, track.Top + offset, track.Width, thumbLength);
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        ScrollBarPainter.Draw(e.Graphics, Direction, ClientRectangle, ThumbBounds(), _pressedPart, Enabled);
    }

    public override string ToString() => $"{base.ToString()}, Minimum: {Minimum}, Maximum: {Maximum}, Value: {Value}";
}
