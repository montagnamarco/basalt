using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Two panels, Panel1 and Panel2, with a splitter between them the user
/// drags or steps with the arrow keys to divide the space between them.
/// </summary>
/// <remarks>
/// As with <see cref="Splitter"/>, dragging moves the splitter and resizes
/// the panels live rather than drawing a reversible preview line:
/// <see cref="ControlPaint.DrawReversibleLine"/> has no screen to XOR
/// against in this renderer.
/// </remarks>
[DefaultEvent("SplitterMoved")]
public partial class SplitContainer : ContainerControl, ISupportInitialize
{
    private Orientation _orientation = Orientation.Vertical;
    private int _splitterDistance = 50;
    private int _splitterWidth = 4;
    private int _splitterIncrement = 1;
    private int _panel1MinSize = 25;
    private int _panel2MinSize = 25;
    private FixedPanel _fixedPanel = FixedPanel.None;
    private bool _isSplitterFixed;
    private bool _panel1Collapsed;
    private bool _panel2Collapsed;
    private BorderStyle _borderStyle = BorderStyle.None;

    /// <summary>The extent (width or height, along the orientation's axis) the last layout saw, to scale FixedPanel.None's share of a resize.</summary>
    private int _knownExtent = -1;

    private Rectangle _splitterBounds;

    private bool _dragging;
    private int _dragAnchor;
    private int _dragStartDistance;

    public SplitContainer()
    {
        SetStyle(ControlStyles.Selectable | ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        TabStop = true;

        Panel1 = new SplitterPanel(this);
        Panel2 = new SplitterPanel(this);

        Controls.Add(Panel1);
        Controls.Add(Panel2);
    }

    protected override Size DefaultSize => new(150, 100);

    [DefaultValue(Orientation.Vertical)]
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Orientation));
            if (_orientation == value) return;

            _orientation = value;

            // The axis changed: the extent recorded along the other one no longer applies to a resize.
            _knownExtent = -1;

            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(50)]
    public int SplitterDistance
    {
        get => _splitterDistance;
        set
        {
            var clamped = ClampDistance(value);
            if (clamped == _splitterDistance) return;

            _splitterDistance = clamped;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(4)]
    public int SplitterWidth
    {
        get => _splitterWidth;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "SplitterWidth must be greater than zero.");
            if (_splitterWidth == value) return;

            _splitterWidth = value;
            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(1)]
    public int SplitterIncrement
    {
        get => _splitterIncrement;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "SplitterIncrement must be greater than zero.");

            _splitterIncrement = value;
        }
    }

    [Browsable(false)]
    public Rectangle SplitterRectangle => _splitterBounds;

    [DefaultValue(25)]
    public int Panel1MinSize
    {
        get => _panel1MinSize;
        set
        {
            if (_panel1MinSize == value) return;

            _panel1MinSize = value;
            PerformLayout();
        }
    }

    [DefaultValue(25)]
    public int Panel2MinSize
    {
        get => _panel2MinSize;
        set
        {
            if (_panel2MinSize == value) return;

            _panel2MinSize = value;
            PerformLayout();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public SplitterPanel Panel1 { get; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
    public SplitterPanel Panel2 { get; }

    [DefaultValue(false)]
    public bool Panel1Collapsed
    {
        get => _panel1Collapsed;
        set
        {
            if (_panel1Collapsed == value) return;

            _panel1Collapsed = value;
            if (value) _panel2Collapsed = false;

            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool Panel2Collapsed
    {
        get => _panel2Collapsed;
        set
        {
            if (_panel2Collapsed == value) return;

            _panel2Collapsed = value;
            if (value) _panel1Collapsed = false;

            PerformLayout();
            Invalidate();
        }
    }

    [DefaultValue(FixedPanel.None)]
    public FixedPanel FixedPanel
    {
        get => _fixedPanel;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(FixedPanel));

            _fixedPanel = value;
        }
    }

    [DefaultValue(false)]
    public bool IsSplitterFixed
    {
        get => _isSplitterFixed;
        set => _isSplitterFixed = value;
    }

    [DefaultValue(BorderStyle.None)]
    public BorderStyle BorderStyle
    {
        get => _borderStyle;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(BorderStyle));
            if (_borderStyle == value) return;

            _borderStyle = value;
            PerformLayout();
            Invalidate();
        }
    }

    public event SplitterEventHandler? SplitterMoved
    {
        add => Events.AddHandler(s_splitterMovedEvent, value);
        remove => Events.RemoveHandler(s_splitterMovedEvent, value);
    }

    public event SplitterCancelEventHandler? SplitterMoving
    {
        add => Events.AddHandler(s_splitterMovingEvent, value);
        remove => Events.RemoveHandler(s_splitterMovingEvent, value);
    }

    private static readonly object s_splitterMovedEvent = new();
    private static readonly object s_splitterMovingEvent = new();

    public void OnSplitterMoved(SplitterEventArgs e) => ((SplitterEventHandler?)Events[s_splitterMovedEvent])?.Invoke(this, e);

    public void OnSplitterMoving(SplitterCancelEventArgs e) => ((SplitterCancelEventHandler?)Events[s_splitterMovingEvent])?.Invoke(this, e);

    public void BeginInit()
    {
    }

    public void EndInit()
    {
    }

    /// <summary>Only Panel1 and Panel2 may sit directly in a SplitContainer; everything else goes inside one of them.</summary>
    protected override ControlCollection CreateControlsInstance() => new SplitContainerControlCollection(this);

    private sealed class SplitContainerControlCollection : ControlCollection
    {
        private readonly SplitContainer _owner;

        public SplitContainerControlCollection(SplitContainer owner)
            : base(owner)
        {
            _owner = owner;
        }

        public override void Add(Control? value)
        {
            if (value is not SplitterPanel panel || (panel != _owner.Panel1 && panel != _owner.Panel2))
            {
                throw new ArgumentException(
                    "Cannot add or remove a control directly to or from a SplitContainer. Add or remove controls to or from the SplitterPanel instead.",
                    nameof(value));
            }

            base.Add(value);
        }
    }

    /// <summary>The distance kept between Panel1MinSize and what Panel2MinSize and the splitter's own width leave available.</summary>
    private int ClampDistance(int value)
    {
        var extent = _orientation == Orientation.Vertical ? ClientSize.Width : ClientSize.Height;
        var available = Math.Max(extent - _splitterWidth, 0);

        var min = _panel1MinSize;
        var max = available - _panel2MinSize;
        if (max < min) max = min;

        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// How a resize changes SplitterDistance: Panel1's fixed panel keeps it
    /// unchanged, Panel2's keeps Panel2's own size (so the distance absorbs
    /// the whole delta), and neither fixed shares the delta by the panels'
    /// current proportion.
    /// </summary>
    private void AdjustForResize(int previousExtent, int newExtent)
    {
        if (previousExtent <= 0 || previousExtent == newExtent) return;

        switch (_fixedPanel)
        {
            case FixedPanel.Panel1:
                break;

            case FixedPanel.Panel2:
                _splitterDistance += newExtent - previousExtent;
                break;

            default:
                var ratio = (double)_splitterDistance / previousExtent;
                _splitterDistance = (int)Math.Round(ratio * newExtent);
                break;
        }
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);
        ArrangePanels();
    }

    private void ArrangePanels()
    {
        // Control's own constructor sizes itself from DefaultSize before SplitContainer's constructor body
        // creates the panels; a layout caused by that early resize has nothing to arrange yet.
        if (Panel1 is null || Panel2 is null) return;

        var client = ClientRectangle;

        var inset = _borderStyle switch
        {
            BorderStyle.FixedSingle => 1,
            BorderStyle.Fixed3D => 2,
            _ => 0,
        };
        client.Inflate(-inset, -inset);

        var extent = _orientation == Orientation.Vertical ? client.Width : client.Height;
        if (_knownExtent >= 0) AdjustForResize(_knownExtent, extent);
        _knownExtent = extent;

        if (_panel1Collapsed)
        {
            Panel1.Visible = false;
            Panel2.Visible = true;
            Panel2.SetBounds(client.X, client.Y, client.Width, client.Height);
            _splitterBounds = Rectangle.Empty;
            return;
        }

        if (_panel2Collapsed)
        {
            Panel1.Visible = true;
            Panel2.Visible = false;
            Panel1.SetBounds(client.X, client.Y, client.Width, client.Height);
            _splitterBounds = Rectangle.Empty;
            return;
        }

        Panel1.Visible = true;
        Panel2.Visible = true;

        _splitterDistance = ClampDistance(_splitterDistance);

        if (_orientation == Orientation.Vertical)
        {
            Panel1.SetBounds(client.X, client.Y, _splitterDistance, client.Height);
            _splitterBounds = new Rectangle(client.X + _splitterDistance, client.Y, _splitterWidth, client.Height);

            var panel2Width = Math.Max(client.Width - _splitterDistance - _splitterWidth, 0);
            Panel2.SetBounds(client.X + _splitterDistance + _splitterWidth, client.Y, panel2Width, client.Height);
        }
        else
        {
            Panel1.SetBounds(client.X, client.Y, client.Width, _splitterDistance);
            _splitterBounds = new Rectangle(client.X, client.Y + _splitterDistance, client.Width, _splitterWidth);

            var panel2Height = Math.Max(client.Height - _splitterDistance - _splitterWidth, 0);
            Panel2.SetBounds(client.X, client.Y + _splitterDistance + _splitterWidth, client.Width, panel2Height);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || _isSplitterFixed || _panel1Collapsed || _panel2Collapsed) return;
        if (!_splitterBounds.Contains(e.Location)) return;

        if (CanSelect) Focus();

        _dragging = true;
        Capture = true;
        _dragAnchor = _orientation == Orientation.Vertical ? e.X : e.Y;
        _dragStartDistance = _splitterDistance;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_dragging) return;

        var current = _orientation == Orientation.Vertical ? e.X : e.Y;
        var proposed = ClampDistance(_dragStartDistance + (current - _dragAnchor));

        var moving = new SplitterCancelEventArgs(e.X, e.Y, _splitterBounds.X, _splitterBounds.Y);
        OnSplitterMoving(moving);

        if (moving.Cancel)
        {
            _dragging = false;
            Capture = false;
            return;
        }

        if (proposed == _splitterDistance) return;

        _splitterDistance = proposed;
        PerformLayout();
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!_dragging) return;

        _dragging = false;
        Capture = false;

        OnSplitterMoved(new SplitterEventArgs(e.X, e.Y, _splitterBounds.X, _splitterBounds.Y));
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        // Another control may take capture during a drag. MouseUp will then
        // reach that control, so stop applying movement to this splitter.
        if (!Capture) _dragging = false;
    }

    /// <summary>Tab moves between the two panels and on; the arrow keys move the splitter when it, not a child, has the focus.</summary>
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (!_isSplitterFixed && Focused && MoveSplitterByKey(keyData)) return true;

        return base.ProcessDialogKey(keyData);
    }

    private bool MoveSplitterByKey(Keys keyData)
    {
        if ((keyData & (Keys.Alt | Keys.Control | Keys.Shift)) != Keys.None) return false;

        int? step = (keyData & Keys.KeyCode) switch
        {
            Keys.Left when _orientation == Orientation.Vertical => -_splitterIncrement,
            Keys.Right when _orientation == Orientation.Vertical => _splitterIncrement,
            Keys.Up when _orientation == Orientation.Horizontal => -_splitterIncrement,
            Keys.Down when _orientation == Orientation.Horizontal => _splitterIncrement,
            _ => null,
        };

        if (step is not { } delta) return false;
        if (_panel1Collapsed || _panel2Collapsed) return true;

        var proposed = ClampDistance(_splitterDistance + delta);

        var moving = new SplitterCancelEventArgs(0, 0, _splitterBounds.X, _splitterBounds.Y);
        OnSplitterMoving(moving);
        if (moving.Cancel) return true;

        if (proposed != _splitterDistance)
        {
            _splitterDistance = proposed;
            PerformLayout();
            Invalidate();
            OnSplitterMoved(new SplitterEventArgs(0, 0, _splitterBounds.X, _splitterBounds.Y));
        }

        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        if (_borderStyle == BorderStyle.Fixed3D) ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Sunken);
        else if (_borderStyle == BorderStyle.FixedSingle) ControlPaint.DrawBorder3D(e.Graphics, ClientRectangle, Border3DStyle.Flat);

        if (_splitterBounds.Width <= 0 || _splitterBounds.Height <= 0) return;

        using var face = new SolidBrush(SystemColors.Control);
        e.Graphics.FillRectangle(face, _splitterBounds);

        if (!Focused) return;

        var focus = Rectangle.Inflate(_splitterBounds, -1, -1);
        if (focus.Width > 0 && focus.Height > 0) DrawFocusDots(e.Graphics, focus);
    }

    /// <summary>The dotted focus rectangle over the splitter bar: every other pixel, as Win32 DrawFocusRect.</summary>
    private static void DrawFocusDots(Graphics graphics, Rectangle bounds)
    {
        using var brush = new SolidBrush(SystemColors.ControlText);

        for (var x = bounds.Left; x < bounds.Right; x += 2)
        {
            graphics.FillRectangle(brush, x, bounds.Top, 1, 1);
            graphics.FillRectangle(brush, x, bounds.Bottom - 1, 1, 1);
        }

        for (var y = bounds.Top; y < bounds.Bottom; y += 2)
        {
            graphics.FillRectangle(brush, bounds.Left, y, 1, 1);
            graphics.FillRectangle(brush, bounds.Right - 1, y, 1, 1);
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate(_splitterBounds, false);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate(_splitterBounds, false);
    }
}
