using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A thin bar, docked against an edge of its parent, that the user drags to
/// resize the control docked on the same edge immediately ahead of it.
/// </summary>
/// <remarks>
/// WinForms drags this with a reversible (XOR) preview line on the screen's
/// device context, then applies the new size once on release.
/// <see cref="ControlPaint.DrawReversibleLine"/> is a no-op here (see its own
/// remarks: there is no screen device context to XOR against), so this control
/// moves its target live, on every MouseMove, instead of drawing a preview.
/// </remarks>
[DefaultEvent("SplitterMoved")]
[DefaultProperty("Dock")]
public partial class Splitter : Control
{
    private const int DefaultThickness = 3;

    private BorderStyle _borderStyle = BorderStyle.None;
    private int _minSize = 25;
    private int _minExtra = 25;

    private bool _dragging;
    private Control? _dragTarget;
    private int _dragAnchor;
    private int _dragStartSize;

    public Splitter()
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Dock = DockStyle.Left;
    }

    protected override Size DefaultSize => new(DefaultThickness, DefaultThickness);

    protected override Cursor DefaultCursor => Dock is DockStyle.Left or DockStyle.Right ? Cursors.VSplit : Cursors.HSplit;

    [DefaultValue(BorderStyle.None)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (value == _borderStyle) return;

            _borderStyle = value;
            UpdateStyles();
        }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;

            if (_borderStyle == BorderStyle.FixedSingle) parameters.Style |= WindowStyles.Border;
            else if (_borderStyle == BorderStyle.Fixed3D) parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    [DefaultValue(25)]
    public int MinExtra
    {
        get => _minExtra;
        set
        {
            if (value < 0) value = 0;

            _minExtra = value;
        }
    }

    [DefaultValue(25)]
    public int MinSize
    {
        get => _minSize;
        set
        {
            if (value < 0) value = 0;

            _minSize = value;
        }
    }

    /// <summary>
    /// The size, along the axis the splitter travels, of the control it
    /// follows: reading it when nothing is docked on this splitter's edge
    /// answers -1, as WinForms does.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SplitPosition
    {
        get
        {
            var target = FindTarget();
            if (target is null) return -1;

            return Dock is DockStyle.Left or DockStyle.Right ? target.Width : target.Height;
        }
        set
        {
            var target = FindTarget();
            if (target is null) return;

            ApplySplitPosition(target, value);
        }
    }

    public override string ToString() => $"{base.ToString()}, MinExtra: {MinExtra}, MinSize: {MinSize}";

    public event SplitterEventHandler? SplitterMoved
    {
        add => Events.AddHandler(s_splitterMovedEvent, value);
        remove => Events.RemoveHandler(s_splitterMovedEvent, value);
    }

    public event SplitterEventHandler? SplitterMoving
    {
        add => Events.AddHandler(s_splitterMovingEvent, value);
        remove => Events.RemoveHandler(s_splitterMovingEvent, value);
    }

    private static readonly object s_splitterMovedEvent = new();
    private static readonly object s_splitterMovingEvent = new();

    protected virtual void OnSplitterMoved(SplitterEventArgs sevent) => ((SplitterEventHandler?)Events[s_splitterMovedEvent])?.Invoke(this, sevent);

    protected virtual void OnSplitterMoving(SplitterEventArgs sevent) => ((SplitterEventHandler?)Events[s_splitterMovingEvent])?.Invoke(this, sevent);

    /// <summary>
    /// The control this splitter resizes: the first one, scanning the
    /// parent's children from just after this splitter towards the back of
    /// the z-order, docked on the same edge. That is the control the
    /// splitter was placed right after, and so the one it "follows" when the
    /// parent docks its children back to front.
    /// </summary>
    private Control? FindTarget()
    {
        if (Parent is not { } parent) return null;

        var index = parent.Controls.GetChildIndex(this, false);
        if (index < 0) return null;

        for (var i = index + 1; i < parent.Controls.Count; i++)
        {
            var candidate = parent.Controls[i];
            if (candidate.Dock == Dock) return candidate;
        }

        return null;
    }

    /// <summary>Resizes <paramref name="target"/> to <paramref name="value"/>, kept between MinSize and MinExtra of the room left.</summary>
    private void ApplySplitPosition(Control target, int value)
    {
        if (Parent is not { } parent) return;

        var horizontal = Dock is DockStyle.Left or DockStyle.Right;
        var available = horizontal ? parent.ClientSize.Width : parent.ClientSize.Height;
        var thickness = horizontal ? Width : Height;

        var max = Math.Max(_minSize, available - thickness - _minExtra);
        var clamped = Math.Clamp(value, _minSize, max);

        if (horizontal) target.Width = clamped;
        else target.Height = clamped;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left) return;

        var target = FindTarget();
        if (target is null) return;

        _dragTarget = target;
        _dragging = true;
        Capture = true;

        var horizontal = Dock is DockStyle.Left or DockStyle.Right;
        var screen = PointToScreen(e.Location);
        _dragAnchor = horizontal ? screen.X : screen.Y;
        _dragStartSize = horizontal ? target.Width : target.Height;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging || _dragTarget is not { } target) return;

        var horizontal = Dock is DockStyle.Left or DockStyle.Right;
        var screen = PointToScreen(e.Location);
        var current = horizontal ? screen.X : screen.Y;
        var delta = current - _dragAnchor;

        // A splitter on the trailing edge (Right/Bottom) grows its target the opposite way the mouse moves.
        var proposed = Dock is DockStyle.Right or DockStyle.Bottom ? _dragStartSize - delta : _dragStartSize + delta;

        var splitterScreen = PointToScreen(Point.Empty);
        OnSplitterMoving(new SplitterEventArgs(e.X, e.Y, splitterScreen.X, splitterScreen.Y));

        ApplySplitPosition(target, proposed);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_dragging) return;

        _dragging = false;
        Capture = false;
        _dragTarget = null;

        var splitterScreen = PointToScreen(Point.Empty);
        OnSplitterMoved(new SplitterEventArgs(e.X, e.Y, splitterScreen.X, splitterScreen.Y));
    }

    internal override void DefaultCaptureLost()
    {
        base.DefaultCaptureLost();
        _dragging = false;
        _dragTarget = null;
    }

    /// <summary>Escape abandons the drag, putting the target back to the size it had when the mouse pressed.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Escape && CancelDrag()) e.Handled = true;
    }

    /// <summary>
    /// The splitter never has the focus, so during a drag Escape reaches it
    /// here, through the capture, as WinForms catches it with a message filter.
    /// </summary>
    internal override bool KeyDownWhileCaptured(Keys keyData) => (keyData & Keys.KeyCode) == Keys.Escape && CancelDrag();

    private bool CancelDrag()
    {
        if (!_dragging || _dragTarget is not { } target) return false;

        var horizontal = Dock is DockStyle.Left or DockStyle.Right;
        if (horizontal) target.Width = _dragStartSize;
        else target.Height = _dragStartSize;

        _dragging = false;
        Capture = false;
        _dragTarget = null;

        return true;
    }
}
