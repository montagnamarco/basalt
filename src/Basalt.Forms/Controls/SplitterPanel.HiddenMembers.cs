using System.Drawing;

namespace System.Windows.Forms;

public sealed partial class SplitterPanel
{
    public new AnchorStyles Anchor { get => base.Anchor; set => base.Anchor = value; }
    public new bool AutoSize { get => base.AutoSize; set => base.AutoSize = value; }
    public override AutoSizeMode AutoSizeMode { get => base.AutoSizeMode; set => base.AutoSizeMode = value; }
    public new BorderStyle BorderStyle { get => base.BorderStyle; set => base.BorderStyle = value; }
    public new DockStyle Dock { get => base.Dock; set => base.Dock = value; }
    public new ScrollableControl.DockPaddingEdges DockPadding => base.DockPadding;
    public new int Height { get => base.Height; set => base.Height = value; }
    public new Point Location { get => base.Location; set => base.Location = value; }
    public new Size MaximumSize { get => base.MaximumSize; set => base.MaximumSize = value; }
    public new Size MinimumSize { get => base.MinimumSize; set => base.MinimumSize = value; }
    public new string Name { get => base.Name; set => base.Name = value; }
    public new Control? Parent { get => base.Parent; set => base.Parent = value; }
    public new Size Size { get => base.Size; set => base.Size = value; }
    public new int TabIndex { get => base.TabIndex; set => base.TabIndex = value; }
    public new bool TabStop { get => base.TabStop; set => base.TabStop = value; }
    public new bool Visible { get => base.Visible; set => base.Visible = value; }
    public new int Width { get => base.Width; set => base.Width = value; }

    public new event EventHandler? AutoSizeChanged { add => base.AutoSizeChanged += value; remove => base.AutoSizeChanged -= value; }
    public new event EventHandler? DockChanged { add => base.DockChanged += value; remove => base.DockChanged -= value; }
    public new event EventHandler? LocationChanged { add => base.LocationChanged += value; remove => base.LocationChanged -= value; }
    public new event EventHandler? TabIndexChanged { add => base.TabIndexChanged += value; remove => base.TabIndexChanged -= value; }
    public new event EventHandler? TabStopChanged { add => base.TabStopChanged += value; remove => base.TabStopChanged -= value; }
    public new event EventHandler? VisibleChanged { add => base.VisibleChanged += value; remove => base.VisibleChanged -= value; }
}
