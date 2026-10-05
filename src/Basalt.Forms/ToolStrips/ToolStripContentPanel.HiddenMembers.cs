using System.Drawing;

namespace System.Windows.Forms;

public partial class ToolStripContentPanel
{
    public override AnchorStyles Anchor { get => base.Anchor; set => base.Anchor = value; }
    public override bool AutoScroll { get => base.AutoScroll; set => base.AutoScroll = value; }
    public new Size AutoScrollMargin { get => base.AutoScrollMargin; set => base.AutoScrollMargin = value; }
    public new Size AutoScrollMinSize { get => base.AutoScrollMinSize; set => base.AutoScrollMinSize = value; }
    public override bool AutoSize { get => base.AutoSize; set => base.AutoSize = value; }
    public override AutoSizeMode AutoSizeMode { get => base.AutoSizeMode; set => base.AutoSizeMode = value; }
    public override Color BackColor { get => base.BackColor; set => base.BackColor = value; }
    public new bool CausesValidation { get => base.CausesValidation; set => base.CausesValidation = value; }
    public override DockStyle Dock { get => base.Dock; set => base.Dock = value; }
    public new Point Location { get => base.Location; set => base.Location = value; }
    public override Size MaximumSize { get => base.MaximumSize; set => base.MaximumSize = value; }
    public override Size MinimumSize { get => base.MinimumSize; set => base.MinimumSize = value; }
    public new string Name { get => base.Name; set => base.Name = value; }
    public new int TabIndex { get => base.TabIndex; set => base.TabIndex = value; }
    public new bool TabStop { get => base.TabStop; set => base.TabStop = value; }

    public new event EventHandler? AutoSizeChanged { add => base.AutoSizeChanged += value; remove => base.AutoSizeChanged -= value; }
    public new event EventHandler? CausesValidationChanged { add => base.CausesValidationChanged += value; remove => base.CausesValidationChanged -= value; }
    public new event EventHandler? DockChanged { add => base.DockChanged += value; remove => base.DockChanged -= value; }
    public new event EventHandler? LocationChanged { add => base.LocationChanged += value; remove => base.LocationChanged -= value; }
    public new event EventHandler? TabIndexChanged { add => base.TabIndexChanged += value; remove => base.TabIndexChanged -= value; }
    public new event EventHandler? TabStopChanged { add => base.TabStopChanged += value; remove => base.TabStopChanged -= value; }
}
