using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The Control/ContainerControl members PropertyGrid re-declares purely to
/// change how the designer sees them (hidden events, DefaultValue-only
/// property overrides) or to turn off behaviour that would conflict with the
/// grid's own drawing (BackColor/ForeColor/BackgroundImage apply to the
/// whole control surface, which PropertyGrid paints itself; AutoScroll and
/// Controls are ContainerControl/ScrollableControl members PropertyGrid does
/// not use the way a plain container would). Every one of them forwards to
/// base, the same pattern as PrintPreviewDialog.HiddenEvents.cs.
/// </summary>
public partial class PropertyGrid
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AutoScroll
    {
        get => base.AutoScroll;
        set { } // PropertyGrid draws its own scroll bar in the grid view; ScrollableControl's never engages.
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
    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>Re-declared, like Microsoft's, only so the designer never lists the grid's own child controls (the edit box, the drop-down holder) as a Controls collection to populate.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new ControlCollection Controls => base.Controls;

    /// <summary>Re-declared only to give it DefaultValue(0,0,0,0): PropertyGrid has no border padding a user would set.</summary>
    [DefaultValue(typeof(Padding), "0, 0, 0, 0")]
    public new Padding Padding
    {
        get => base.Padding;
        set => base.Padding = value;
    }

    public override ISite? Site
    {
        get => base.Site;
        set => base.Site = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageChanged
    {
        add => base.BackgroundImageChanged += value;
        remove => base.BackgroundImageChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackgroundImageLayoutChanged
    {
        add => base.BackgroundImageLayoutChanged += value;
        remove => base.BackgroundImageLayoutChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? ForeColorChanged
    {
        add => base.ForeColorChanged += value;
        remove => base.ForeColorChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyDown
    {
        add => base.KeyDown += value;
        remove => base.KeyDown -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyPressEventHandler? KeyPress
    {
        add => base.KeyPress += value;
        remove => base.KeyPress -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event KeyEventHandler? KeyUp
    {
        add => base.KeyUp += value;
        remove => base.KeyUp -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseDown
    {
        add => base.MouseDown += value;
        remove => base.MouseDown -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MouseEnter
    {
        add => base.MouseEnter += value;
        remove => base.MouseEnter -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MouseLeave
    {
        add => base.MouseLeave += value;
        remove => base.MouseLeave -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseMove
    {
        add => base.MouseMove += value;
        remove => base.MouseMove -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event MouseEventHandler? MouseUp
    {
        add => base.MouseUp += value;
        remove => base.MouseUp -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? PaddingChanged
    {
        add => base.PaddingChanged += value;
        remove => base.PaddingChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }
}
