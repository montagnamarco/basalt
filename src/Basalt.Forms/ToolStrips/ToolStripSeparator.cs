using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>A thin etched line that divides groups of items on a <see cref="ToolStrip"/>.</summary>
public class ToolStripSeparator : ToolStripItem
{
    public ToolStripSeparator()
    {
        AccessibleRole = AccessibleRole.Separator;
    }

    public override bool CanSelect => false;

    protected override Padding DefaultMargin => Padding.Empty;

    protected override Size DefaultSize => new(6, 6);

    public override bool Enabled
    {
        get => true;
        set
        {
            // A separator is never disabled: Microsoft's own ignores the setter too.
        }
    }

    public override Image? Image
    {
        get => null;
        set
        {
        }
    }

    public override string? Text
    {
        get => null;
        set
        {
        }
    }

    // --- Members ToolStripItem defines that a separator has no use for: each is hidden from the
    // designer and forwards to the base's own storage, exactly as Printing/PrintPreviewDialog.HiddenEvents.cs does. ---

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool AutoToolTip
    {
        get => base.AutoToolTip;
        set => base.AutoToolTip = value;
    }

    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new ToolStripItemDisplayStyle DisplayStyle
    {
        get => base.DisplayStyle;
        set => base.DisplayStyle = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new bool DoubleClickEnabled
    {
        get => base.DoubleClickEnabled;
        set => base.DoubleClickEnabled = value;
    }

    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
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
    public new int ImageIndex
    {
        get => base.ImageIndex;
        set => base.ImageIndex = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string ImageKey
    {
        get => base.ImageKey;
        set => base.ImageKey = value;
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

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new string? ToolTipText
    {
        get => base.ToolTipText;
        set => base.ToolTipText = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DisplayStyleChanged
    {
        add => base.DisplayStyleChanged += value;
        remove => base.DisplayStyleChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? EnabledChanged
    {
        add => base.EnabledChanged += value;
        remove => base.EnabledChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripItemAccessibleObject(this);

    /// <summary>Just a line: always the fixed DefaultSize, never measured from text or an image (it has neither).</summary>
    public override Size GetPreferredSize(Size constrainingSize) => DefaultSize;

    protected override void OnFontChanged(EventArgs e)
    {
        // A separator never paints text, so unlike the base it has nothing to remeasure or invalidate for.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var owner = Owner;
        if (owner is null) return;

        var vertical = owner.Orientation == Orientation.Horizontal;
        owner.Renderer.DrawSeparator(new ToolStripSeparatorRenderEventArgs(e.Graphics, this, vertical));

        base.OnPaint(e);
    }

    /// <summary>Stretched across the strip's cross-axis (its full height on a horizontal strip, width on a vertical one), as a real divider spans it.</summary>
    protected override void SetBounds(Rectangle bounds)
    {
        if (Owner is { } owner)
        {
            var display = owner.DisplayRectangle;

            bounds = owner.Orientation == Orientation.Horizontal
                ? new Rectangle(bounds.X, display.Top, bounds.Width, display.Height)
                : new Rectangle(display.Left, bounds.Y, display.Width, bounds.Height);
        }

        base.SetBounds(bounds);
    }
}
