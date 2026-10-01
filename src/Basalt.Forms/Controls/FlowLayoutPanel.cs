using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>A panel that flows its children one after another, wrapping into a new row or column.</summary>
[DefaultProperty("FlowDirection")]
public class FlowLayoutPanel : Panel, IExtenderProvider
{
    private FlowLayoutSettings? _settings;

    /// <summary>The settings this panel shares with its <see cref="FlowLayout"/> engine.</summary>
    internal FlowLayoutSettings FlowLayoutSettings => _settings ??= new FlowLayoutSettings(this);

    [DefaultValue(FlowDirection.LeftToRight)]
    public FlowDirection FlowDirection
    {
        get => FlowLayoutSettings.FlowDirection;
        set
        {
            if (value == FlowDirection) return;

            FlowLayoutSettings.FlowDirection = value;
            DoLayout(this, "FlowDirection");
        }
    }

    public override LayoutEngine LayoutEngine => FlowLayout.Instance;

    [DefaultValue(true)]
    public bool WrapContents
    {
        get => FlowLayoutSettings.WrapContents;
        set
        {
            if (value == WrapContents) return;

            FlowLayoutSettings.WrapContents = value;
            DoLayout(this, "WrapContents");
        }
    }

    [DefaultValue(false)]
    [DisplayName("FlowBreak")]
    public bool GetFlowBreak(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);

        return FlowLayoutSettings.GetFlowBreak(control);
    }

    [DisplayName("FlowBreak")]
    public void SetFlowBreak(Control control, bool value)
    {
        ArgumentNullException.ThrowIfNull(control);

        if (value == FlowLayoutSettings.GetFlowBreak(control)) return;

        FlowLayoutSettings.SetFlowBreak(control, value);
        DoLayout(control, "FlowBreak");
    }

    /// <summary>The size needed to flow every child without clipping any of them.</summary>
    internal override Size GetPreferredSizeCore(Size proposedSize) => FlowLayout.PreferredSize(this, proposedSize);

    /// <summary>Lets the designer offer the FlowBreak extender property on this panel's own children.</summary>
    bool IExtenderProvider.CanExtend(object extendee) => extendee is Control control && control.Parent == this;
}
