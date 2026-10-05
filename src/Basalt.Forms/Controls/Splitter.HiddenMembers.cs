using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace System.Windows.Forms;

public partial class Splitter
{
    public override bool AllowDrop { get => base.AllowDrop; set => base.AllowDrop = value; }
    public override AnchorStyles Anchor { get => base.Anchor; set => base.Anchor = value; }
    public override Image? BackgroundImage { get => base.BackgroundImage; set => base.BackgroundImage = value; }
    public override ImageLayout BackgroundImageLayout { get => base.BackgroundImageLayout; set => base.BackgroundImageLayout = value; }
    public override DockStyle Dock { get => base.Dock; set => base.Dock = value; }
    public override Font Font { get => base.Font; set => base.Font = value; }
    public override Color ForeColor { get => base.ForeColor; set => base.ForeColor = value; }
    public new ImeMode ImeMode { get => base.ImeMode; set => base.ImeMode = value; }
    public new bool TabStop { get => base.TabStop; set => base.TabStop = value; }
    [AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }

    public new event EventHandler? BackgroundImageChanged { add => base.BackgroundImageChanged += value; remove => base.BackgroundImageChanged -= value; }
    public new event EventHandler? BackgroundImageLayoutChanged { add => base.BackgroundImageLayoutChanged += value; remove => base.BackgroundImageLayoutChanged -= value; }
    public new event EventHandler? Enter { add => base.Enter += value; remove => base.Enter -= value; }
    public new event EventHandler? FontChanged { add => base.FontChanged += value; remove => base.FontChanged -= value; }
    public new event EventHandler? ForeColorChanged { add => base.ForeColorChanged += value; remove => base.ForeColorChanged -= value; }
    public new event EventHandler? ImeModeChanged { add => base.ImeModeChanged += value; remove => base.ImeModeChanged -= value; }
    public new event KeyEventHandler? KeyDown { add => base.KeyDown += value; remove => base.KeyDown -= value; }
    public new event KeyPressEventHandler? KeyPress { add => base.KeyPress += value; remove => base.KeyPress -= value; }
    public new event KeyEventHandler? KeyUp { add => base.KeyUp += value; remove => base.KeyUp -= value; }
    public new event EventHandler? Leave { add => base.Leave += value; remove => base.Leave -= value; }
    public new event EventHandler? TabStopChanged { add => base.TabStopChanged += value; remove => base.TabStopChanged -= value; }
    public new event EventHandler? TextChanged { add => base.TextChanged += value; remove => base.TextChanged -= value; }
}
