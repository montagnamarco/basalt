using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Layout;

namespace System.Windows.Forms;

/// <summary>
/// A dock-able area, usually along one edge of a <see cref="ToolStripContainer"/>,
/// that holds <see cref="ToolStrip"/>s the user can drag between it and other
/// panels: each strip lives in a <see cref="ToolStripPanelRow"/>, and the
/// rows stack along whichever axis <see cref="Orientation"/> is not.
/// </summary>
[Designer("System.Windows.Forms.Design.ToolStripPanelDesigner, System.Design, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
public partial class ToolStripPanel : ContainerControl
{
    private readonly ToolStripPanelRowCollection _rows;
    private Orientation _orientation = Orientation.Horizontal;
    private Padding _rowMargin = new(0, 0, 0, 3);
    private ToolStripRenderer? _renderer;
    private ToolStripRenderMode _renderMode = ToolStripRenderMode.ManagerRenderMode;

    private static readonly ToolStripSystemRenderer SystemRendererInstance = new();
    private static readonly ToolStripProfessionalRenderer ProfessionalRendererInstance = new();

    /// <summary>Which row <see cref="Join(ToolStrip,int)"/> is about to put the next added control in, consumed by <see cref="OnControlAdded"/>.</summary>
    private int? _pendingRow;

    public ToolStripPanel()
    {
        _rows = new ToolStripPanelRowCollection(this);
        AutoSize = true;
        Dock = DockStyle.Top;
    }

    protected override Padding DefaultMargin => Padding.Empty;

    protected override Padding DefaultPadding => Padding.Empty;

    public override LayoutEngine LayoutEngine => ToolStripPanelLayoutEngine.Instance;

    [DefaultValue(false)]
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public bool Locked { get; set; }

    /// <summary>
    /// Whether rows stack top to bottom (strips side by side within one) or
    /// side by side (strips stacked within one). Docking Top/Bottom sets this
    /// to Horizontal, Left/Right to Vertical; it can also be set directly.
    /// </summary>
    public Orientation Orientation
    {
        get => _orientation;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Orientation));
            if (value == _orientation) return;

            _orientation = value;
            foreach (var row in _rows.RowsInOrder) row.NotifyOrientationChanged();
            PerformLayout();
        }
    }

    public ToolStripRenderMode RenderMode
    {
        get => _renderMode;
        set
        {
            if (value == ToolStripRenderMode.Custom) throw new NotSupportedException("RenderMode.Custom is applied automatically when Renderer is set; set Renderer instead.");
            if (value == _renderMode) return;

            _renderMode = value;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    [Browsable(false)]
    public ToolStripRenderer Renderer
    {
        get => _renderMode switch
        {
            ToolStripRenderMode.System => SystemRendererInstance,
            ToolStripRenderMode.Professional => ProfessionalRendererInstance,
            ToolStripRenderMode.Custom => _renderer ?? SystemRendererInstance,
            _ => ToolStripManager.Renderer,
        };
        set
        {
            _renderer = value;
            _renderMode = ToolStripRenderMode.Custom;
            OnRendererChanged(EventArgs.Empty);
            Invalidate(true);
        }
    }

    public Padding RowMargin
    {
        get => _rowMargin;
        set
        {
            if (value == _rowMargin) return;

            _rowMargin = value;
            PerformLayout();
        }
    }

    /// <summary>The panel's rows, in the order they stack; a defensive copy, as Microsoft's own is.</summary>
    [Browsable(false)]
    public ToolStripPanelRow[] Rows => _rows.ToArray();

    public event EventHandler? RendererChanged;

    public void BeginInit() => SuspendLayout();

    public void EndInit() => ResumeLayout(true);

    protected override Control.ControlCollection CreateControlsInstance() => new(this);

    /// <summary>Puts the strip in the last row, creating one if the panel is still empty.</summary>
    public void Join(ToolStrip toolStripToDrag) => Join(toolStripToDrag, _rows.Count > 0 ? _rows.Count - 1 : CreateRow());

    /// <summary>Puts the strip in the row at the given index, creating empty rows up to it if needed.</summary>
    public void Join(ToolStrip toolStripToDrag, int row)
    {
        ArgumentNullException.ThrowIfNull(toolStripToDrag);
        if (row < 0) throw new ArgumentOutOfRangeException(nameof(row), row, "Row index must not be negative.");

        // Drop the strip from wherever it already was first, so creating the target row below never finds (and removes) it empty.
        foreach (var existing in _rows.RowsInOrder) existing.RemoveControlInternal(toolStripToDrag);
        DropEmptyRows();

        while (row >= _rows.Count) CreateRow();

        _pendingRow = row;

        if (toolStripToDrag.Parent != this) Controls.Add(toolStripToDrag);
        else _rows[_pendingRow.Value].AddControlInternal(toolStripToDrag);

        _pendingRow = null;
        PerformLayout();
    }

    public void Join(ToolStrip toolStripToDrag, Point location) => Join(toolStripToDrag, RowIndexFromPoint(location));

    public void Join(ToolStrip toolStripToDrag, int x, int y) => Join(toolStripToDrag, new Point(x, y));

    /// <summary>The row whose band contains the point, or the nearest one along the cross axis if none does.</summary>
    public ToolStripPanelRow PointToRow(Point clientLocation)
    {
        foreach (var row in _rows.RowsInOrder)
        {
            if (row.Bounds.Contains(clientLocation)) return row;
        }

        return _rows.Count > 0 ? _rows[^1] : _rows[CreateRow()];
    }

    private int RowIndexFromPoint(Point location)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Bounds.Contains(location)) return i;
        }

        return _rows.Count > 0 ? _rows.Count - 1 : CreateRow();
    }

    private int CreateRow()
    {
        _rows.Add(new ToolStripPanelRow(this) { Margin = _rowMargin });

        return _rows.Count - 1;
    }

    private void DropEmptyRows()
    {
        // Removing a row performs layout, and layout callbacks may change the
        // collection again. Walk a snapshot so later rows remain addressable.
        var rows = _rows.ToArray();
        for (var i = rows.Length - 1; i >= 0; i--)
        {
            if (rows[i].IsEmpty) _rows.Remove(rows[i]);
        }
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);

        if (e.Control is null) return;

        var rowIndex = _pendingRow ?? (_rows.Count > 0 ? _rows.Count - 1 : CreateRow());
        _rows[rowIndex].AddControlInternal(e.Control);

        DoLayout(e.Control, PropertyNames.Parent);
    }

    protected override void OnControlRemoved(ControlEventArgs e)
    {
        base.OnControlRemoved(e);

        if (e.Control is null) return;

        foreach (var row in _rows.RowsInOrder) row.RemoveControlInternal(e.Control);
        DropEmptyRows();

        DoLayout(e.Control, PropertyNames.Parent);
    }

    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);

        Orientation = Dock is DockStyle.Left or DockStyle.Right ? Orientation.Vertical : Orientation.Horizontal;
    }

    protected virtual void OnRendererChanged(EventArgs e) => RendererChanged?.Invoke(this, e);

    /// <summary>Lays every row out along the cross axis, and every row's strips out along the main axis.</summary>
    internal void PerformRowLayout()
    {
        var display = DisplayRectangle;
        var cursor = Orientation == Orientation.Horizontal ? display.Top : display.Left;

        foreach (var row in _rows.RowsInOrder)
        {
            var thickness = RowThickness(row);

            var bounds = Orientation == Orientation.Horizontal
                ? new Rectangle(display.Left, cursor, display.Width, thickness)
                : new Rectangle(cursor, display.Top, thickness, display.Height);

            row.SetBoundsInternal(bounds);
            LayoutRowControls(row);

            cursor += thickness + (Orientation == Orientation.Horizontal ? row.Margin.Vertical : row.Margin.Horizontal);
        }
    }

    private void LayoutRowControls(ToolStripPanelRow row)
    {
        var area = row.DisplayRectangle;
        var cursor = Orientation == Orientation.Horizontal ? area.Left : area.Top;

        foreach (var control in row.Controls)
        {
            var size = control.AutoSize ? control.GetPreferredSize(Size.Empty) : control.Size;

            var bounds = Orientation == Orientation.Horizontal
                ? new Rectangle(cursor, area.Top, size.Width, area.Height)
                : new Rectangle(area.Left, cursor, area.Width, size.Height);

            control.Bounds = bounds;
            cursor += Orientation == Orientation.Horizontal ? bounds.Width : bounds.Height;
        }
    }

    private static int RowThickness(ToolStripPanelRow row)
    {
        var thickness = 0;

        foreach (var control in row.Controls)
        {
            var size = control.AutoSize ? control.GetPreferredSize(Size.Empty) : control.Size;
            thickness = Math.Max(thickness, row.Orientation == Orientation.Horizontal ? size.Height : size.Width);
        }

        return thickness;
    }

    internal override Size GetPreferredSizeCore(Size proposedSize)
    {
        var length = 0;
        var cross = 0;

        foreach (var row in _rows.RowsInOrder)
        {
            var thickness = RowThickness(row);
            length += thickness;
            cross = Math.Max(cross, RowLength(row));
        }

        var padding = Padding;

        return Orientation == Orientation.Horizontal
            ? new Size(cross + padding.Horizontal, length + padding.Vertical)
            : new Size(length + padding.Horizontal, cross + padding.Vertical);
    }

    private static int RowLength(ToolStripPanelRow row)
    {
        var length = 0;

        foreach (var control in row.Controls)
        {
            var size = control.AutoSize ? control.GetPreferredSize(Size.Empty) : control.Size;
            length += row.Orientation == Orientation.Horizontal ? size.Width : size.Height;
        }

        return length;
    }
}

/// <summary>The panel's own arrangement: rows stacked along the cross axis, each row's strips stacked along the main one.</summary>
internal sealed class ToolStripPanelLayoutEngine : LayoutEngine
{
    public static readonly ToolStripPanelLayoutEngine Instance = new();

    public override void InitLayout(object child, BoundsSpecified specified)
    {
    }

    public override bool Layout(object container, LayoutEventArgs layoutEventArgs)
    {
        if (container is not ToolStripPanel panel) throw new NotSupportedException($"ToolStripPanelLayoutEngine cannot lay out {container?.GetType()}.");

        if (panel.AutoSize)
        {
            var preferred = panel.GetPreferredSize(Size.Empty);
            if (preferred != panel.Size) panel.Size = preferred;
        }

        panel.PerformRowLayout();

        return false;
    }
}
