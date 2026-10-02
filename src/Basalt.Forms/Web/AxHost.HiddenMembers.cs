using System.ComponentModel;
using System.Drawing;
using System.Diagnostics.CodeAnalysis;

namespace System.Windows.Forms;

/// <summary>
/// The Control members AxHost re-declares, for the same reason
/// <see cref="Basalt.Forms.Printing.PrintPreviewDialog"/>'s hidden members
/// file does: Microsoft hides them from the designer because a generated
/// Ax wrapper class is meant to expose the ocx's own properties instead.
/// Most forward straight to Control's storage; RightToLeft is the one real
/// difference - AxHost's own is a plain bool, not the three-state enum
/// Control uses, so it hides the property entirely rather than overriding it.
/// </summary>
public abstract partial class AxHost
{
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
    public override Cursor Cursor
    {
        get => base.Cursor;
        set => base.Cursor = value;
    }

    /// <summary>Control.Enabled is not itself overridable; AxHost opens its own virtual slot so a generated Ax wrapper can refine it.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new virtual bool Enabled
    {
        get => base.Enabled;
        set => base.Enabled = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    /// <summary>
    /// AxHost's own RightToLeft is a plain bool (an ocx asks for reading
    /// order as a flag, not Control's three-state enum), so it hides
    /// Control.RightToLeft entirely instead of overriding it: Yes maps to
    /// true, anything else - No or Inherit - to false.
    /// </summary>
    [Browsable(true)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new virtual bool RightToLeft
    {
        get => base.RightToLeft == System.Windows.Forms.RightToLeft.Yes;
        set => base.RightToLeft = value ? System.Windows.Forms.RightToLeft.Yes : System.Windows.Forms.RightToLeft.No;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>Only the setter is re-declared, as in WinForms: the getter still comes from Control.</summary>
    public override ISite? Site
    {
        set => base.Site = value;
    }

    public new void DrawToBitmap(Bitmap bitmap, Rectangle targetBounds) => base.DrawToBitmap(bitmap, targetBounds);

    public override bool PreProcessMessage(ref Message msg) => base.PreProcessMessage(ref msg);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? BackColorChanged
    {
        add => base.BackColorChanged += value;
        remove => base.BackColorChanged -= value;
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
    public new event EventHandler? BindingContextChanged
    {
        add => base.BindingContextChanged += value;
        remove => base.BindingContextChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? Click
    {
        add => base.Click += value;
        remove => base.Click -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? CursorChanged
    {
        add => base.CursorChanged += value;
        remove => base.CursorChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DoubleClick
    {
        add => base.DoubleClick += value;
        remove => base.DoubleClick -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DragEventHandler? DragDrop
    {
        add => base.DragDrop += value;
        remove => base.DragDrop -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DragEventHandler? DragEnter
    {
        add => base.DragEnter += value;
        remove => base.DragEnter -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? DragLeave
    {
        add => base.DragLeave += value;
        remove => base.DragLeave -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event DragEventHandler? DragOver
    {
        add => base.DragOver += value;
        remove => base.DragOver -= value;
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
    public new event EventHandler? FontChanged
    {
        add => base.FontChanged += value;
        remove => base.FontChanged -= value;
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
    public new event GiveFeedbackEventHandler? GiveFeedback
    {
        add => base.GiveFeedback += value;
        remove => base.GiveFeedback -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event HelpEventHandler? HelpRequested
    {
        add => base.HelpRequested += value;
        remove => base.HelpRequested -= value;
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
    public new event LayoutEventHandler? Layout
    {
        add => base.Layout += value;
        remove => base.Layout -= value;
    }

    private static readonly object s_mouseClickEvent = new();
    private static readonly object s_mouseDoubleClickEvent = new();

    /// <summary>
    /// Microsoft's own AxHost redeclares this with a plain EventHandler,
    /// not Control's MouseEventHandler - a long-standing mismatch kept for
    /// compatibility. It is never raised: nothing here calls it, matching
    /// that this handler shape could not receive mouse coordinates anyway.
    /// </summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MouseClick
    {
        add => Events.AddHandler(s_mouseClickEvent, value);
        remove => Events.RemoveHandler(s_mouseClickEvent, value);
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? MouseDoubleClick
    {
        add => Events.AddHandler(s_mouseDoubleClickEvent, value);
        remove => Events.RemoveHandler(s_mouseDoubleClickEvent, value);
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
    public new event EventHandler? MouseHover
    {
        add => base.MouseHover += value;
        remove => base.MouseHover -= value;
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
    public new event MouseEventHandler? MouseWheel
    {
        add => base.MouseWheel += value;
        remove => base.MouseWheel -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event PaintEventHandler? Paint
    {
        add => base.Paint += value;
        remove => base.Paint -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event QueryAccessibilityHelpEventHandler? QueryAccessibilityHelp
    {
        add => base.QueryAccessibilityHelp += value;
        remove => base.QueryAccessibilityHelp -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event QueryContinueDragEventHandler? QueryContinueDrag
    {
        add => base.QueryContinueDrag += value;
        remove => base.QueryContinueDrag -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? RightToLeftChanged
    {
        add => base.RightToLeftChanged += value;
        remove => base.RightToLeftChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? StyleChanged
    {
        add => base.StyleChanged += value;
        remove => base.StyleChanged -= value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }
}
