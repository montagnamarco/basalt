using System.ComponentModel;
using System.Drawing;
using System.Diagnostics.CodeAnalysis;

namespace System.Windows.Forms;

/// <summary>
/// Everything else Microsoft's ToolStripPanel redeclares over
/// ContainerControl: CreateAccessibilityInstance, disposing the rows along
/// with the panel, painting through the renderer (same Handled-skips-the-
/// default-fill pattern as ToolStripContentPanel), and the forwarding the
/// rest of the list calls for.
/// </summary>
public partial class ToolStripPanel
{
    protected override AccessibleObject CreateAccessibilityInstance() => new ControlAccessibleObject(this);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var row in _rows) row.Dispose();
            _rows.Clear();
        }

        base.Dispose(disposing);
    }

    protected override void OnLayout(LayoutEventArgs levent) => base.OnLayout(levent);

    /// <summary>The band a strip can be dragged into, painted through the renderer; Handled lets a subscriber skip the default fill.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var args = new ToolStripPanelRenderEventArgs(e.Graphics, this);
        Renderer.DrawToolStripPanelBackground(args);

        if (!args.Handled) base.OnPaintBackground(e);
    }

    protected override void OnParentChanged(EventArgs e) => base.OnParentChanged(e);

    protected override void OnRightToLeftChanged(EventArgs e)
    {
        base.OnRightToLeftChanged(e);
        Invalidate(true);
    }

    // --- Scrolling: a ToolStripPanel arranges its rows directly and never scrolls them. -------------

    private const string AutoScrollNotSupportedMessage = "ToolStripPanel does not support AutoScroll; its rows are always arranged to fit.";

    [DefaultValue(false)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override bool AutoScroll
    {
        get => false;
        set
        {
            if (value) throw new NotSupportedException(AutoScrollNotSupportedMessage);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size AutoScrollMargin
    {
        get => Size.Empty;
        set
        {
            if (value != Size.Empty) throw new NotSupportedException(AutoScrollNotSupportedMessage);
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Size AutoScrollMinSize
    {
        get => Size.Empty;
        set
        {
            if (value != Size.Empty) throw new NotSupportedException(AutoScrollNotSupportedMessage);
        }
    }

    [Browsable(false)]
    public override bool AllowDrop
    {
        get => base.AllowDrop;
        set => base.AllowDrop = value;
    }

    [Browsable(true)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    public override DockStyle Dock
    {
        get => base.Dock;
        set => base.Dock = value;
    }

    [Browsable(false)]
    public new int TabIndex
    {
        get => base.TabIndex;
        set => base.TabIndex = value;
    }

    [Browsable(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    public new event EventHandler? TabIndexChanged
    {
        add => base.TabIndexChanged += value;
        remove => base.TabIndexChanged -= value;
    }

    public new event EventHandler? TabStopChanged
    {
        add => base.TabStopChanged += value;
        remove => base.TabStopChanged -= value;
    }

    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }
}
