using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Members re-declared only to keep the public shape Microsoft declares on
/// this type: ownership of the colour, background and text properties
/// (hidden by TabControl, which paints its own strip from <see
/// cref="SystemColors.Control"/> rather than these), and OnDrawItem, which
/// <see cref="DrawTab"/> now calls when <see cref="DrawMode"/> is not
/// <see cref="TabDrawMode.Normal"/>, exactly as owner-drawn tabs ask for.
/// </summary>
public partial class TabControl
{
    private static readonly object s_drawItemEvent = new();

    public event DrawItemEventHandler? DrawItem
    {
        add => Events.AddHandler(s_drawItemEvent, value);
        remove => Events.RemoveHandler(s_drawItemEvent, value);
    }

    protected virtual void OnDrawItem(DrawItemEventArgs e) => ((DrawItemEventHandler?)Events[s_drawItemEvent])?.Invoke(this, e);

    /// <summary>Whether something is listening, so <see cref="DrawTab"/> can skip its own painting entirely, as Windows does for an owner-drawn tab.</summary>
    private bool RaiseDrawItem(Graphics graphics, Rectangle bounds, int index, bool selected)
    {
        if (_drawMode == TabDrawMode.Normal) return false;

        var state = selected ? DrawItemState.Selected : DrawItemState.None;
        using var e = new DrawItemEventArgs(graphics, Font, bounds, index, state, ForeColor, SystemColors.Control);
        OnDrawItem(e);

        return true;
    }

    protected override AccessibleObject CreateAccessibilityInstance() => base.CreateAccessibilityInstance();

    protected override void CreateHandle() => base.CreateHandle();

    protected override void Dispose(bool disposing) => base.Dispose(disposing);

    protected override void OnGotFocus(EventArgs e) => base.OnGotFocus(e);

    protected override void OnHandleCreated(EventArgs e) => base.OnHandleCreated(e);

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnStyleChanged(EventArgs e) => base.OnStyleChanged(e);

    protected override bool ProcessKeyPreview(ref Message m) => base.ProcessKeyPreview(ref m);

    protected override CreateParams CreateParams => base.CreateParams;

    protected override bool DoubleBuffered
    {
        get => base.DoubleBuffered;
        set => base.DoubleBuffered = value;
    }

    // Stored and raised only: the strip is not mirrored yet (the same known gap as MonthCalendar.RightToLeftLayout).
    private bool _rightToLeftLayout;

    private static readonly object s_rightToLeftLayoutChangedEvent = new();

    [DefaultValue(false)]
    [Localizable(true)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (value == _rightToLeftLayout) return;

            _rightToLeftLayout = value;
            OnRightToLeftLayoutChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutChangedEvent])?.Invoke(this, e);

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_rightToLeftLayoutChangedEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftLayoutChangedEvent, value);
    }

    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
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

    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

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
    public new event EventHandler? ForeColorChanged
    {
        add => base.ForeColorChanged += value;
        remove => base.ForeColorChanged -= value;
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
    public new event EventHandler? TextChanged
    {
        add => base.TextChanged += value;
        remove => base.TextChanged -= value;
    }
}
