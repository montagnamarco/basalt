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

    public void Increment(int value) => ProgressBar.Increment(value);

    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutEvent])?.Invoke(this, e);

    public void PerformStep() => ProgressBar.PerformStep();
}
