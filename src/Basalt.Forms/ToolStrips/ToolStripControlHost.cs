using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// A <see cref="ToolStripItem"/> that hosts a real <see cref="Control"/>: the
/// control becomes a child of the owning strip, placed at the item's bounds
/// and kept there as the strip lays its items out again.
/// </summary>
public partial class ToolStripControlHost : ToolStripItem
{
    private readonly Control _control;

    public ToolStripControlHost(Control c)
        : this(c, string.Empty)
    {
    }

    public ToolStripControlHost(Control c, string name)
    {
        ArgumentNullException.ThrowIfNull(c);

        _control = c;
        Name = name;
        AutoSize = false;

        // The item's own default size wins over the control's native one (a ToolStripComboBox
        // is 100 wide, not a ComboBox's 121), and the control is sized to match.
        Size = DefaultSize;
        c.Size = Size;

        OnSubscribeControlEvents(c);
    }

    /// <summary>The control this item wraps; never null once constructed.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control Control => _control;

    [DefaultValue(ContentAlignment.MiddleCenter)]
    [Browsable(false)]
    public ContentAlignment ControlAlign { get; set; } = ContentAlignment.MiddleCenter;

    public override bool CanSelect => _control.CanSelect;

    /// <summary>
    /// Microsoft's ToolStripItem() constructor reads this virtual before any
    /// derived field (including <see cref="_control"/>) is assigned, so this
    /// (and every override of it) must tolerate a still-null control.
    /// </summary>
    protected override Size DefaultSize => _control is null ? new Size(23, 23) : _control.Size;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Always)]
    public virtual bool Focused => _control.Focused;

    public override bool Selected => _control.Focused;

    public override Size Size
    {
        get => base.Size;
        set => base.Size = value;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            OnUnsubscribeControlEvents(_control);
            _control.Dispose();
        }

        base.Dispose(disposing);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void Focus() => _control.Focus();

    public override Size GetPreferredSize(Size constrainingSize) => _control.GetPreferredSize(constrainingSize);

    protected override void OnBoundsChanged()
    {
        base.OnBoundsChanged();
        _control.Bounds = Bounds;
    }

    /// <summary>Called whenever the hosted control resizes itself; here, it keeps the item's own Size in step.</summary>
    protected virtual void OnHostedControlResize(EventArgs e)
    {
        if (_control.Size == Size) return;

        base.Size = _control.Size;
        Owner?.PerformLayout();
    }

    protected override void OnParentChanged(ToolStrip? oldParent, ToolStrip? newParent)
    {
        base.OnParentChanged(oldParent, newParent);

        oldParent?.Controls.Remove(_control);

        if (newParent is null) return;

        newParent.Controls.Add(_control);
        _control.Bounds = Bounds;
        _control.Visible = Available;
        _control.Enabled = Enabled;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // The hosted Control paints itself through the normal control pipeline; only the Paint event (if anyone subscribed) still fires here.
        base.OnPaint(e);
    }

    /// <summary>
    /// <see cref="ToolStripItem"/> does not route command/dialog keys or
    /// mnemonics through the item pipeline yet, so these mirror Microsoft's
    /// signatures without a base to override; the hosted Control already
    /// receives real keyboard input directly once it has the focus.
    /// </summary>
    protected internal override bool ProcessCmdKey(ref Message m, Keys keyData) => _control.PreProcessMessage(ref m);

    protected internal override bool ProcessDialogKey(Keys keyData) => false;

    protected internal override bool ProcessMnemonic(char charCode) => false;

    public override void ResetBackColor() => _control.ResetBackColor();

    public override void ResetForeColor() => _control.ResetForeColor();

    protected override void SetVisibleCore(bool visible)
    {
        base.SetVisibleCore(visible);
        _control.Visible = visible;
    }
}
