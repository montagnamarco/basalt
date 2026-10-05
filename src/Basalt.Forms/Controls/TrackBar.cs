using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

/// <summary>A slider: a thumb the user drags or steps with the keyboard along a channel of ticks.</summary>
[DefaultProperty("Value")]
[DefaultEvent("Scroll")]
[DefaultBindingProperty("Value")]
public partial class TrackBar : Control, ISupportInitialize
{
    private int _minimum;
    private int _maximum = 10;
    private int _value;
    private int _smallChange = 1;
    private int _largeChange = 5;
    private int _tickFrequency = 1;
    private TickStyle _tickStyle = TickStyle.BottomRight;
    private Orientation _orientation;
    private bool _dragging;

    /// <summary>The thumb's length along the axis it travels.</summary>
    private const int ThumbLength = 10;

    /// <summary>The strip reserved for tick marks on a side TickStyle asks for.</summary>
    private const int TickBand = 8;

    public TrackBar()
    {
        // TrackBar is native in WinForms: it paints and handles its mouse
        // through Control's default-procedure hooks, not OnPaint/OnMouseDown.
        SetStyle(ControlStyles.UserPaint, false);
        AutoSize = true;
    }

    protected override Size DefaultSize => _orientation == Orientation.Horizontal ? new Size(104, 45) : new Size(45, 104);

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set
        {
            if (value == base.AutoSize) return;

            base.AutoSize = value;
            AdjustSize();
        }
    }

    [DefaultValue(Orientation.Horizontal)]
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Orientation));
            if (_orientation == value) return;

            _orientation = value;
            AdjustSize();
            Invalidate();
        }
    }

    [DefaultValue(0)]
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

    [DefaultValue(10)]
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

    public void SetRange(int minValue, int maxValue)
    {
        if (maxValue < minValue) maxValue = minValue;

        Minimum = minValue;
        Maximum = maxValue;
    }

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

    [DefaultValue(5)]
    public int LargeChange
    {
        get => _largeChange;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _largeChange = value;
        }
    }

    [DefaultValue(1)]
    public int TickFrequency
    {
        get => _tickFrequency;
        set
        {
            if (_tickFrequency == value) return;

            _tickFrequency = value;
            Invalidate();
        }
    }

    [DefaultValue(TickStyle.BottomRight)]
    public TickStyle TickStyle
    {
        get => _tickStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(TickStyle));
            if (_tickStyle == value) return;

            _tickStyle = value;
            Invalidate();
        }
    }

    public event EventHandler? Scroll;

    protected virtual void OnScroll(EventArgs e) => Scroll?.Invoke(this, e);

    public event EventHandler? ValueChanged;

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    private void SetValueCore(int value)
    {
        if (_value == value) return;

        _value = value;
        Invalidate();
        OnValueChanged(EventArgs.Empty);
    }

    /// <summary>A keyboard or mouse gesture: the value changes (raising ValueChanged), then Scroll says the user did it.</summary>
    private void UserSetValue(int target)
    {
        target = Math.Clamp(target, _minimum, _maximum);
        SetValueCore(target);
        OnScroll(EventArgs.Empty);
    }

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    private void AdjustSize()
    {
        if (!AutoSize) return;

        var preferred = GetPreferredSize(Size.Empty);
        if (preferred != Size) Size = preferred;
    }

    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        return _orientation == Orientation.Horizontal ? new Size(Width, 45) : new Size(45, Height);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        return keyData switch
        {
            Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
            _ => base.IsInputKey(keyData),
        };
    }

    /// <summary>A key its KeyDown handlers left unhandled: the native track bar steps itself.</summary>
    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Up:
                UserSetValue(_value - _smallChange);
                e.Handled = true;
                break;

            case Keys.Right:
            case Keys.Down:
                UserSetValue(_value + _smallChange);
                e.Handled = true;
                break;

            case Keys.PageUp:
                UserSetValue(_value - _largeChange);
                e.Handled = true;
                break;

            case Keys.PageDown:
                UserSetValue(_value + _largeChange);
                e.Handled = true;
                break;

            case Keys.Home:
                UserSetValue(_minimum);
                e.Handled = true;
                break;

            case Keys.End:
                UserSetValue(_maximum);
                e.Handled = true;
                break;
        }
    }

    /// <summary>A mouse press on the native track bar, before OnMouseDown: it jumps the thumb to the click and starts dragging.</summary>
    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        _dragging = true;
        UserSetValue(ValueFromPoint(e.Location));
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (_dragging && Capture) UserSetValue(ValueFromPoint(e.Location));
    }

    internal override void DefaultMouseUp(MouseEventArgs e)
    {
        _dragging = false;
    }

    /// <summary>The area left for the channel and thumb, once TickStyle's bands are taken out of the client area.</summary>
    private Rectangle TrackArea()
    {
        var client = ClientRectangle;
        var leading = _tickStyle is TickStyle.TopLeft or TickStyle.Both ? TickBand : 0;
        var trailing = _tickStyle is TickStyle.BottomRight or TickStyle.Both ? TickBand : 0;

        return _orientation == Orientation.Horizontal
            ? Rectangle.FromLTRB(client.Left, client.Top + leading, client.Right, client.Bottom - trailing)
            : Rectangle.FromLTRB(client.Left + leading, client.Top, client.Right - trailing, client.Bottom);
    }

    /// <summary>The line the thumb's centre travels along, inset by half the thumb's length at each end.</summary>
    private Rectangle ChannelBounds()
    {
        var area = TrackArea();
        if (!UsePortableRoundThumb)
        {
            return _orientation == Orientation.Horizontal
                ? Rectangle.FromLTRB(area.Left + ThumbLength / 2, area.Top,
                    area.Right - ThumbLength / 2, area.Bottom)
                : Rectangle.FromLTRB(area.Left, area.Top + ThumbLength / 2,
                    area.Right, area.Bottom - ThumbLength / 2);
        }

        var diameter = RoundThumbDiameter(area);
        var horizontalInset = Math.Min(diameter / 2, Math.Max(area.Width / 2, 0));
        var verticalInset = Math.Min(diameter / 2, Math.Max(area.Height / 2, 0));

        return _orientation == Orientation.Horizontal
            ? Rectangle.FromLTRB(area.Left + horizontalInset, area.Top,
                area.Right - horizontalInset, area.Bottom)
            : Rectangle.FromLTRB(area.Left, area.Top + verticalInset,
                area.Right, area.Bottom - verticalInset);
    }

    private double FractionOf(int value)
    {
        var range = _maximum - _minimum;
        var fraction = range <= 0 ? 0 : (value - _minimum) / (double)range;

        // A vertical track bar reads like a volume slider: up is more, as Windows draws it.
        return _orientation == Orientation.Vertical ? 1 - fraction : fraction;
    }

    private Rectangle ThumbBounds()
    {
        var area = TrackArea();
        var channel = ChannelBounds();
        var fraction = FractionOf(_value);

        if (UsePortableRoundThumb)
        {
            var diameter = RoundThumbDiameter(area);
            if (diameter <= 0) return Rectangle.Empty;

            if (_orientation == Orientation.Horizontal)
            {
                var roundCenter = channel.Left + (int)Math.Round(fraction * channel.Width);
                return new Rectangle(roundCenter - diameter / 2,
                    area.Top + (area.Height - diameter) / 2, diameter, diameter);
            }

            var roundCenterY = channel.Top + (int)Math.Round(fraction * channel.Height);
            return new Rectangle(area.Left + (area.Width - diameter) / 2,
                roundCenterY - diameter / 2, diameter, diameter);
        }

        if (_orientation == Orientation.Horizontal)
        {
            var center = channel.Left + (int)Math.Round(fraction * channel.Width);
            return new Rectangle(center - ThumbLength / 2, area.Top, ThumbLength, area.Height);
        }

        var centerY = channel.Top + (int)Math.Round(fraction * channel.Height);
        return new Rectangle(area.Left, centerY - ThumbLength / 2, area.Width, ThumbLength);
    }

    private int ValueFromPoint(Point location)
    {
        var channel = ChannelBounds();
        var range = _maximum - _minimum;
        if (range <= 0) return _minimum;

        if (_orientation == Orientation.Horizontal)
        {
            if (channel.Width <= 0) return _value;

            var position = Math.Clamp(location.X - channel.Left, 0, channel.Width);
            return _minimum + (int)Math.Round(position / (double)channel.Width * range);
        }

        if (channel.Height <= 0) return _value;

        var positionY = Math.Clamp(location.Y - channel.Top, 0, channel.Height);
        var fraction = 1 - positionY / (double)channel.Height;

        return _minimum + (int)Math.Round(fraction * range);
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var area = TrackArea();
        var channel = ChannelBounds();

        DrawGroove(graphics, channel);
        DrawTicks(graphics, area, channel);
        DrawThumb(graphics, ThumbBounds());
    }

    private void DrawGroove(Graphics graphics, Rectangle channel)
    {
        var groove = _orientation == Orientation.Horizontal
            ? new Rectangle(channel.Left, channel.Top + channel.Height / 2 - 2, channel.Width, 4)
            : new Rectangle(channel.Left + channel.Width / 2 - 2, channel.Top, 4, channel.Height);

        if (PortableVisualStylesEnabled)
        {
            PaintPortableTrack(graphics, groove);
        }
        else
        {
            ControlPaint.DrawBorder3D(graphics, groove, Border3DStyle.Sunken);
        }
    }

    private void DrawTicks(Graphics graphics, Rectangle area, Rectangle channel)
    {
        if (_tickStyle == TickStyle.None || _tickFrequency <= 0) return;

        var range = _maximum - _minimum;
        if (range <= 0) return;

        using var pen = new Pen(PortableVisualStylesEnabled
            ? Internal.Rendering.PortableVisualStyles.Palette(PortableVisualStylePlatform).Border : SystemColors.ControlDark);

        for (var tickValue = _minimum; tickValue <= _maximum; tickValue += _tickFrequency)
        {
            var fraction = FractionOf(tickValue);

            if (_orientation == Orientation.Horizontal)
            {
                var x = channel.Left + (int)Math.Round(fraction * channel.Width);

                if (_tickStyle is TickStyle.TopLeft or TickStyle.Both) graphics.DrawLine(pen, x, area.Top, x, area.Top - TickBand / 2);
                if (_tickStyle is TickStyle.BottomRight or TickStyle.Both) graphics.DrawLine(pen, x, area.Bottom, x, area.Bottom + TickBand / 2);
            }
            else
            {
                var y = channel.Top + (int)Math.Round(fraction * channel.Height);

                if (_tickStyle is TickStyle.TopLeft or TickStyle.Both) graphics.DrawLine(pen, area.Left, y, area.Left - TickBand / 2, y);
                if (_tickStyle is TickStyle.BottomRight or TickStyle.Both) graphics.DrawLine(pen, area.Right, y, area.Right + TickBand / 2, y);
            }
        }
    }

    private void DrawThumb(Graphics graphics, Rectangle thumb)
    {
        if (PortableVisualStylesEnabled)
        {
            PaintPortableThumb(graphics, thumb);
        }
        else
        {
            ControlPaint.DrawButton(graphics, thumb, Enabled ? ButtonState.Normal : ButtonState.Inactive);
        }
    }

    public override string ToString() => $"{base.ToString()}, Minimum: {Minimum}, Maximum: {Maximum}, Value: {Value}";
}
