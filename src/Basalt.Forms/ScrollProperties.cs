using System.ComponentModel;

namespace System.Windows.Forms;

/// <summary>One of a scrollable control's scroll bars: its range, page, position and visibility.</summary>
/// <remarks>
/// Setting a value turns automatic scrolling off for the container, as in
/// WinForms: a program that drives the bars itself takes over from AutoScroll.
/// </remarks>
public abstract class ScrollProperties
{
    private readonly ScrollableControl? _parent;
    private int _minimum;
    private int _maximum = 100;
    private int _smallChange = 1;
    private int _largeChange = 10;
    private int _value;
    private bool _enabled = true;
    private bool _visible;

    protected ScrollProperties(ScrollableControl? container)
    {
        _parent = container;
    }

    protected ScrollableControl? ParentControl => _parent;

    internal abstract bool IsVertical { get; }

    [DefaultValue(true)]
    public bool Enabled
    {
        get => _enabled;
        set
        {
            TakeOverFromAutoScroll();
            _enabled = value;
            _parent?.ScrollStateChanged();
        }
    }

    [DefaultValue(0)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            TakeOverFromAutoScroll();
            _minimum = value;
            if (_maximum < value) _maximum = value;
            if (_value < value) Value = value;
            _parent?.ScrollStateChanged();
        }
    }

    [DefaultValue(100)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            TakeOverFromAutoScroll();
            _maximum = value;
            if (_minimum > value) _minimum = value;
            if (_value > value) Value = value;
            _parent?.ScrollStateChanged();
        }
    }

    [DefaultValue(1)]
    public int SmallChange
    {
        get => Math.Min(_smallChange, LargeChange);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _smallChange = value;
        }
    }

    [DefaultValue(10)]
    public int LargeChange
    {
        get => Math.Min(_largeChange, _maximum - _minimum + 1);
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);

            _largeChange = value;
            _parent?.ScrollStateChanged();
        }
    }

    /// <summary>The position; setting it scrolls the container.</summary>
    [Bindable(true)]
    [DefaultValue(0)]
    public int Value
    {
        get => _value;
        set
        {
            if (value < _minimum || value > _maximum) throw new ArgumentOutOfRangeException(nameof(value), value, $"Value of '{value}' is not valid for 'Value'. 'Value' should be between 'minimum' and 'maximum'.");

            _value = value;
            _parent?.ScrollTo(this, value);
        }
    }

    [DefaultValue(false)]
    public bool Visible
    {
        get => _visible;
        set
        {
            TakeOverFromAutoScroll();
            _visible = value;
            _parent?.ScrollStateChanged();
        }
    }

    /// <summary>The container keeps these in step with its scrolling without turning AutoScroll off.</summary>
    internal void Update(int minimum, int maximum, int largeChange, int value, bool visible)
    {
        _minimum = minimum;
        _maximum = maximum;
        _largeChange = largeChange;
        _value = Math.Clamp(value, minimum, Math.Max(maximum, minimum));
        _visible = visible;
    }

    private void TakeOverFromAutoScroll()
    {
        if (_parent is { AutoScroll: true } parent) parent.AutoScroll = false;
    }
}

public class HScrollProperties : ScrollProperties
{
    public HScrollProperties(ScrollableControl? container)
        : base(container)
    {
    }

    internal override bool IsVertical => false;
}

public class VScrollProperties : ScrollProperties
{
    public VScrollProperties(ScrollableControl? container)
        : base(container)
    {
    }

    internal override bool IsVertical => true;
}
