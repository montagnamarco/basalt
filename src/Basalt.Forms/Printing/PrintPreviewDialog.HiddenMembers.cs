using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The properties a print preview dialog inherits from Form and Control but
/// hides from the designer, because its shape (a toolbar over a preview) is
/// fixed: Microsoft re-declares each one here, forwarding to the base
/// member, purely to attach <see cref="EditorBrowsableAttribute"/>.
/// </summary>
public partial class PrintPreviewDialog
{
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new IButtonControl? AcceptButton
    {
        get => base.AcceptButton;
        set => base.AcceptButton = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string? AccessibleDescription
    {
        get => base.AccessibleDescription;
        set => base.AccessibleDescription = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string? AccessibleName
    {
        get => base.AccessibleName;
        set => base.AccessibleName = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new AccessibleRole AccessibleRole
    {
        get => base.AccessibleRole;
        set => base.AccessibleRole = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AllowDrop
    {
        get => base.AllowDrop;
        set => base.AllowDrop = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override AnchorStyles Anchor
    {
        get => base.Anchor;
        set => base.Anchor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AutoScroll
    {
        get => base.AutoScroll;
        set => base.AutoScroll = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size AutoScrollMargin
    {
        get => base.AutoScrollMargin;
        set => base.AutoScrollMargin = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size AutoScrollMinSize
    {
        get => base.AutoScrollMinSize;
        set => base.AutoScrollMinSize = value;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override AutoValidate AutoValidate
    {
        get => base.AutoValidate;
        set => base.AutoValidate = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new IButtonControl? CancelButton
    {
        get => base.CancelButton;
        set => base.CancelButton = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool CausesValidation
    {
        get => base.CausesValidation;
        set => base.CausesValidation = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ContextMenuStrip? ContextMenuStrip
    {
        get => base.ContextMenuStrip;
        set => base.ContextMenuStrip = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool ControlBox
    {
        get => base.ControlBox;
        set => base.ControlBox = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Cursor Cursor
    {
        get => base.Cursor;
        set => base.Cursor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ControlBindingsCollection DataBindings => base.DataBindings;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override DockStyle Dock
    {
        get => base.Dock;
        set => base.Dock = value;
    }
}
