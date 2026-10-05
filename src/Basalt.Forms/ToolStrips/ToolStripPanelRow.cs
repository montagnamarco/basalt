using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// One horizontal (or, in a vertical panel, one vertical) band of a
/// <see cref="ToolStripPanel"/>: the <see cref="ToolStrip"/>s <see cref="ToolStripPanel.Join(ToolStrip)"/>
/// put in it stack along the row's own axis; the panel stacks the rows
/// themselves along the other one.
/// </summary>
public class ToolStripPanelRow : Component, IArrangedElement
{
    private readonly ToolStripPanel _panel;
    private readonly List<Control> _controls = [];
    private Rectangle _bounds;
    private Padding _margin;
    private Padding _padding;
    private readonly RowControlCollection _children = new();
    private readonly LayoutSettingsStore _layoutStore = new();

    public ToolStripPanelRow(ToolStripPanel parent)
    {
        ArgumentNullException.ThrowIfNull(parent);

        _panel = parent;
        _margin = DefaultMargin;
        _padding = DefaultPadding;
        _layoutStore.Margin = _margin;
        _layoutStore.Padding = _padding;
    }

    public Rectangle Bounds => _bounds;

    /// <summary>The strips currently in this row, in the order they stack.</summary>
    public Control[] Controls => _controls.ToArray();

    protected virtual Padding DefaultMargin => Padding.Empty;

    protected virtual Padding DefaultPadding => Padding.Empty;

    public Rectangle DisplayRectangle => new(
        _bounds.X + _padding.Left,
        _bounds.Y + _padding.Top,
        Math.Max(_bounds.Width - _padding.Horizontal, 0),
        Math.Max(_bounds.Height - _padding.Vertical, 0));

    /// <summary>Not consulted by this implementation: the owning panel positions a row's strips directly.</summary>
    public LayoutEngine LayoutEngine => DefaultLayout.Instance;

    public Padding Margin
    {
        get => _margin;
        set
        {
            if (value == _margin) return;

            _margin = value;
            _layoutStore.Margin = value;
            _panel.PerformLayout();
        }
    }

    public Orientation Orientation => _panel.Orientation;

    public virtual Padding Padding
    {
        get => _padding;
        set
        {
            if (value == _padding) return;

            _padding = value;
            _layoutStore.Padding = value;
            _panel.PerformLayout();
        }
    }

    public ToolStripPanel ToolStripPanel => _panel;

    /// <summary>Whether the strip could move into this row: always true, as every strip fits some row in this implementation.</summary>
    public bool CanMove(ToolStrip toolStripToDrag) => toolStripToDrag is not null;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controls.Clear();
            _children.Clear();
        }

        base.Dispose(disposing);
    }

    protected void OnBoundsChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        // No public event to raise: Microsoft's own row does not expose one either.
    }

    protected virtual void OnControlAdded(Control control, int index)
    {
    }

    protected virtual void OnControlRemoved(Control control, int index)
    {
    }

    protected virtual void OnLayout(LayoutEventArgs e)
    {
        // The panel computes every row's bounds and its controls' positions itself (see ToolStripPanel.PerformRowLayout).
    }

    protected virtual void OnOrientationChanged()
    {
    }

    internal void NotifyOrientationChanged() => OnOrientationChanged();

    internal void SetBoundsInternal(Rectangle bounds)
    {
        if (bounds == _bounds) return;

        var old = _bounds;
        _bounds = bounds;
        OnBoundsChanged(old, bounds);
    }

    internal void AddControlInternal(Control control)
    {
        _controls.Add(control);
        _children.Add(control);
        OnControlAdded(control, _controls.Count - 1);
    }

    internal bool RemoveControlInternal(Control control)
    {
        var index = _controls.IndexOf(control);
        if (index < 0) return false;

        _controls.RemoveAt(index);
        _children.Remove(control);
        OnControlRemoved(control, index);

        return true;
    }

    internal bool IsEmpty => _controls.Count == 0;

    void IArrangedElement.SetBounds(Rectangle bounds, BoundsSpecified specified)
    {
        if (specified != BoundsSpecified.None) _layoutStore.SpecifiedBounds = bounds;
        SetBoundsInternal(bounds);
    }

    Size IArrangedElement.GetPreferredSize(Size proposedSize)
    {
        var length = 0;
        var thickness = 0;

        foreach (var control in _controls)
        {
            var size = control.AutoSize ? control.GetPreferredSize(proposedSize) : control.Size;
            length += Orientation == Orientation.Horizontal ? size.Width : size.Height;
            thickness = Math.Max(thickness, Orientation == Orientation.Horizontal ? size.Height : size.Width);
        }

        return Orientation == Orientation.Horizontal
            ? new Size(length + Padding.Horizontal, thickness + Padding.Vertical)
            : new Size(thickness + Padding.Horizontal, length + Padding.Vertical);
    }

    bool IArrangedElement.ParticipatesInLayout => _panel.Rows.Contains(this);

    IArrangedElement IArrangedElement.Container => _panel;

    ArrangedElementCollection IArrangedElement.Children => _children;

    LayoutSettingsStore IArrangedElement.LayoutStore => _layoutStore;

    void IArrangedElement.PerformLayout(IArrangedElement affectedElement, string? propertyName) => _panel.PerformLayout();

    private sealed class RowControlCollection : ArrangedElementCollection
    {
        internal void Add(Control control) => Items.Add(control);

        internal void Remove(Control control) => Items.Remove(control);

        internal void Clear() => Items.Clear();
    }
}
