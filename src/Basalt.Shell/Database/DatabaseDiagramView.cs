using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Basalt.Core.Localization;
using Basalt.Data.Sessions;
using Basalt.Shell.Controls;

namespace Basalt.Shell.Database;

/// <summary>
/// A connection's tables drawn as boxes with their columns, and the foreign keys between them as lines.
/// </summary>
/// <remarks>
/// Read-only for the relations: a line is drawn, not edited. The boxes move, and where they were left is kept
/// per connection in <see cref="DiagramLayoutStore"/>. Reading the tables goes through the session, which runs it
/// on its own worker, and the layout file is read and written on the pool, so the interface thread only draws.
/// </remarks>
public sealed class DatabaseDiagramView : Grid, IDatabaseDocumentContent, IDisposable
{
    private readonly DatabaseWorkspace _workspace;
    private readonly DiagramLayoutStore _store;
    private readonly DiagramCanvas _canvas = new();
    private readonly TextBlock _message;
    private readonly DispatcherTimer _saveTimer;
    private DatabaseDiagramModel _model = new([], []);
    private bool _loaded;

    public DatabaseDiagramView(DatabaseWorkspace workspace, string connectionId, DiagramLayoutStore store)
    {
        _workspace = workspace;
        _store = store;
        ConnectionId = connectionId;

        var reset = DatabaseButtons.Create(IconKind.Layout, Localizer.Get(StringKeys.DiagramResetLayout));
        reset.Click += (_, _) => ResetLayout();

        var hint = new TextBlock
        {
            Text = Localizer.Get(StringKeys.DiagramHint),
            FontSize = PanelMetrics.SecondaryFontSize,
            VerticalAlignment = VerticalAlignment.Center,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = Spacing.Normal,
            Margin = new Thickness(Spacing.Normal, Spacing.Tight),
            Children = { reset, hint },
        };

        _message = new TextBlock
        {
            FontSize = PanelMetrics.FontSize,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
        }.Themed(TextBlock.ForegroundProperty, "DescriptionForegroundBrush");

        RowDefinitions = new RowDefinitions("Auto,*");

        SetRow(toolbar, 0);
        SetRow(_canvas, 1);
        SetRow(_message, 1);
        Children.Add(toolbar);
        Children.Add(_canvas);
        Children.Add(_message);

        // A drag or a zoom changes the layout many times a second; one write follows the last of them.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _ = SaveLayoutAsync();
        };

        _canvas.LayoutChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };

        _canvas.TableActivated += (_, name) => TableDesignRequested?.Invoke(this, name);
    }

    public string ConnectionId { get; }

    /// <summary>Raised when a box is double-clicked, with its table.</summary>
    public event EventHandler<DbObjectName>? TableDesignRequested;

    /// <summary>The tab's title: the connection's name.</summary>
    public string Title => Localizer.Get(StringKeys.DiagramTitle, _workspace.Find(ConnectionId)?.Name ?? ConnectionId);

    /// <summary>The layout is kept as it changes, so closing the tab loses nothing.</summary>
    public bool HasUnsavedChanges => false;

    public Task<bool> SaveAsync() => Task.FromResult(true);

    /// <summary>What the view says instead of a diagram: nothing to draw, or why it could not be read. For the tests.</summary>
    internal string MessageText => _message.IsVisible ? _message.Text ?? "" : "";

    internal DiagramCanvas Canvas => _canvas;

    internal DatabaseDiagramModel Model => _model;

    /// <summary>Reads the tables and foreign keys, and the saved layout, and draws them.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            var session = await _workspace.OpenAsync(ConnectionId, ct).ConfigureAwait(true);

            _model = await DatabaseDiagramModel.ReadAsync(session, ct).ConfigureAwait(true);

            var saved = await _store.GetAsync(ConnectionId).ConfigureAwait(true);
            var positions = DiagramLayoutEngine.Merge(_model, saved?.Tables ?? new Dictionary<string, DiagramPoint>());

            _canvas.SetDiagram(_model, positions);

            if (saved is not null) _canvas.SetView(saved.Zoom, new Vector(saved.PanX, saved.PanY));

            _message.Text = Localizer.Get(StringKeys.DiagramEmpty);
            _message.IsVisible = _model.Tables.Count == 0;
            _loaded = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _message.Text = Localizer.Get(StringKeys.DiagramLoadFailed, Title, ex.Message);
            _message.IsVisible = true;
        }
    }

    /// <summary>Puts the boxes back where the automatic layout puts them.</summary>
    internal void ResetLayout()
    {
        if (!_loaded) return;

        _canvas.SetDiagram(_model, DiagramLayoutEngine.Auto(_model));
        _canvas.SetView(1, new Vector(DiagramCanvas.Margin, DiagramCanvas.Margin));
        _saveTimer.Stop();
        _ = SaveLayoutAsync();
    }

    /// <summary>Writes the layout now. For the tests, which would otherwise wait for the timer.</summary>
    internal Task SaveLayoutAsync() => _loaded ? _store.SaveAsync(ConnectionId, _canvas.CurrentLayout()) : Task.CompletedTask;

    public void Dispose()
    {
        _saveTimer.Stop();

        if (_loaded) _ = SaveLayoutAsync();
    }
}

/// <summary>
/// The surface the diagram is drawn on: pan with a drag on the background, zoom with Ctrl+wheel, boxes dragged, and
/// a double-click on a box.
/// </summary>
internal sealed class DiagramCanvas : Control
{
    /// <summary>The space left around the diagram when it first opens.</summary>
    public const double Margin = 24;

    private const double MinimumZoom = 0.25;
    private const double MaximumZoom = 3;
    private const double LoopOut = 24;

    private DatabaseDiagramModel _model = new([], []);
    private Dictionary<DbObjectName, DiagramPoint> _positions = [];

    // Back to front: the box clicked last is drawn last, over the others.
    private List<DiagramTable> _order = [];

    private double _zoom = 1;
    private Vector _pan = new(Margin, Margin);

    private DiagramTable? _dragging;
    private Point _dragStart;
    private DiagramPoint? _dragOrigin;
    private bool _panning;
    private Vector _panOrigin;
    private bool _moved;

    public DiagramCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    /// <summary>Raised when a box was moved or the view was panned or zoomed.</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>Raised when a box is double-clicked.</summary>
    public event EventHandler<DbObjectName>? TableActivated;

    public double Zoom => _zoom;

    public Vector Pan => _pan;

    public void SetDiagram(DatabaseDiagramModel model, IReadOnlyDictionary<DbObjectName, DiagramPoint> positions)
    {
        _model = model;
        _positions = new Dictionary<DbObjectName, DiagramPoint>(positions);
        _order = [.. model.Tables];

        InvalidateVisual();
    }

    public void SetView(double zoom, Vector pan)
    {
        _zoom = Math.Clamp(zoom, MinimumZoom, MaximumZoom);
        _pan = pan;

        InvalidateVisual();
    }

    public DiagramLayout CurrentLayout() => new(
        _positions.ToDictionary(p => p.Key.ToString(), p => p.Value), _zoom, _pan.X, _pan.Y);

    // ------------------------------------------------------------------
    // Geometry, in screen coordinates. The drawing and the tests read the same numbers.
    // ------------------------------------------------------------------

    /// <summary>The screen rectangle of a table's box.</summary>
    public Rect BoxRect(DbObjectName name)
    {
        var table = _model.Tables.First(t => t.Name == name);
        var at = _positions[name];

        return new Rect(
            at.X * _zoom + _pan.X,
            at.Y * _zoom + _pan.Y,
            DiagramGeometry.BoxWidth * _zoom,
            DiagramGeometry.BoxHeight(table) * _zoom);
    }

    /// <summary>The corners of a relation's line, in screen coordinates.</summary>
    public IReadOnlyList<Point> RelationLine(DiagramRelation relation)
    {
        var from = _model.Tables.First(t => t.Name == relation.FromTable);
        var to = _model.Tables.First(t => t.Name == relation.ToTable);

        var a = _positions[from.Name];
        var b = _positions[to.Name];

        var fromY = a.Y + DiagramGeometry.RowCentre(DiagramGeometry.RowOf(from, relation.FromColumns.FirstOrDefault() ?? ""));
        var toY = b.Y + DiagramGeometry.RowCentre(DiagramGeometry.RowOf(to, relation.ToColumns.FirstOrDefault() ?? ""));

        var width = DiagramGeometry.BoxWidth;
        var points = new List<Point>();

        if (a.X + width < b.X)
        {
            // The referencing table is left of the referenced one: out of its right edge, into the left edge.
            var middle = (a.X + width + b.X) / 2;
            points.AddRange([new(a.X + width, fromY), new(middle, fromY), new(middle, toY), new(b.X, toY)]);
        }
        else if (b.X + width < a.X)
        {
            var middle = (b.X + width + a.X) / 2;
            points.AddRange([new(a.X, fromY), new(middle, fromY), new(middle, toY), new(b.X + width, toY)]);
        }
        else
        {
            // Boxes in one column (or one box, for a key to itself): out of the right edges, round, and back in.
            var out_ = Math.Max(a.X, b.X) + width + LoopOut;
            points.AddRange([new(a.X + width, fromY), new(out_, fromY), new(out_, toY), new(b.X + width, toY)]);
        }

        return [.. points.Select(p => new Point(p.X * _zoom + _pan.X, p.Y * _zoom + _pan.Y))];
    }

    /// <summary>The colour relation lines are drawn in.</summary>
    public Color LineColour => Colour("FocusBorderBrush", Color.FromRgb(0x3B, 0x82, 0xF6));

    /// <summary>The colour behind the boxes.</summary>
    public Color BackgroundColour => Colour("EditorBackgroundBrush", Colors.White);

    /// <summary>The colour of a box's title bar.</summary>
    public Color HeaderColour => Colour("DockSurfaceHeaderActiveBrush", Color.FromRgb(0xE0, 0xE0, 0xE0));

    public IReadOnlyList<DiagramTable> DrawOrder => _order;

    // ------------------------------------------------------------------
    // Drawing
    // ------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var background = Brush("EditorBackgroundBrush", Colors.White);
        var bodyBrush = Brush("DockSurfacePanelBrush", Colors.WhiteSmoke);
        var headerBrush = Brush("DockSurfaceHeaderActiveBrush", Color.FromRgb(0xE0, 0xE0, 0xE0));
        var borderBrush = Brush("PanelBorderBrush", Colors.Gray);
        var textBrush = Brush("EditorForegroundBrush", Colors.Black);
        var quietBrush = Brush("DescriptionForegroundBrush", Colors.Gray);
        var keyBrush = Brush("WarningForegroundBrush", Colors.DarkGoldenrod);
        var linkBrush = Brush("FocusBorderBrush", Color.FromRgb(0x3B, 0x82, 0xF6));

        var surface = new Rect(Bounds.Size);

        // Painted, so the whole surface takes pointer events and a drag on the background pans.
        context.FillRectangle(background, surface);

        var linePen = new Pen(linkBrush, Math.Max(1.5, 1.5 * _zoom));

        foreach (var relation in _model.Relations)
        {
            if (!_positions.ContainsKey(relation.FromTable) || !_positions.ContainsKey(relation.ToTable)) continue;

            DrawLine(context, linePen, RelationLine(relation));
        }

        var transform = Matrix.CreateScale(_zoom, _zoom) * Matrix.CreateTranslation(_pan.X, _pan.Y);

        foreach (var table in _order)
        {
            // A box wholly off the surface costs nothing: a hundred tables are not drawn when ten are in view.
            if (!BoxRect(table.Name).Intersects(surface)) continue;

            using (context.PushTransform(transform))
                DrawBox(context, table, _positions[table.Name], bodyBrush, headerBrush, borderBrush, textBrush, quietBrush, keyBrush);
        }
    }

    private static void DrawLine(DrawingContext context, Pen pen, IReadOnlyList<Point> points)
    {
        for (var i = 1; i < points.Count; i++)
            context.DrawLine(pen, points[i - 1], points[i]);
    }

    private static void DrawBox(
        DrawingContext context,
        DiagramTable table,
        DiagramPoint at,
        IBrush body,
        IBrush header,
        IBrush border,
        IBrush text,
        IBrush quiet,
        IBrush key)
    {
        var box = new Rect(at.X, at.Y, DiagramGeometry.BoxWidth, DiagramGeometry.BoxHeight(table));
        var title = new Rect(box.X, box.Y, box.Width, DiagramGeometry.HeaderHeight);

        context.DrawRectangle(body, new Pen(border, 1), box, 4, 4);
        context.DrawRectangle(header, null, title, 4, 4);
        context.DrawLine(new Pen(border, 1), new Point(box.X, title.Bottom), new Point(box.Right, title.Bottom));

        DrawText(context, table.Name.ToString(), 12, FontWeight.SemiBold, text, new Point(box.X + 8, box.Y + 5), box.Width - 16);

        var visible = Math.Min(table.Columns.Count, DiagramGeometry.MaxVisibleColumns);

        for (var i = 0; i < visible; i++)
        {
            var column = table.Columns[i];
            var top = box.Y + DiagramGeometry.HeaderHeight + i * DiagramGeometry.RowHeight;

            if (column.IsPrimaryKey)
                DrawText(context, "PK", 9, FontWeight.Bold, key, new Point(box.X + 6, top + 3), 18);
            else if (column.IsForeignKey)
                DrawText(context, "FK", 9, FontWeight.Bold, quiet, new Point(box.X + 6, top + 3), 18);

            DrawText(context, column.Name, 11, FontWeight.Normal, text, new Point(box.X + 28, top + 2), 96);
            DrawText(context, column.DataType, 10, FontWeight.Normal, quiet, new Point(box.X + 126, top + 3), box.Right - box.X - 132);
        }

        if (table.Columns.Count > visible)
        {
            var top = box.Y + DiagramGeometry.HeaderHeight + visible * DiagramGeometry.RowHeight;

            DrawText(context, "… " + (table.Columns.Count - visible), 10, FontWeight.Normal, quiet, new Point(box.X + 28, top + 3), 120);
        }
    }

    private static void DrawText(DrawingContext context, string value, double size, FontWeight weight, IBrush brush, Point at, double maxWidth)
    {
        var formatted = new FormattedText(
            value,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight),
            size,
            brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxTextHeight = size * 1.5,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        context.DrawText(formatted, at);
    }

    private IBrush Brush(string key, Color fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private Color Colour(string key, Color fallback) =>
        Brush(key, fallback) is ISolidColorBrush solid ? solid.Color : fallback;

    // ------------------------------------------------------------------
    // Pointer
    // ------------------------------------------------------------------

    /// <summary>The topmost box under a point of the surface, or null.</summary>
    public DiagramTable? TableAt(Point screen)
    {
        for (var i = _order.Count - 1; i >= 0; i--)
        {
            if (_positions.ContainsKey(_order[i].Name) && BoxRect(_order[i].Name).Contains(screen)) return _order[i];
        }

        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);

        if (!point.Properties.IsLeftButtonPressed) return;

        Focus();

        var at = point.Position;
        var table = TableAt(at);

        if (table is not null && e.ClickCount >= 2)
        {
            TableActivated?.Invoke(this, table.Name);
            e.Handled = true;
            return;
        }

        _moved = false;

        if (table is not null)
        {
            // Brought to the front, as a window is.
            _order.Remove(table);
            _order.Add(table);

            _dragging = table;
            _dragStart = at;
            _dragOrigin = _positions[table.Name];
            InvalidateVisual();
        }
        else
        {
            _panning = true;
            _dragStart = at;
            _panOrigin = _pan;
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var at = e.GetPosition(this);

        if (_dragging is not null && _dragOrigin is not null)
        {
            var delta = at - _dragStart;

            _positions[_dragging.Name] = new DiagramPoint(_dragOrigin.X + delta.X / _zoom, _dragOrigin.Y + delta.Y / _zoom);
            _moved = true;
            InvalidateVisual();
        }
        else if (_panning)
        {
            _pan = _panOrigin + (at - _dragStart);
            _moved = true;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var changed = _moved && (_dragging is not null || _panning);

        _dragging = null;
        _dragOrigin = null;
        _panning = false;
        _moved = false;

        e.Pointer.Capture(null);

        if (changed) LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ZoomAt(e.GetPosition(this), Math.Pow(1.0015, e.Delta.Y * 120));
        }
        else
        {
            // The wheel alone scrolls, as it does in any other document.
            _pan += new Vector(e.Delta.X * 40, e.Delta.Y * 40);
            InvalidateVisual();
            LayoutChanged?.Invoke(this, EventArgs.Empty);
        }

        e.Handled = true;
    }

    /// <summary>Zooms by a factor around a point of the surface, which stays under the pointer.</summary>
    public void ZoomAt(Point screen, double factor)
    {
        var next = Math.Clamp(_zoom * factor, MinimumZoom, MaximumZoom);

        if (Math.Abs(next - _zoom) < 1e-9) return;

        // The diagram point under the pointer before and after must be the same one.
        var world = new Point((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

        _zoom = next;
        _pan = new Vector(screen.X - world.X * _zoom, screen.Y - world.Y * _zoom);

        InvalidateVisual();
        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
