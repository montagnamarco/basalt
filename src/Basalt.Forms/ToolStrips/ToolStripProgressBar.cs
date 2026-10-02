using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A <see cref="ProgressBar"/> hosted on a <see cref="ToolStrip"/>.</summary>
[DefaultProperty("Value")]
public class ToolStripProgressBar : ToolStripControlHost
{
    private bool _rightToLeftLayout;

    public ToolStripProgressBar()
        : base(new ProgressBar())
    {
    }

    public ToolStripProgressBar(string? name)
        : this()
    {
        Name = name;
    }

    protected override Padding DefaultMargin => new(1, 0, 1, 0);

    protected override Size DefaultSize => new(100, 15);

    [DefaultValue(100)]
    public int MarqueeAnimationSpeed
    {
        get => ProgressBar.MarqueeAnimationSpeed;
        set => ProgressBar.MarqueeAnimationSpeed = value;
    }

    [DefaultValue(100)]
    public int Maximum
    {
        get => ProgressBar.Maximum;
        set => ProgressBar.Maximum = value;
    }

    [DefaultValue(0)]
    public int Minimum
    {
        get => ProgressBar.Minimum;
        set => ProgressBar.Minimum = value;
    }

    /// <summary>The hosted bar, for members this wrapper does not repeat.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ProgressBar ProgressBar => (ProgressBar)Control;

    [DefaultValue(false)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (value == _rightToLeftLayout) return;

            _rightToLeftLayout = value;
            OnRightToLeftLayoutChanged(EventArgs.Empty);
        }
    }

    [DefaultValue(10)]
    public int Step
    {
        get => ProgressBar.Step;
        set => ProgressBar.Step = value;
    }

    [DefaultValue(ProgressBarStyle.Blocks)]
    public ProgressBarStyle Style
    {
        get => ProgressBar.Style;
        set => ProgressBar.Style = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public override string? Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    [DefaultValue(0)]
    [Bindable(true)]
    public int Value
    {
        get => ProgressBar.Value;
        set => ProgressBar.Value = value;
    }

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_rightToLeftLayoutEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftLayoutEvent, value);
    }

    private static readonly object s_rightToLeftLayoutEvent = new();

    // --- A progress bar never takes keyboard focus, so the keyboard and validation events a plain
    // control host wires up do not apply to it: each is hidden from the designer, forwarding to the
    // base's own storage, exactly as Printing/PrintPreviewDialog.HiddenEvents.cs does. ---

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyDown
    {
        add => base.KeyDown += value;
        remove => base.KeyDown -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyPressEventHandler? KeyPress
    {
        add => base.KeyPress += value;
        remove => base.KeyPress -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyUp
    {
        add => base.KeyUp += value;
        remove => base.KeyUp -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? LocationChanged
    {
        add => base.LocationChanged += value;
        remove => base.LocationChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? OwnerChanged
    {
        add => base.OwnerChanged += value;
        remove => base.OwnerChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? Validated
    {
        add => base.Validated += value;
        remove => base.Validated -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event CancelEventHandler? Validating
    {
        add => base.Validating += value;
        remove => base.Validating -= value;
    }

    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    public void Increment(int value) => ProgressBar.Increment(value);

    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutEvent])?.Invoke(this, e);

    /// <summary>Only Resize matters for a non-focusable control: the keyboard and validation wiring a plain host does is skipped.</summary>
    protected override void OnSubscribeControlEvents(Control? control)
    {
        if (control is null) return;

        control.Resize += HandleProgressBarResize;
    }

    protected override void OnUnsubscribeControlEvents(Control? control)
    {
        if (control is null) return;

        control.Resize -= HandleProgressBarResize;
    }

    private void HandleProgressBarResize(object? sender, EventArgs e) => OnHostedControlResize(e);

    public void PerformStep() => ProgressBar.PerformStep();
}
