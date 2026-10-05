using System.Drawing;

namespace System.Windows.Forms;

public partial class ToolStripContainer
{
    public override bool AutoScroll { get => base.AutoScroll; set => base.AutoScroll = value; }
    public new Size AutoScrollMargin { get => base.AutoScrollMargin; set => base.AutoScrollMargin = value; }
    public new Size AutoScrollMinSize { get => base.AutoScrollMinSize; set => base.AutoScrollMinSize = value; }
    public new Color BackColor { get => base.BackColor; set => base.BackColor = value; }
    public new Image? BackgroundImage { get => base.BackgroundImage; set => base.BackgroundImage = value; }
    public override ImageLayout BackgroundImageLayout { get => base.BackgroundImageLayout; set => base.BackgroundImageLayout = value; }
    public new bool CausesValidation { get => base.CausesValidation; set => base.CausesValidation = value; }
    public new ContextMenuStrip? ContextMenuStrip { get => base.ContextMenuStrip; set => base.ContextMenuStrip = value; }
    public new ControlCollection Controls => base.Controls;
    public override Cursor Cursor { get => base.Cursor; set => base.Cursor = value; }
    public new Color ForeColor { get => base.ForeColor; set => base.ForeColor = value; }

    public new event EventHandler? BackColorChanged { add => base.BackColorChanged += value; remove => base.BackColorChanged -= value; }
    public new event EventHandler? BackgroundImageChanged { add => base.BackgroundImageChanged += value; remove => base.BackgroundImageChanged -= value; }
    public new event EventHandler? BackgroundImageLayoutChanged { add => base.BackgroundImageLayoutChanged += value; remove => base.BackgroundImageLayoutChanged -= value; }
    public new event EventHandler? CausesValidationChanged { add => base.CausesValidationChanged += value; remove => base.CausesValidationChanged -= value; }
    public new event EventHandler? ContextMenuStripChanged { add => base.ContextMenuStripChanged += value; remove => base.ContextMenuStripChanged -= value; }
    public new event EventHandler? CursorChanged { add => base.CursorChanged += value; remove => base.CursorChanged -= value; }
    public new event EventHandler? ForeColorChanged { add => base.ForeColorChanged += value; remove => base.ForeColorChanged -= value; }
}
