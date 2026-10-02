using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

/// <summary>A sunken bar that fills with SystemColors.Highlight to show how much of a task is done.</summary>
[DefaultProperty("Value")]
[DefaultBindingProperty("Value")]
public partial class ProgressBar : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private int _step = 10;
    private ProgressBarStyle _style = ProgressBarStyle.Blocks;
    private int _marqueeAnimationSpeed = 100;
    private Timer? _marqueeTimer;
    private int _marqueeOffset;

    /// <summary>The width of one segment of the classic Blocks style, and the pixel gap after it.</summary>
    private const int BlockWidth = 8;
    private const int BlockGap = 2;

    /// <summary>The sliding block of the Marquee style is this fraction of the bar's width.</summary>
    private const int MarqueeBlockFraction = 3;

    /// <summary>How far the marquee block moves on each tick.</summary>
    private const int MarqueeStep = 4;

    public ProgressBar()
    {
        // ProgressBar is native in WinForms: it paints through Control's
        // default-procedure hook, not OnPaint, and takes no input of its own.
        SetStyle(ControlStyles.UserPaint, false);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    protected override Size DefaultSize => new(100, 23);

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
        }
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

    [DefaultValue(10)]
    public int Step
    {
        get => _step;
        set => _step = value;
    }

    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    [DefaultValue(ProgressBarStyle.Blocks)]
    public ProgressBarStyle Style
    {
        get => _style;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ProgressBarStyle));
            if (_style == value) return;

            _style = value;
            UpdateMarqueeTimer();
            Invalidate();
        }
    }

    [DefaultValue(100)]
    public int MarqueeAnimationSpeed
    {
        get => _marqueeAnimationSpeed;
        set
        {
            if (_marqueeAnimationSpeed == value) return;

            _marqueeAnimationSpeed = value;
            UpdateMarqueeTimer();
        }
    }

    /// <summary>Advances Value by Step, never past Maximum.</summary>
    public void PerformStep() => Increment(_step);

    /// <summary>Advances Value by an arbitrary amount, clamped to stay within Minimum and Maximum.</summary>
    public void Increment(int value)
    {
        var target = Math.Clamp(_value + value, _minimum, _maximum);
        SetValueCore(target);
    }

    private void SetValueCore(int value)
    {
        if (_value == value) return;

        _value = value;
        if (_style != ProgressBarStyle.Marquee) Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateMarqueeTimer();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        _marqueeTimer?.Dispose();
        _marqueeTimer = null;
        base.OnHandleDestroyed(e);
    }

    /// <summary>Starts or stops the sliding block: it animates only while the bar is a created Marquee.</summary>
    private void UpdateMarqueeTimer()
    {
        var shouldAnimate = _style == ProgressBarStyle.Marquee && IsHandleCreated && _marqueeAnimationSpeed > 0;

        if (shouldAnimate)
        {
            _marqueeTimer ??= CreateMarqueeTimer();
            _marqueeTimer.Interval = _marqueeAnimationSpeed;
            _marqueeTimer.Start();
        }
        else
        {
            _marqueeTimer?.Stop();
        }
    }

    private Timer CreateMarqueeTimer()
    {
        var timer = new Timer();
        timer.Tick += (_, _) =>
        {
            _marqueeOffset += MarqueeStep;
            Invalidate();
        };

        return timer;
    }

    /// <summary>How far through the range Value sits, 0 to 1.</summary>
    private double Fraction()
    {
        var range = _maximum - _minimum;
        if (range <= 0) return 0;

        return Math.Clamp((_value - _minimum) / (double)range, 0, 1);
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var bounds = ClientRectangle;

        using (var face = new SolidBrush(SystemColors.Control))
        {
            graphics.FillRectangle(face, bounds);
        }

        ControlPaint.DrawBorder3D(graphics, bounds, Border3DStyle.Sunken);

        var interior = Rectangle.Inflate(bounds, -2, -2);

        switch (_style)
        {
            case ProgressBarStyle.Marquee:
                PaintMarquee(graphics, interior);
                break;

            case ProgressBarStyle.Continuous:
                PaintContinuous(graphics, interior);
                break;

            default:
                PaintBlocks(graphics, interior);
                break;
        }
    }

    private void PaintContinuous(Graphics graphics, Rectangle interior)
    {
        var width = (int)(interior.Width * Fraction());
        if (width <= 0) return;

        using var brush = new SolidBrush(SystemColors.Highlight);
        graphics.FillRectangle(brush, interior.Left, interior.Top, width, interior.Height);
    }

    private void PaintBlocks(Graphics graphics, Rectangle interior)
    {
        var filledWidth = (int)(interior.Width * Fraction());
        if (filledWidth <= 0) return;

        using var brush = new SolidBrush(SystemColors.Highlight);

        var x = interior.Left;
        while (x < interior.Left + filledWidth && x < interior.Right)
        {
            var blockWidth = Math.Min(BlockWidth, interior.Right - x);
            graphics.FillRectangle(brush, x, interior.Top, blockWidth, interior.Height);
            x += BlockWidth + BlockGap;
        }
    }

    private void PaintMarquee(Graphics graphics, Rectangle interior)
    {
        if (interior.Width <= 0 || interior.Height <= 0) return;

        var blockWidth = Math.Max(interior.Width / MarqueeBlockFraction, 1);
        var travel = interior.Width + blockWidth;
        var x = interior.Left - blockWidth + _marqueeOffset % travel;

        using var brush = new SolidBrush(SystemColors.Highlight);
        var clipped = Rectangle.Intersect(interior, new Rectangle(x, interior.Top, blockWidth, interior.Height));
        if (!clipped.IsEmpty) graphics.FillRectangle(brush, clipped);
    }

    public override string ToString() => $"{base.ToString()}, Minimum: {Minimum}, Maximum: {Maximum}, Value: {Value}";
}
