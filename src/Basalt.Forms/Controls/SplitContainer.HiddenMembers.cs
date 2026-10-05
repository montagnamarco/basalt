using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace System.Windows.Forms;

public partial class SplitContainer
{
    /// <summary>
    /// Always false: Panel1 and Panel2 fill the client area that ArrangePanels
    /// lays out, so there is never content left over for a scroll bar to show.
    /// The getter is overridden (not just the setter) because
    /// ScrollableControl.OnLayout reads this virtual property rather than its
    /// own field; the setter still forwards to the base so its state stays
    /// consistent, but the base state alone can no longer turn a bar on.
    /// </summary>
    public override bool AutoScroll { get => false; set => base.AutoScroll = value; }
    public new Size AutoScrollMargin { get => base.AutoScrollMargin; set => base.AutoScrollMargin = value; }
    public new Size AutoScrollMinSize { get => base.AutoScrollMinSize; set => base.AutoScrollMinSize = value; }
    public override Point AutoScrollOffset { get => base.AutoScrollOffset; set => base.AutoScrollOffset = value; }
    public new Point AutoScrollPosition { get => base.AutoScrollPosition; set => base.AutoScrollPosition = value; }
    public override bool AutoSize { get => base.AutoSize; set => base.AutoSize = value; }
    public override Image? BackgroundImage { get => base.BackgroundImage; set => base.BackgroundImage = value; }
    public override ImageLayout BackgroundImageLayout { get => base.BackgroundImageLayout; set => base.BackgroundImageLayout = value; }
    public override BindingContext? BindingContext { get => base.BindingContext; set => base.BindingContext = value; }
    public new ControlCollection Controls => base.Controls;
    public new DockStyle Dock { get => base.Dock; set => base.Dock = value; }
    public new Padding Padding { get => base.Padding; set => base.Padding = value; }
    public new bool TabStop { get => base.TabStop; set => base.TabStop = value; }
    [AllowNull]
    public override string Text { get => base.Text; set => base.Text = value; }

    public new event EventHandler? AutoSizeChanged { add => base.AutoSizeChanged += value; remove => base.AutoSizeChanged -= value; }
    public new event EventHandler? BackgroundImageChanged { add => base.BackgroundImageChanged += value; remove => base.BackgroundImageChanged -= value; }
    public new event EventHandler? BackgroundImageLayoutChanged { add => base.BackgroundImageLayoutChanged += value; remove => base.BackgroundImageLayoutChanged -= value; }
    public new event ControlEventHandler? ControlAdded { add => base.ControlAdded += value; remove => base.ControlAdded -= value; }
    public new event ControlEventHandler? ControlRemoved { add => base.ControlRemoved += value; remove => base.ControlRemoved -= value; }
    public new event EventHandler? PaddingChanged { add => base.PaddingChanged += value; remove => base.PaddingChanged -= value; }
    public new event EventHandler? TextChanged { add => base.TextChanged += value; remove => base.TextChanged -= value; }
}
