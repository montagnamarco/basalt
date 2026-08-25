using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Basalt.Core.Localization;
using Basalt.Designer;
using Basalt.Designer.Model;

namespace Basalt.Shell.Controls;

/// <summary>
/// Design surface: shows the preview rendered by the real Avalonia engine and
/// overlays the selection indicators on top of it.
///
/// The rendered control is matched to its XAML element by position in the tree:
/// the renderer produces a visual tree with the same shape as the XML document,
/// so walking both in parallel makes it possible to go from the clicked element
/// back to the line of XAML that has to be edited.
/// </summary>
public sealed class DesignSurface : ContentControl
{
    /// <summary>The eight places a resize handle sits.</summary>
    private enum Handle
    {
        TopLeft, Top, TopRight,
        Left, Right,
        BottomLeft, Bottom, BottomRight,
    }

    /// <summary>What a press started, until the pointer is released.</summary>
    private sealed class Drag
    {
        public required Point Origin { get; init; }
        public required Rect Bounds { get; init; }

        /// <summary>Which handle was grabbed, or none when the body was.</summary>
        public Handle? Handle { get; init; }

        /// <summary>How far the pointer has travelled since the press.</summary>
        public Vector Delta { get; set; }

        /// <summary>Whether it has moved far enough to be a drag at all.</summary>
        public bool Started { get; set; }

        /// <summary>The alignment lines to draw, from the last snap.</summary>
        public IReadOnlyList<AlignmentGuides.Guide> Guides { get; set; } = [];
    }

    /// <summary>
    /// How far the pointer must travel before a press becomes a drag.
    /// </summary>
    /// <remarks>
    /// Without a threshold every click nudges the control by a pixel or two,
    /// because a hand on a trackpad never presses and releases at exactly the
    /// same point.
    /// </remarks>
    private const double DragThreshold = 3;

    /// <summary>How far an arrow key moves the selection.</summary>
    private const double NudgeStep = 1;

    /// <summary>How far Shift and an arrow key move it.</summary>
    private const double NudgeStride = 10;

    private readonly Canvas _adorners = new() { IsHitTestVisible = false };
    private readonly Dictionary<Control, XElement> _controlToElement = [];
    private DesignerSession? _session;
    private Drag? _drag;

    /// <summary>The container a dragged control would land in.</summary>
    private XElement? _dropTarget;

    /// <summary>Where a rubber-band selection started, while one is running.</summary>
    private Point? _bandOrigin;
    private Point _bandTo;

    public static readonly StyledProperty<DesignerSession?> SessionProperty =
        AvaloniaProperty.Register<DesignSurface, DesignerSession?>(nameof(Session));

    public DesignerSession? Session
    {
        get => GetValue(SessionProperty);
        set => SetValue(SessionProperty, value);
    }

    public event EventHandler<XElement?>? SelectionChanged;

    public DesignSurface()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;

        // Focusable so the arrow keys reach it: a surface that cannot take
        // focus is a surface where the keyboard does nothing, and the user is
        // left clicking and dragging for a one-pixel adjustment.
        Focusable = true;

        // Handled during the tunnel, before the preview sees it: the controls
        // being designed are live, so a Button in the preview swallows the
        // press and the surface never learns it was clicked. At design time
        // the click means "select this", not "press this".
        AddHandler(PointerPressedEvent, TunnelPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, TunnelMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, TunnelReleased, RoutingStrategies.Tunnel);

        // Tab as well: the framework moves focus on it before a bubbling
        // handler is reached, so the surface never saw it and only Shift+Tab
        // worked — which reads as half a feature rather than a routing quirk.
        AddHandler(KeyDownEvent, TunnelKeyDown, RoutingStrategies.Tunnel);

        // Controls dragged from the toolbox land here.
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    private void TunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Tab) OnKeyDown(e);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(ToolboxPanel.DragFormat)
            ? DragDropEffects.Copy
            : DragDropEffects.None;

        // The container that would take it, outlined while the pointer is
        // over it: without this a drop is a guess about where the control
        // will end up, and a nested panel is easy to miss by a few pixels.
        _dropTarget = e.DragEffects == DragDropEffects.Copy
            ? ContainerAt(e.GetPosition(this))
            : null;

        DrawSelectionAdorner();

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _dropTarget = null;

        if (_session is null) return;
        if (e.DataTransfer.TryGetValue(ToolboxPanel.DragFormat) is not { } item) return;

        var at = e.GetPosition(this);
        var container = ContainerAt(at);

        var inserted = _session.InsertFromToolbox(item, container);

        // Dropped where the pointer was, not at the container's origin: a
        // control that lands in the corner however carefully it was placed
        // makes the gesture pointless.
        if (container is not null && BoundsOf(container) is { } bounds)
        {
            var inside = at - bounds.Position;

            _session.Select(inserted);
            _session.MoveSelection(Math.Round(inside.X), Math.Round(inside.Y));
        }

        SelectionChanged?.Invoke(this, _session.Selection);

        DrawSelectionAdorner();

        e.Handled = true;
    }

    private void TunnelPressed(object? sender, PointerPressedEventArgs e) =>
        OnPointerPressed(e);

    private void TunnelMoved(object? sender, PointerEventArgs e) => OnPointerMoved(e);

    private void TunnelReleased(object? sender, PointerReleasedEventArgs e) =>
        OnPointerReleased(e);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SessionProperty)
        {
            if (_session is not null) _session.DocumentModified -= OnDocumentModified;
            _session = change.GetNewValue<DesignerSession?>();
            if (_session is not null) _session.DocumentModified += OnDocumentModified;
            Refresh();
        }
    }

    private void OnDocumentModified(object? sender, EventArgs e) => Refresh();

    /// <summary>Rebuilds the preview from the current document.</summary>
    public void Refresh()
    {
        _controlToElement.Clear();
        _adorners.Children.Clear();

        if (_session is null)
        {
            Content = null;
            return;
        }

        var result = _session.Render();

        if (!result.Succeeded)
        {
            // Never an empty message: a failure with nothing to say leaves a
            // blank surface, which reads as the designer being broken rather
            // than as this file not being renderable.
            Content = new TextBlock
            {
                Text = result.Error is { Length: > 0 } reason
                    ? reason
                    : Localizer.Get(StringKeys.DesignerCannotRender),
                Foreground = Brushes.OrangeRed,
                Margin = new Thickness(16),
                TextWrapping = TextWrapping.Wrap
            };
            return;
        }

        var rootElement = XamlDocument.ControlChildren(_session.Document.Root).FirstOrDefault()
                          ?? _session.Document.Root;
        MapControls(result.Root!, rootElement);

        // Detached from the previous overlay first: a visual belongs to one
        // parent, and adding it to a second throws. Nothing caught it while
        // the surface only ever rebuilt on a document change from elsewhere;
        // moving a control refreshes and rebuilds in the same breath.
        (_adorners.Parent as Panel)?.Children.Remove(_adorners);

        var overlay = new Panel();
        overlay.Children.Add(result.Root!);
        overlay.Children.Add(_adorners);
        Content = overlay;

        DrawSelectionAdorner();
    }

    /// <summary>
    /// Walks the visual tree and the XML tree in parallel to link them together.
    /// The two trees share the same structure because the former was produced
    /// from the latter.
    /// </summary>
    private void MapControls(Control control, XElement element)
    {
        _controlToElement[control] = element;

        var xmlChildren = XamlDocument.ControlChildren(element).ToList();
        var visualChildren = VisualChildrenOf(control).ToList();

        for (var i = 0; i < Math.Min(xmlChildren.Count, visualChildren.Count); i++)
            MapControls(visualChildren[i], xmlChildren[i]);
    }

    /// <summary>
    /// Direct logical children of a control. The logical model is used rather
    /// than the visual one because the latter also contains template-generated
    /// controls, which have no counterpart in the user's XAML.
    /// </summary>
    private static IEnumerable<Control> VisualChildrenOf(Control control) => control switch
    {
        Panel panel => panel.Children.OfType<Control>(),
        ContentControl { Content: Control child } => [child],
        Decorator { Child: Control child } => [child],
        ItemsControl items => items.Items.OfType<Control>(),
        _ => []
    };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        if (_session is null) return;

        Focus();

        var at = e.GetPosition(this);

        // A press on a handle of the current selection resizes it; anything
        // else selects first. Checked before hit-testing because the handles
        // sit outside the control's own bounds and would otherwise select
        // whatever is behind them.
        if (SelectionBounds() is { } bounds && HandleAt(bounds, at) is { } handle)
        {
            _drag = new Drag { Origin = at, Bounds = bounds, Handle = handle };
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Walk up from the hit control to the first one that maps to a XAML
        // element: the hit may land on a part of the template.
        var hit = e.Source as Control;
        while (hit is not null && !_controlToElement.ContainsKey(hit))
            hit = hit.Parent as Control;

        var element = hit is null ? null : _controlToElement[hit];

        // Shift adds to the selection rather than replacing it, which is how
        // several controls get aligned to one another.
        if (element is not null && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            _session.ToggleSelection(element);
        else
            _session.Select(element);

        SelectionChanged?.Invoke(this, _session.Selection);

        if (element is null)
        {
            // A press on empty space starts a rubber band: dragging over
            // several controls is how a group gets picked without clicking
            // each one with Shift held.
            _bandOrigin = at;
            _bandTo = at;
            e.Pointer.Capture(this);
        }
        else if (SelectionBounds() is { } selected)
        {
            _drag = new Drag { Origin = at, Bounds = selected };
            e.Pointer.Capture(this);
        }

        DrawSelectionAdorner();
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        LastPointerPosition = e.GetPosition(this);

        if (_bandOrigin is not null)
        {
            _bandTo = e.GetPosition(this);
            DrawSelectionAdorner();
            e.Handled = true;
            return;
        }

        if (_drag is null) return;

        _drag.Delta = e.GetPosition(this) - _drag.Origin;

        // Below the threshold nothing has happened yet: a hand on a trackpad
        // never presses and releases at the same point, and without this
        // every click nudged the control a pixel.
        if (!_drag.Started &&
            Math.Abs(_drag.Delta.X) < DragThreshold &&
            Math.Abs(_drag.Delta.Y) < DragThreshold)
            return;

        _drag.Started = true;

        // Alt places a control exactly where the pointer is: snapping that
        // cannot be turned off is snapping that stops you putting something
        // where you meant to.
        var snapping = !e.KeyModifiers.HasFlag(KeyModifiers.Alt);

        if (_drag.Handle is null) SnapDrag(_drag, snapping);

        DrawSelectionAdorner();
        e.Handled = true;
    }

    /// <summary>
    /// Adjusts a move so the control lines up with its neighbours.
    /// </summary>
    /// <remarks>
    /// Only for moves: a resize snapping to a neighbour changes the size by an
    /// amount the user did not ask for, which is much harder to notice than a
    /// position being nudged.
    /// </remarks>
    private void SnapDrag(Drag drag, bool snapping)
    {
        var moved = drag.Bounds.Translate(drag.Delta);

        var snap = AlignmentGuides.SnapTo(
            new AlignmentGuides.Box(moved.X, moved.Y, moved.Width, moved.Height),
            NeighbourBoxes(),
            snapping);

        drag.Delta = new Vector(
            snap.X - drag.Bounds.X,
            snap.Y - drag.Bounds.Y);

        drag.Guides = snap.Guides;
    }

    /// <summary>Where every control other than the selected one sits.</summary>
    private IReadOnlyList<AlignmentGuides.Box> NeighbourBoxes()
    {
        if (Content is not Panel overlay) return [];

        var boxes = new List<AlignmentGuides.Box>();

        foreach (var (control, element) in _controlToElement)
        {
            if (ReferenceEquals(element, _session?.Selection)) continue;

            // The root is the surface itself: aligning to its edges is what
            // makes a control sit flush with the window.
            var origin = control.TranslatePoint(default, overlay);
            if (origin is null) continue;

            boxes.Add(new AlignmentGuides.Box(
                origin.Value.X, origin.Value.Y,
                control.Bounds.Width, control.Bounds.Height));
        }

        return boxes;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        if (_bandOrigin is { } origin)
        {
            _bandOrigin = null;
            e.Pointer.Capture(null);

            SelectWithin(Band(origin, _bandTo));

            DrawSelectionAdorner();
            e.Handled = true;
            return;
        }

        if (_drag is null) return;

        var drag = _drag;
        _drag = null;

        e.Pointer.Capture(null);

        if (drag.Started) Commit(drag);

        DrawSelectionAdorner();
        e.Handled = true;
    }

    /// <summary>Writes a finished drag into the document.</summary>
    private void Commit(Drag drag)
    {
        if (_session is null) return;

        if (drag.Handle is null)
        {
            _session.MoveSelection(drag.Delta.X, drag.Delta.Y);
            return;
        }

        var to = Preview(drag.Bounds, drag);

        _session.ResizeSelection(
            to.Width, to.Height,
            to.X - drag.Bounds.X, to.Y - drag.Bounds.Y);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_session is null) return;

        // Escape abandons a drag in progress without writing anything: a drag
        // that has gone somewhere unintended should not have to be undone.
        if (e.Key == Key.Escape && _drag is not null)
        {
            _drag = null;
            DrawSelectionAdorner();
            e.Handled = true;
            return;
        }

        // Tab walks the controls in document order, which is the order they
        // are written and the order a reader of the XAML expects.
        if (e.Key == Key.Tab)
        {
            StepSelection(forwards: !e.KeyModifiers.HasFlag(KeyModifiers.Shift));
            e.Handled = true;
            return;
        }

        if (_session.Selection is null) return;

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NudgeStride : NudgeStep;

        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0d),
            Key.Right => (step, 0d),
            Key.Up => (0d, -step),
            Key.Down => (0d, step),
            _ => (0d, 0d),
        };

        if (dx == 0 && dy == 0)
        {
            if (e.Key is Key.Delete or Key.Back)
            {
                _session.RemoveSelected();
                SelectionChanged?.Invoke(this, null);
                e.Handled = true;
            }

            return;
        }

        _session.MoveSelection(dx, dy);
        e.Handled = true;
    }

    /// <summary>
    /// Selects every control the rubber band covers.
    /// </summary>
    /// <remarks>
    /// Wholly covered, not merely touched: a band drawn across a busy layout
    /// brushes half of it, and picking up everything it grazed is never what
    /// was meant. Visual Studio draws the same line.
    /// </remarks>
    private void SelectWithin(Rect band)
    {
        if (_session is null) return;

        // A band of nothing is a click on empty space, which clears.
        if (band.Width < DragThreshold && band.Height < DragThreshold)
        {
            _session.Select(null);
            SelectionChanged?.Invoke(this, null);
            return;
        }

        var caught = _session.Document.Root.Descendants()
            .Where(_controlToElement.ContainsValue)
            .Where(element => BoundsOf(element) is { } bounds && band.Contains(bounds))
            .ToList();

        _session.SelectMany(caught);
        SelectionChanged?.Invoke(this, _session.Selection);
    }

    /// <summary>
    /// The container under a point, for dropping a new control into.
    /// </summary>
    /// <remarks>
    /// The innermost one, so dragging into a panel nested in another puts the
    /// control where the pointer is rather than in the outermost thing that
    /// happens to accept children. Null when the point is over nothing, which
    /// means the root.
    /// </remarks>
    public XElement? ContainerAt(Point point)
    {
        if (_session is null || Content is not Panel overlay) return null;

        XElement? innermost = null;
        var smallest = double.MaxValue;

        foreach (var (control, element) in _controlToElement)
        {
            var origin = control.TranslatePoint(default, overlay);
            if (origin is null) continue;

            var bounds = new Rect(origin.Value, control.Bounds.Size);

            if (!bounds.Contains(point)) continue;

            // Smallest wins: a nested panel is inside its parent, and both
            // contain the point.
            var area = bounds.Width * bounds.Height;
            if (area >= smallest) continue;

            smallest = area;
            innermost = element;
        }

        return innermost;
    }

    /// <summary>Where the pointer last was, for a drop.</summary>
    public Point LastPointerPosition { get; private set; }

    /// <summary>Selects the next or previous control in document order.</summary>
    private void StepSelection(bool forwards)
    {
        if (_session is null) return;

        // Document order rather than the order they were mapped: a dictionary
        // has no order worth relying on, and Tab jumping about at random is
        // worse than Tab doing nothing.
        var elements = _session.Document.Root.Descendants()
            .Where(_controlToElement.ContainsValue)
            .ToList();

        if (elements.Count == 0) return;

        var current = _session.Selection is null
            ? -1
            : elements.FindIndex(e => ReferenceEquals(e, _session.Selection));

        var next = current < 0
            ? (forwards ? 0 : elements.Count - 1)
            : (current + (forwards ? 1 : -1) + elements.Count) % elements.Count;

        _session.Select(elements[next]);
        SelectionChanged?.Invoke(this, elements[next]);

        DrawSelectionAdorner();
    }

    /// <summary>Draws the frame and handles around the selected controls.</summary>
    private void DrawSelectionAdorner()
    {
        _adorners.Children.Clear();

        DrawBand();
        DrawDropTarget();

        // The ones that are not the primary get a plain outline: handles on
        // every selected control would suggest each can be resized on its
        // own, and a resize acts on one at a time.
        foreach (var element in _session?.SelectedElements ?? [])
        {
            if (ReferenceEquals(element, _session?.Selection)) continue;

            if (BoundsOf(element) is { } other)
                _adorners.Children.Add(new Border
                {
                    BorderBrush = Brushes.DodgerBlue,
                    BorderThickness = new Thickness(1),
                    Width = other.Width,
                    Height = other.Height,
                    Opacity = 0.6,
                    IsHitTestVisible = false,
                    [Canvas.LeftProperty] = other.X,
                    [Canvas.TopProperty] = other.Y,
                });
        }

        if (SelectionBounds() is not { } bounds) return;

        // Offset by the drag so far, so the outline follows the pointer while
        // the button is down: without it the control appears to stay put until
        // the drag ends, and there is no telling where it will land.
        if (_drag is { Started: true } drag) bounds = Preview(bounds, drag);

        var frame = new Border
        {
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(2),
            Width = bounds.Width,
            Height = bounds.Height,
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(frame, bounds.X);
        Canvas.SetTop(frame, bounds.Y);
        _adorners.Children.Add(frame);

        foreach (var handle in Enum.GetValues<Handle>())
            _adorners.Children.Add(DrawHandle(bounds, handle));

        if (_drag is { Started: true } current)
            foreach (var guide in current.Guides)
                _adorners.Children.Add(DrawGuide(guide));
    }

    /// <summary>
    /// The rectangle between two points, whichever way round they are.
    /// </summary>
    /// <remarks>
    /// Rect's two-point constructor keeps the sign, so a band dragged up or
    /// to the left comes out with a negative width — and Avalonia throws on a
    /// negative Width rather than clamping it, which crashed the application
    /// mid-drag. Dragging down and right, which is what the tests did, never
    /// reached it.
    /// </remarks>
    private static Rect Band(Point from, Point to) => new(
        Math.Min(from.X, to.X),
        Math.Min(from.Y, to.Y),
        Math.Abs(to.X - from.X),
        Math.Abs(to.Y - from.Y));

    /// <summary>Outlines the container a dragged control would land in.</summary>
    private void DrawDropTarget()
    {
        if (_dropTarget is not { } target) return;
        if (BoundsOf(target) is not { } bounds) return;

        var outline = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            BorderBrush = Brushes.MediumSeaGreen,
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(20, 60, 179, 113)),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);

        _adorners.Children.Add(outline);
    }

    /// <summary>Draws the rubber band, when one is being dragged.</summary>
    private void DrawBand()
    {
        if (_bandOrigin is not { } origin) return;

        var band = Band(origin, _bandTo);

        var outline = new Border
        {
            Width = band.Width,
            Height = band.Height,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(30, 30, 144, 255)),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(outline, band.X);
        Canvas.SetTop(outline, band.Y);

        _adorners.Children.Add(outline);
    }

    /// <summary>Draws one alignment line across the surface.</summary>
    private Control DrawGuide(AlignmentGuides.Guide guide)
    {
        var line = new Border
        {
            Background = Brushes.Crimson,
            Width = guide.IsVertical ? 1 : Bounds.Width,
            Height = guide.IsVertical ? Bounds.Height : 1,
            IsHitTestVisible = false,
            Opacity = 0.7,
        };

        Canvas.SetLeft(line, guide.IsVertical ? guide.At : 0);
        Canvas.SetTop(line, guide.IsVertical ? 0 : guide.At);

        return line;
    }

    /// <summary>Where the primary selection sits, in surface coordinates.</summary>
    private Rect? SelectionBounds() =>
        _session?.Selection is { } selection ? BoundsOf(selection) : null;

    /// <summary>Where an element is drawn, in surface coordinates.</summary>
    private Rect? BoundsOf(XElement element)
    {
        if (Content is not Panel overlay) return null;

        var control = _controlToElement
            .FirstOrDefault(pair => ReferenceEquals(pair.Value, element)).Key;

        if (control is null) return null;

        var origin = control.TranslatePoint(default, overlay);

        return origin is null ? null : new Rect(origin.Value, control.Bounds.Size);
    }

    /// <summary>
    /// Lines the selected controls up with the last one picked.
    /// </summary>
    /// <remarks>
    /// Measured here because only the surface knows what the layout did with
    /// them: a control with no Margin still sits somewhere.
    /// </remarks>
    public void Align(AlignmentCommand command)
    {
        if (_session is null) return;

        var boxes = new List<AlignmentArithmetic.Box>();

        foreach (var element in _session.SelectedElements)
        {
            if (BoundsOf(element) is not { } bounds) return;

            boxes.Add(new AlignmentArithmetic.Box(
                bounds.X, bounds.Y, bounds.Width, bounds.Height));
        }

        _session.AlignSelection(boxes, command);
    }

    /// <summary>Where the control would land if the drag ended now.</summary>
    private static Rect Preview(Rect bounds, Drag drag)
    {
        if (drag.Handle is not { } handle) return bounds.Translate(drag.Delta);

        var (dx, dy) = (drag.Delta.X, drag.Delta.Y);

        // A top or left handle moves the origin as well as changing the size;
        // the others only change the size.
        var left = handle is Handle.TopLeft or Handle.Left or Handle.BottomLeft ? dx : 0;
        var top = handle is Handle.TopLeft or Handle.Top or Handle.TopRight ? dy : 0;

        var width = bounds.Width + handle switch
        {
            Handle.TopRight or Handle.Right or Handle.BottomRight => dx,
            Handle.TopLeft or Handle.Left or Handle.BottomLeft => -dx,
            _ => 0,
        };

        var height = bounds.Height + handle switch
        {
            Handle.BottomLeft or Handle.Bottom or Handle.BottomRight => dy,
            Handle.TopLeft or Handle.Top or Handle.TopRight => -dy,
            _ => 0,
        };

        return new Rect(
            bounds.X + left, bounds.Y + top,
            Math.Max(0, width), Math.Max(0, height));
    }

    /// <summary>How big a resize handle is drawn.</summary>
    private const double HandleSize = 7;

    private Control DrawHandle(Rect bounds, Handle handle)
    {
        var square = new Border
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = Brushes.White,
            BorderBrush = Brushes.DodgerBlue,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };

        var at = HandleCentre(bounds, handle);

        Canvas.SetLeft(square, at.X - HandleSize / 2);
        Canvas.SetTop(square, at.Y - HandleSize / 2);

        return square;
    }

    private static Point HandleCentre(Rect bounds, Handle handle) => handle switch
    {
        Handle.TopLeft => bounds.TopLeft,
        Handle.Top => new Point(bounds.Center.X, bounds.Top),
        Handle.TopRight => bounds.TopRight,
        Handle.Left => new Point(bounds.Left, bounds.Center.Y),
        Handle.Right => new Point(bounds.Right, bounds.Center.Y),
        Handle.BottomLeft => bounds.BottomLeft,
        Handle.Bottom => new Point(bounds.Center.X, bounds.Bottom),
        _ => bounds.BottomRight,
    };

    /// <summary>The handle under a point, when one is.</summary>
    private static Handle? HandleAt(Rect bounds, Point point)
    {
        foreach (var handle in Enum.GetValues<Handle>())
        {
            var centre = HandleCentre(bounds, handle);

            // A little larger than the drawn square: a 7-pixel target is hard
            // to hit, and missing it starts a move instead of a resize.
            if (Math.Abs(point.X - centre.X) <= HandleSize &&
                Math.Abs(point.Y - centre.Y) <= HandleSize)
                return handle;
        }

        return null;
    }
}
