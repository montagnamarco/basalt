using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The chevron button a <see cref="ToolStrip"/> shows at its trailing edge
/// once something no longer fits: clicking it opens a <see cref="ToolStripOverflow"/>
/// drop-down with whatever no longer fits on the strip. The strip itself places
/// and sizes this button (see <see cref="ToolStrip.OverflowButton"/>); it is
/// never part of <see cref="ToolStrip.Items"/>.
/// </summary>
public class ToolStripOverflowButton : ToolStripDropDownButton
{
    /// <summary>Matches the thickness <see cref="ToolStrip"/> reserves for this button; GetPreferredSize is not otherwise consulted for it.</summary>
    private const int Thickness = 13;

    /// <summary>Not constructible by user code: only <see cref="ToolStrip.OverflowButton"/> ever creates one.</summary>
    internal ToolStripOverflowButton()
    {
        // Its chevron points the other way in a right-to-left strip, unlike an ordinary item's image.
        RightToLeftAutoMirrorImage = true;
    }

    protected override Padding DefaultMargin => Padding.Empty;

    /// <summary>Whether the strip lent anything to the drop-down, read without creating it.</summary>
    public override bool HasDropDownItems => Owner?.OverflowItems.Count > 0;

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [DefaultValue(true)]
    public new bool RightToLeftAutoMirrorImage
    {
        get => base.RightToLeftAutoMirrorImage;
        set => base.RightToLeftAutoMirrorImage = value;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => new ToolStripDropDownItemAccessibleObject(this);

    protected override ToolStripDropDown CreateDefaultDropDown() => new ToolStripOverflow(this);

    /// <summary>Nothing of its own to release; declared again only because disposing this button also means forgetting the strip never truly owned it as an Items entry.</summary>
    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    public override Size GetPreferredSize(Size constrainingSize) => new(Thickness, Thickness);

    /// <summary>
    /// Declared again only to document that, unlike most items, an empty
    /// rectangle here is not clamped up to Thickness: the strip collapses
    /// this button to zero on purpose when there is nothing to overflow,
    /// which is how it stays hidden without a separate Visible switch of
    /// its own (see <see cref="ToolStrip.OverflowButton"/>).
    /// </summary>
    protected override void SetBounds(Rectangle bounds) => base.SetBounds(bounds);

    protected override void OnPaint(PaintEventArgs e)
    {
        var renderer = Owner?.Renderer;
        if (renderer is null)
        {
            base.OnPaint(e);
            return;
        }

        renderer.DrawOverflowButtonBackground(new ToolStripItemRenderEventArgs(e.Graphics, this));

        var direction = Owner?.Orientation == Orientation.Vertical ? ArrowDirection.Right : ArrowDirection.Down;
        renderer.DrawArrow(new ToolStripArrowRenderEventArgs(e.Graphics, this, Rectangle.Inflate(Bounds, -3, -3), ForeColor, direction));
    }
}
