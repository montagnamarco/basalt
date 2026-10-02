using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Properties <see cref="ToolStripItem"/> defines for painting its own image
/// and text that a control host has no use for (the hosted <see cref="Control"/>
/// paints the whole item): each is re-declared only to hide it from the
/// designer and forward to the base's storage, the pattern
/// <c>Printing/PrintPreviewDialog.HiddenEvents.cs</c> follows for the same
/// reason. <see cref="RightToLeft"/>, <see cref="BackgroundImage"/> and
/// <see cref="TextDirection"/> stay virtual and real: they forward to the
/// hosted control instead of the item's own storage.
/// </summary>
public partial class ToolStripControlHost
{
    public override Image? BackgroundImage
    {
        get => Control.BackgroundImage;
        set
        {
            if (value == Control.BackgroundImage) return;

            Control.BackgroundImage = value;
            Invalidate();
        }
    }

    public override ImageLayout BackgroundImageLayout
    {
        get => Control.BackgroundImageLayout;
        set
        {
            if (value == Control.BackgroundImageLayout) return;

            Control.BackgroundImageLayout = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new ToolStripItemDisplayStyle DisplayStyle
    {
        get => base.DisplayStyle;
        set => base.DisplayStyle = value;
    }

    /// <summary>The hosted Control raises its own DoubleClick; this item-level flag never reaches it.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new bool DoubleClickEnabled
    {
        get => false;
        set
        {
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ContentAlignment ImageAlign
    {
        get => base.ImageAlign;
        set => base.ImageAlign = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ToolStripItemImageScaling ImageScaling
    {
        get => base.ImageScaling;
        set => base.ImageScaling = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Color ImageTransparentColor
    {
        get => base.ImageTransparentColor;
        set => base.ImageTransparentColor = value;
    }

    public override RightToLeft RightToLeft
    {
        get => Control.RightToLeft;
        set
        {
            if (value == Control.RightToLeft) return;

            Control.RightToLeft = value;
            OnRightToLeftChanged(EventArgs.Empty);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool RightToLeftAutoMirrorImage
    {
        get => base.RightToLeftAutoMirrorImage;
        set => base.RightToLeftAutoMirrorImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ContentAlignment TextAlign
    {
        get => base.TextAlign;
        set => base.TextAlign = value;
    }

    public override ToolStripTextDirection TextDirection
    {
        get => base.TextDirection;
        set => base.TextDirection = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new TextImageRelation TextImageRelation
    {
        get => base.TextImageRelation;
        set => base.TextImageRelation = value;
    }

    public new event EventHandler? DisplayStyleChanged
    {
        add => base.DisplayStyleChanged += value;
        remove => base.DisplayStyleChanged -= value;
    }

    /// <summary>The hosted Control already lays itself out; this only keeps it in step after an owner-driven relayout.</summary>
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        _control.PerformLayout();
    }
}
