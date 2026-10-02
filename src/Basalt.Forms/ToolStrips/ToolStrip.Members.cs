using System.ComponentModel;
using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// Everything else Microsoft's ToolStrip redeclares over Control and
/// ScrollableControl: most only to adjust a designer attribute or to match
/// the exact declaring type the API-surface test checks for (forwarding,
/// behavior unchanged), a few with real differences documented on each one
/// -- a ToolStrip never scrolls its own items, and dragging its grip (not an
/// item) begins and ends a reparenting drag a <see cref="ToolStripPanel"/>
/// would otherwise finish.
/// </summary>
public partial class ToolStrip
{
    private bool _allowClickThrough = true;
    private bool _allowItemReorder;
    private bool _allowMerge = true;
    private bool _isCurrentlyDragging;

    private static readonly object s_beginDragEvent = new();
    private static readonly object s_endDragEvent = new();

    // --- Scrolling: a ToolStrip arranges its own items directly and never scrolls them. -----------

    private const string AutoScrollNotSupportedMessage = "ToolStrip does not support AutoScroll; its items are always arranged to fit.";

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
    [EditorBrowsable(EditorBrowsableState.Never)]
    public new Point AutoScrollPosition
    {
        get => Point.Empty;
        set
        {
            if (value != Point.Empty) throw new NotSupportedException(AutoScrollNotSupportedMessage);
        }
    }

    public new void SetAutoScrollMargin(int x, int y)
    {
        if (x != 0 || y != 0) throw new NotSupportedException(AutoScrollNotSupportedMessage);
    }

    [Browsable(false)]
    public new HScrollProperties HorizontalScroll => base.HorizontalScroll;

    [Browsable(false)]
    public new VScrollProperties VerticalScroll => base.VerticalScroll;

    protected override void OnScroll(ScrollEventArgs se) => base.OnScroll(se);

    // --- Mutually exclusive drag models: native OLE drop, or in-strip item reorder. -----------------

    /// <summary>
    /// Whether a mouse-down on an item immediately clicks it even if the
    /// button went down somewhere else first (what this project's own mouse
    /// handling already does for every item, so this is a plain switch kept
    /// for source compatibility rather than one that changes anything here).
    /// </summary>
    [DefaultValue(true)]
    public bool AllowClickThrough
    {
        get => _allowClickThrough;
        set => _allowClickThrough = value;
    }

    /// <summary>Dragging a pressed item onto another swaps their place in <see cref="Items"/>; see <see cref="TryReorderOnDrag"/>.</summary>
    [DefaultValue(false)]
    public bool AllowItemReorder
    {
        get => _allowItemReorder;
        set
        {
            if (value == _allowItemReorder) return;
            if (value && AllowDrop) throw new ArgumentException("AllowItemReorder cannot be set to true while AllowDrop is also true.");

            _allowItemReorder = value;
        }
    }

    [DefaultValue(true)]
    public bool AllowMerge
    {
        get => _allowMerge;
        set => _allowMerge = value;
    }

    [Browsable(false)]
    public override bool AllowDrop
    {
        get => base.AllowDrop;
        set
        {
            if (value && AllowItemReorder) throw new ArgumentException("AllowDrop cannot be set to true while AllowItemReorder is also true.");

            base.AllowDrop = value;
        }
    }

    /// <summary>True while the grip is being dragged, between <see cref="OnBeginDrag"/> and <see cref="OnEndDrag"/>.</summary>
    [Browsable(false)]
    public bool IsCurrentlyDragging => _isCurrentlyDragging;

    [Browsable(false)]
    public bool IsDropDown => this is ToolStripDropDown;

    public event EventHandler? BeginDrag
    {
        add => Events.AddHandler(s_beginDragEvent, value);
        remove => Events.RemoveHandler(s_beginDragEvent, value);
    }

    public event EventHandler? EndDrag
    {
        add => Events.AddHandler(s_endDragEvent, value);
        remove => Events.RemoveHandler(s_endDragEvent, value);
    }

    protected virtual void OnBeginDrag(EventArgs e) => ((EventHandler?)Events[s_beginDragEvent])?.Invoke(this, e);

    protected virtual void OnEndDrag(EventArgs e) => ((EventHandler?)Events[s_endDragEvent])?.Invoke(this, e);

    // --- Forwarding: same behavior as Control/ScrollableControl, redeclared to match the exact -----
    // --- declaring type Microsoft's own ToolStrip has (most to add Browsable(false)). --------------

    [Browsable(false)]
    public new Control.ControlCollection Controls => base.Controls;

    [Browsable(false)]
    public new bool HasChildren => base.HasChildren;

    [Browsable(false)]
    public new bool TabStop
    {
        get => base.TabStop;
        set => base.TabStop = value;
    }

    [Browsable(false)]
    public new bool CausesValidation
    {
        get => base.CausesValidation;
        set => base.CausesValidation = value;
    }

    public new event EventHandler? CausesValidationChanged
    {
        add => base.CausesValidationChanged += value;
        remove => base.CausesValidationChanged -= value;
    }

    public new event EventHandler? AutoSizeChanged
    {
        add => base.AutoSizeChanged += value;
        remove => base.AutoSizeChanged -= value;
    }

    public new event ControlEventHandler? ControlAdded
    {
        add => base.ControlAdded += value;
        remove => base.ControlAdded -= value;
    }

    public new event ControlEventHandler? ControlRemoved
    {
        add => base.ControlRemoved += value;
        remove => base.ControlRemoved -= value;
    }

    public new event EventHandler? CursorChanged
    {
        add => base.CursorChanged += value;
        remove => base.CursorChanged -= value;
    }

    public new event EventHandler? ForeColorChanged
    {
        add => base.ForeColorChanged += value;
        remove => base.ForeColorChanged -= value;
    }

    [Browsable(false)]
    public override AnchorStyles Anchor
    {
        get => base.Anchor;
        set => base.Anchor = value;
    }

    [Browsable(true)]
    public override bool AutoSize
    {
        get => base.AutoSize;
        set => base.AutoSize = value;
    }

    public new Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    public override BindingContext? BindingContext
    {
        get => base.BindingContext;
        set => base.BindingContext = value;
    }

    public override Cursor Cursor
    {
        get => base.Cursor;
        set => base.Cursor = value;
    }

    public override Rectangle DisplayRectangle => base.DisplayRectangle;

    public override DockStyle Dock
    {
        get => base.Dock;
        set => base.Dock = value;
    }

    public override Font Font
    {
        get => base.Font;
        set => base.Font = value;
    }

    public new Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    [DefaultValue(false)]
    public bool Stretch { get; set; }

    private ToolStripTextDirection _textDirection = ToolStripTextDirection.Horizontal;

    /// <summary>
    /// The default an item's own TextDirection (Inherit) falls back to.
    /// ToolStripItem does not yet read it (it is owned by another part of
    /// this wave); storing it here is what Microsoft's own ToolStrip does
    /// for the ambient value regardless.
    /// </summary>
    public virtual ToolStripTextDirection TextDirection
    {
        get => _textDirection;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(ToolStripTextDirection));

            _textDirection = value;
        }
    }

    public new Control? GetChildAtPoint(Point pt) => base.GetChildAtPoint(pt);

    public new Control? GetChildAtPoint(Point pt, GetChildAtPointSkip skipValue) => base.GetChildAtPoint(pt, skipValue);

    public void ResetMinimumSize() => MinimumSize = DefaultMinimumSize;

    protected virtual Size MaxItemSize => DisplayRectangle.Size;

    protected override Control.ControlCollection CreateControlsInstance() =>
        // ToolStripControlHost (owned elsewhere) attaches its hosted control through this same public
        // collection, so it cannot be replaced by one that rejects a plain Add: see the task's report.
        base.CreateControlsInstance();

    private LayoutSettings? _layoutSettings;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LayoutSettings? LayoutSettings
    {
        get => _layoutSettings ??= CreateLayoutSettings(LayoutStyle);
        set => _layoutSettings = value;
    }

    /// <summary>
    /// Flow hands back a real settings object (ToolStrip is an
    /// IArrangedElement, which is all FlowLayoutSettings needs). Table has
    /// no ToolStrip-compatible settings type to construct (TableLayoutSettings
    /// is tied to a TableLayoutPanel) and, like the two stack styles this
    /// project's ToolStripLayoutEngine actually runs, keeps the plain marker.
    /// </summary>
    protected virtual LayoutSettings CreateLayoutSettings(ToolStripLayoutStyle layoutStyle) => layoutStyle switch
    {
        ToolStripLayoutStyle.Flow => new FlowLayoutSettings(this),
        _ => new ToolStripStackLayoutSettings(),
    };

    protected override bool IsInputChar(char charCode) => base.IsInputChar(charCode);

    protected override bool IsInputKey(Keys keyData) => base.IsInputKey(keyData);

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData) => base.ProcessCmdKey(ref msg, keyData);

    protected override void OnDockChanged(EventArgs e) => base.OnDockChanged(e);

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Renderer.InitializeOnHandleCreated(this);
    }

    protected override void OnHandleDestroyed(EventArgs e) => base.OnHandleDestroyed(e);

    protected override void OnLeave(EventArgs e) => base.OnLeave(e);

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnTabStopChanged(EventArgs e) => base.OnTabStopChanged(e);

    protected override void OnVisibleChanged(EventArgs e) => base.OnVisibleChanged(e);

    /// <summary>
    /// Gives focus back to this strip's form once keyboard navigation (Alt
    /// into a MenuStrip, arrowing a drop-down) is done with it. Without the
    /// native focus history Microsoft's own RestoreFocus reads, the form
    /// itself is the next best place to send it.
    /// </summary>
    protected virtual void RestoreFocus()
    {
        if (FindForm() is { } form) form.Focus();
    }

    protected override void Select(bool directed, bool forward) => base.Select(directed, forward);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified) =>
        base.SetBoundsCore(x, y, width, height, specified);

    /// <summary>
    /// A plain strip's <see cref="DisplayedItems"/> is already Items itself,
    /// computed live: there is nothing to cache. StatusStrip,
    /// ToolStripDropDownMenu and ToolStripOverflow override this the same
    /// way, for the same reason -- their DisplayedItems is live too.
    /// </summary>
    protected virtual void SetDisplayedItems()
    {
    }

    protected override void SetVisibleCore(bool value)
    {
        base.SetVisibleCore(value);

        if (!value && _overflowButton is { HasDropDown: true } button) button.HideDropDown();
    }
}

/// <summary>A plain marker: the two stack layout styles this project's <see cref="ToolStripLayoutEngine"/> runs do not need settings beyond the engine itself.</summary>
internal sealed class ToolStripStackLayoutSettings : LayoutSettings
{
    public override Layout.LayoutEngine LayoutEngine => ToolStripLayoutEngine.Instance;
}
