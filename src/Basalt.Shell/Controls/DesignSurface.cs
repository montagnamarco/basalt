using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    /// <summary>Where the pointer is, so the hint can say where in the container.</summary>
    private Point _dropAt;

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

    /// <summary>
    /// How the surface draws itself.
    /// </summary>
    /// <remarks>
    /// Set to DesignerLook.VisualBasic6 for a form opened from a .frm: the
    /// dotted grid, the small filled handles and the grey surface are most of
    /// why a Visual Basic 6 designer is recognisable at a glance, and someone
    /// opening a twenty-year-old form is expecting to recognise it.
    /// </remarks>
    public DesignerLook Look
    {
        get => _look;
        set
        {
            _look = value;
            InvalidateVisual();
            DrawSelectionAdorner();
        }
    }

    private DesignerLook _look = DesignerLook.Modern;

    public DesignSurface()
    {
        // The rulers measure where the preview ended up, and nothing can be
        // measured until the layout has placed it: asking during Refresh
        // returns nothing, because Refresh is what puts the content in.
        LayoutUpdated += (_, _) => ShowRulerState();

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
        AddHandler(DoubleTappedEvent, TunnelDoubleTapped, RoutingStrategies.Tunnel);
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

        // Ctrl and the wheel, as every drawing program does it; the wheel
        // alone scrolls, which is what a wheel does everywhere else.
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);

        ContextMenu = BuildContextMenu();
    }

    /// <summary>Raised when the menu asks for something the window owns.</summary>
    public event EventHandler<DesignerMenuCommand>? MenuCommand;

    /// <summary>
    /// Raised when a control is double-clicked, to wire it to code.
    /// </summary>
    /// <remarks>
    /// The surface knows which control was hit; only the window can open a
    /// file and put the caret in it.
    /// </remarks>
    public event EventHandler? HandlerRequested;

    /// <summary>What the designer's context menu can ask for.</summary>
    public enum DesignerMenuCommand
    {
        Cut,
        Copy,
        Paste,
        Delete,
        SelectParent,
        AlignLeft,
        AlignRight,
        AlignTop,
        AlignBottom,
        SameWidth,
        SameHeight,
        BringToFront,
        SendToBack,
        ZoomToFit,
        ResetZoom,
    }

    /// <summary>
    /// The menu that comes up on the surface.
    /// </summary>
    /// <remarks>
    /// Right-clicking is where a person looks for what can be done to the
    /// thing under the pointer, and the designer answered with nothing at
    /// all: the commands existed only in the menu bar, which is a long way
    /// from the control being worked on.
    ///
    /// Availability is decided when the menu opens, so entries grey out
    /// rather than vanish — a menu that changes shape is one you have to
    /// read again every time.
    /// </remarks>
    private ContextMenu BuildContextMenu()
    {
        bool Selected() => _session?.Selection is not null;
        bool Several() => (_session?.SelectedElements.Count ?? 0) > 1;

        MenuAction Ask(string header, DesignerMenuCommand command, Func<bool> available) =>
            new(header, () => MenuCommand?.Invoke(this, command)) { IsAvailable = available };

        return ContextMenus.Build(
        [
            Ask(Localizer.Get(StringKeys.DesignerCut), DesignerMenuCommand.Cut, Selected) with
                { Icon = IconKind.Cut, Gesture = "Ctrl+X" },
            Ask(Localizer.Get(StringKeys.DesignerCopy), DesignerMenuCommand.Copy, Selected) with
                { Icon = IconKind.Copy, Gesture = "Ctrl+C" },
            Ask(Localizer.Get(StringKeys.DesignerPaste), DesignerMenuCommand.Paste, () => _session?.CanPaste == true) with
                { Icon = IconKind.Paste, Gesture = "Ctrl+V" },
            Ask(Localizer.Get(StringKeys.DesignerDelete), DesignerMenuCommand.Delete, Selected),

            MenuAction.Separator,

            Ask(Localizer.Get(StringKeys.DesignerSelectContainer), DesignerMenuCommand.SelectParent,
                () => _session?.Selection?.Parent is not null) with { Gesture = "Escape" },

            MenuAction.Separator,

            // In front and behind: among siblings a control is drawn in the
            // order it is written, so this is what "bring to front" means in
            // XAML and there is no other way to say it.
            Ask(Localizer.Get(StringKeys.DesignerBringToFront), DesignerMenuCommand.BringToFront, Selected),
            Ask(Localizer.Get(StringKeys.DesignerSendToBack), DesignerMenuCommand.SendToBack, Selected),

            MenuAction.Separator,

            // Aligning needs something to align to, so it wants two.
            Ask(Localizer.Get(StringKeys.DesignerAlignLeftItem), DesignerMenuCommand.AlignLeft, Several),
            Ask(Localizer.Get(StringKeys.DesignerAlignRightItem), DesignerMenuCommand.AlignRight, Several),
            Ask(Localizer.Get(StringKeys.DesignerAlignTopItem), DesignerMenuCommand.AlignTop, Several),
            Ask(Localizer.Get(StringKeys.DesignerAlignBottomItem), DesignerMenuCommand.AlignBottom, Several),
            Ask(Localizer.Get(StringKeys.DesignerSameWidthItem), DesignerMenuCommand.SameWidth, Several),
            Ask(Localizer.Get(StringKeys.DesignerSameHeightItem), DesignerMenuCommand.SameHeight, Several),

            MenuAction.Separator,

            Ask(Localizer.Get(StringKeys.DesignerFitToWindow), DesignerMenuCommand.ZoomToFit, () => true),
            Ask(Localizer.Get(StringKeys.DesignerActualSize), DesignerMenuCommand.ResetZoom, () => true),
        ]);
    }

    /// <summary>The context menu, for tests.</summary>
    internal ContextMenu? MenuForTests => ContextMenu;

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.KeyModifiers.HasFlag(KeyModifiers.Meta))
        {
            // Zoomed about the pointer, so the thing under it stays under it:
            // magnifying about the corner sends whatever you were looking at
            // off the edge, and you have to chase it with the scrollbars.
            var before = ToContent(e.GetPosition(this));

            Zoom *= e.Delta.Y > 0 ? 1.1 : 1 / 1.1;

            var after = ToContent(e.GetPosition(this));

            PanBy((after.X - before.X) * Zoom, (after.Y - before.Y) * Zoom);
        }
        else
        {
            PanBy(e.Delta.X * WheelStep, e.Delta.Y * WheelStep);
        }

        e.Handled = true;
    }

    /// <summary>How far one notch of the wheel scrolls.</summary>
    private const double WheelStep = 40;

    /// <summary>The panel holding the preview and its adorners.</summary>
    private Panel? _scaled;

    /// <summary>The scales down the top and the left.</summary>
    private readonly DesignerRulers _rulers = new();

    /// <summary>
    /// Whether the scales are shown.
    /// </summary>
    /// <remarks>
    /// On by default: a form is laid out against numbers, and without a ruler
    /// those numbers live only in the property grid. Off is for when the
    /// panel is too small to spare the space.
    /// </remarks>
    public bool ShowRulers
    {
        get => _showRulers;
        set
        {
            if (_showRulers == value) return;

            _showRulers = value;

            ApplyView();
            ShowRulerState();
        }
    }

    private bool _showRulers = true;

    /// <summary>Where a control sits along the content's own X axis.</summary>
    private double ContentXOf(XElement element) =>
        _controlToElement.FirstOrDefault(p => ReferenceEquals(p.Value, element)).Key is { } control
        && _scaled?.Children.FirstOrDefault() is { } preview
        && control.TranslatePoint(default, preview) is { } at
            ? at.X
            : 0;

    /// <summary>Where a control sits along the content's own Y axis.</summary>
    private double ContentYOf(XElement element) =>
        _controlToElement.FirstOrDefault(p => ReferenceEquals(p.Value, element)).Key is { } control
        && _scaled?.Children.FirstOrDefault() is { } preview
        && control.TranslatePoint(default, preview) is { } at
            ? at.Y
            : 0;

    /// <summary>Tells the rulers where the view is now.</summary>
    private void ShowRulerState()
    {
        _rulers.IsVisible = _showRulers;

        if (!_showRulers) return;

        _rulers.Zoom = _zoom;

        // The content is pushed clear of the scales, so the ruler's own
        // origin is where the content starts rather than where the panel does.
        // Measured against the preview rather than worked out from the pan.
        // The content is also centred by the layout, which is neither pan nor
        // zoom and cannot be derived from either: asking where the preview
        // actually is answers all three at once.
        _rulers.Origin = _scaled?.Children.FirstOrDefault() is { } preview
            && preview.TranslatePoint(default, this) is { } corner
                ? corner
                : new Point(DesignerRulers.Thickness, DesignerRulers.Thickness);

        _rulers.Highlight = _session?.Selection is { } selection
            && BoundsOf(selection) is { } bounds
                ? new Rect(default, bounds.Size)
                    .WithX(ContentXOf(selection))
                    .WithY(ContentYOf(selection))
                : null;
    }

    /// <summary>How much the surface is magnified.</summary>
    /// <remarks>
    /// A window is designed at the size it will run, which is regularly
    /// larger than the panel it is being drawn in: without this the far side
    /// of an 800-wide window is simply unreachable.
    /// </remarks>
    public double Zoom
    {
        get => _zoom;
        set
        {
            var wanted = Math.Clamp(value, MinimumZoom, MaximumZoom);

            if (Math.Abs(wanted - _zoom) < 0.001) return;

            _zoom = wanted;
            ApplyView();

            ZoomChanged?.Invoke(this, _zoom);
        }
    }

    private double _zoom = 1;

    /// <summary>Raised when the magnification changes, for whoever shows it.</summary>
    public event EventHandler<double>? ZoomChanged;

    public const double MinimumZoom = 0.25;
    public const double MaximumZoom = 4;

    /// <summary>How far the view is scrolled, in surface pixels.</summary>
    private Point _pan;

    /// <summary>Puts the zoom and the pan onto the panel.</summary>
    private void ApplyView()
    {
        ShowRulerState();

        if (_scaled is null) return;

        // Scaled about the top left rather than the centre: the origin is
        // where the window being designed starts, and scaling about the
        // middle moves that corner off screen as soon as you zoom in.
        _scaled.RenderTransformOrigin = RelativePoint.TopLeft;

        var aside = _showRulers ? DesignerRulers.Thickness : 0;

        _scaled.RenderTransform = new TransformGroup
        {
            Children =
            {
                new ScaleTransform(_zoom, _zoom),
                new TranslateTransform(_pan.X + aside, _pan.Y + aside),
            },
        };
    }

    /// <summary>Puts the view back to actual size, at the origin.</summary>
    public void ResetView()
    {
        _pan = default;
        Zoom = 1;

        // Zoom raises nothing when it was already 1, so the pan is applied
        // here rather than left to it.
        ApplyView();
    }

    /// <summary>
    /// Fits what is being designed into the space there is.
    /// </summary>
    /// <remarks>
    /// Never magnifies past actual size: a small user control blown up to
    /// fill the panel is not what the user is drawing, and the pixel sizes
    /// they are typing would stop matching what they see.
    /// </remarks>
    public void ZoomToFit()
    {
        if (_scaled is null || Bounds.Width <= 0 || Bounds.Height <= 0) return;

        var content = _scaled.Children.FirstOrDefault();

        var width = content?.Bounds.Width ?? 0;
        var height = content?.Bounds.Height ?? 0;

        if (width <= 0 || height <= 0) return;

        const double margin = 24;

        var fit = Math.Min(
            (Bounds.Width - margin) / width,
            (Bounds.Height - margin) / height);

        _pan = default;
        Zoom = Math.Min(1, fit);

        ApplyView();
    }

    /// <summary>Moves the view by the given amount, for a middle-drag or a scroll.</summary>
    public void PanBy(double dx, double dy)
    {
        _pan = new Point(_pan.X + dx, _pan.Y + dy);
        ApplyView();
    }

    /// <summary>Where a point on the surface falls on the unscaled preview.</summary>
    /// <remarks>
    /// Every hit test and every drag measurement works in the preview's own
    /// coordinates; the pointer arrives in the surface's. Without undoing the
    /// zoom here, a click at 200% selected whatever was under half the
    /// distance from the corner.
    /// </remarks>
    private Point ToContent(Point onSurface)
    {
        // The rulers push the content aside, so their thickness has to come
        // off before the zoom is undone: without it every click lands
        // eighteen pixels from where it was made.
        var aside = _showRulers ? DesignerRulers.Thickness : 0;

        return new(
            (onSurface.X - _pan.X - aside) / _zoom,
            (onSurface.Y - _pan.Y - aside) / _zoom);
    }

    /// <summary>
    /// Double-clicking a control asks for its handler.
    /// </summary>
    /// <remarks>
    /// Only where something is selected, which the press that preceded the
    /// double click has already seen to: double-clicking the background is
    /// not a request for anything.
    /// </remarks>
    private void TunnelDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_session?.Selection is null) return;

        HandlerRequested?.Invoke(this, EventArgs.Empty);

        e.Handled = true;
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
        var over = ToContent(e.GetPosition(this));

        _dropTarget = e.DragEffects == DragDropEffects.Copy ? ContainerAt(over) : null;
        _dropAt = over;

        DrawSelectionAdorner();

        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        _dropTarget = null;

        if (_session is null) return;
        if (e.DataTransfer.TryGetValue(ToolboxPanel.DragFormat) is not { } item) return;

        var at = ToContent(e.GetPosition(this));
        var container = ContainerAt(at);

        var inserted = _session.InsertFromToolbox(item, container);

        // Dropped where the pointer was, but said in the container's own
        // terms: a cell for a Grid, an edge for a DockPanel, coordinates for
        // a Canvas. Writing a Margin everywhere put the control under the
        // pointer on this screen and nowhere sensible on any other.
        if (container is not null && BoundsOf(container) is { } bounds)
        {
            var inside = at - bounds.Position;

            _session.Select(inserted);

            _session.PlaceDrop(
                inserted,
                inside.X, inside.Y,
                bounds.Width, bounds.Height);
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

    /// <summary>
    /// Redraws after a change, but not once per change.
    /// </summary>
    /// <remarks>
    /// Rendering means handing the whole document back to the Avalonia XAML
    /// loader, which on a form of forty controls costs about 40ms — measured,
    /// not guessed. A drag raises a change per frame, so redrawing on each
    /// one caps the designer at around twenty frames a second and makes
    /// dragging feel like it is catching up rather than following.
    ///
    /// Coalesced instead: the changes keep arriving, and the drawing happens
    /// once when they stop. The adorners are not delayed — they are cheap and
    /// they are what the eye follows during a drag.
    /// </remarks>
    private void OnDocumentModified(object? sender, EventArgs e)
    {
        DrawSelectionAdorner();

        _pendingRefresh?.Stop();

        _pendingRefresh ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(RefreshDelay),
        };

        _pendingRefresh.Tick -= OnRefreshDue;
        _pendingRefresh.Tick += OnRefreshDue;

        _pendingRefresh.Start();
    }

    private void OnRefreshDue(object? sender, EventArgs e)
    {
        _pendingRefresh?.Stop();

        Refresh();
    }

    /// <summary>
    /// How long the surface waits before redrawing.
    /// </summary>
    /// <remarks>
    /// Short enough that a single edit looks immediate, long enough that the
    /// frames of a drag fall inside one wait.
    /// </remarks>
    private const double RefreshDelay = 60;

    private DispatcherTimer? _pendingRefresh;

    /// <summary>Redraws now, without waiting. For tests and for a drag ending.</summary>
    public void RefreshNow()
    {
        _pendingRefresh?.Stop();

        Refresh();
    }

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

        // The preview and the adorners share one panel, so the zoom scales
        // both together and a handle stays on the corner it belongs to. A
        // transform on the preview alone would drift them apart.
        var overlay = new Panel();
        overlay.Children.Add(result.Root!);

        // A form with nothing on it is a blank rectangle, which says neither
        // "empty" nor "broken" nor what to do about it. The hint goes under
        // the adorners so it never covers a control, and disappears the
        // moment there is one.
        if (!XamlDocument.ControlChildren(rootElement).Any())
            overlay.Children.Add(EmptyHint());

        overlay.Children.Add(_adorners);

        _scaled = overlay;
        ApplyView();

        // The rulers sit over the surface rather than inside the scaled
        // panel: scaled with the content, their numbers would grow with the
        // zoom and stop meaning device-independent pixels.
        // Detached first, as the adorners are: a visual belongs to one
        // parent, and every refresh builds a new panel to put it in.
        (_rulers.Parent as Panel)?.Children.Remove(_rulers);

        var withRulers = new Panel();

        withRulers.Children.Add(overlay);
        withRulers.Children.Add(_rulers);

        Content = withRulers;

        ShowRulerState();

        DrawSelectionAdorner();

        DesignedSizeChanged?.Invoke(this, DesignedSize);
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

        var at = ToContent(e.GetPosition(this));

        // A press on a handle of the current selection resizes it; anything
        // else selects first. Checked before hit-testing because the handles
        // sit outside the control's own bounds and would otherwise select
        // whatever is behind them.
        //
        // Compared where the handles are drawn — around the bounds, in the
        // surface's own coordinates — rather than in the content's. The two
        // used to differ only by the layout centring the preview, so this
        // was wrong by a few pixels and nobody noticed; with the rulers it
        // is wrong by eighteen more and the handles cannot be grabbed.
        if (SelectionBounds() is { } bounds
            && HandleAt(bounds, e.GetPosition(this), HandleSize) is { } handle)
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

        LastPointerPosition = ToContent(e.GetPosition(this));

        ShowCursorFor(e.GetPosition(this));

        // Where the pointer is, marked on both scales: the reading a person
        // wants while placing something, without stopping to select it.
        if (_showRulers) _rulers.PointerAt = LastPointerPosition;

        if (_bandOrigin is not null)
        {
            _bandTo = ToContent(e.GetPosition(this));
            DrawSelectionAdorner();
            e.Handled = true;
            return;
        }

        if (_drag is null) return;

        _drag.Delta = ToContent(e.GetPosition(this)) - _drag.Origin;

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
            // Dragged, so the control goes where it was dropped, said the way
            // its container lays out: in a Grid that is another cell, not a
            // Margin measured from the first one.
            var landed = drag.Bounds.Position + drag.Delta;

            var selection = _session.Selection;

            // Dropped over a different panel, so it moves into it. Only ever
            // repositioning within the panel it started in meant rearranging
            // a form was delete-and-add-again, losing everything set on the
            // control.
            var over = ContainerAt(landed + new Point(1, 1));

            if (selection is not null
                && over is not null
                && !ReferenceEquals(over, selection.Parent)
                && !ReferenceEquals(over, selection)
                && BoundsOf(over) is { } target)
            {
                var inside = landed - target.Position;

                _session.Reparent(
                    selection, over,
                    inside.X, inside.Y,
                    target.Width, target.Height);

                return;
            }

            if (selection?.Parent is { } container
                && BoundsOf(container) is { } bounds)
            {
                var inside = landed - bounds.Position;

                _session.DragSelectionTo(
                    inside.X, inside.Y,
                    bounds.Width, bounds.Height,
                    drag.Delta.X, drag.Delta.Y);
            }
            else
            {
                _session.MoveSelection(drag.Delta.X, drag.Delta.Y);
            }

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

        // The zoom keys every editor has. Both the main row and the numeric
        // pad, because a keyboard has two plus signs and only one of them
        // would otherwise work.
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Control)
                   || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        if (command)
        {
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add:
                    Zoom *= 1.25;
                    e.Handled = true;
                    return;

                case Key.OemMinus or Key.Subtract:
                    Zoom /= 1.25;
                    e.Handled = true;
                    return;

                case Key.D0 or Key.NumPad0:
                    ResetView();
                    e.Handled = true;
                    return;

                case Key.D9 or Key.NumPad9:
                    ZoomToFit();
                    e.Handled = true;
                    return;
            }
        }

        // Escape with nothing being dragged goes up to the container, which
        // is the only way to reach one that its own children cover.
        if (e.Key == Key.Escape && _session.Selection?.Parent is { } above)
        {
            _session.Select(above);
            SelectionChanged?.Invoke(this, _session.Selection);
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

    /// <summary>
    /// Shows what the pointer would do where it is.
    /// </summary>
    /// <remarks>
    /// The surface never changed the cursor, so a resize handle looked
    /// exactly like the background: whether a corner could be grabbed, and in
    /// which direction it would stretch, could only be found by trying. The
    /// arrow is the one part of the interface that is always under the eye.
    /// </remarks>
    private void ShowCursorFor(Point onSurface)
    {
        // Mid-drag the cursor is whatever started the drag: changing it under
        // the pointer while the button is down reads as the grab slipping.
        if (_drag is not null || _bandOrigin is not null) return;

        // In surface coordinates, which is where the handles are drawn.
        var handle = SelectionBounds() is { } bounds
            ? HandleAt(bounds, onSurface, HandleSize)
            : null;

        var at = ToContent(onSurface);

        Cursor = handle switch
        {
            Handle.TopLeft or Handle.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
            Handle.TopRight or Handle.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
            Handle.Top or Handle.Bottom => new Cursor(StandardCursorType.SizeNorthSouth),
            Handle.Left or Handle.Right => new Cursor(StandardCursorType.SizeWestEast),

            // Over a control that is already selected, so a press would move
            // it: the four-way arrow says so.
            _ when IsOverSelection(at) => new Cursor(StandardCursorType.SizeAll),

            _ => Cursor.Default,
        };
    }

    /// <summary>Whether a point is over something already selected.</summary>
    private bool IsOverSelection(Point at) =>
        _session?.SelectedElements.Any(element =>
            BoundsOf(element) is { } bounds && bounds.Contains(at)) == true;

    /// <summary>
    /// How big the window being designed will actually be.
    /// </summary>
    /// <remarks>
    /// The surface draws the form at whatever size the panel gives it, which
    /// is not the size it will run at: without this the numbers a person is
    /// laying out against are invisible, and a form designed in a narrow
    /// panel is a guess.
    ///
    /// Null where the document says nothing — a UserControl takes the size of
    /// whatever hosts it, and inventing one would be a claim the file does
    /// not make.
    /// </remarks>
    public (double Width, double Height)? DesignedSize
    {
        get
        {
            if (_session?.Document.Root is not { } root) return null;

            var width = Number(root, "Width");
            var height = Number(root, "Height");

            return width is null || height is null ? null : (width.Value, height.Value);
        }
    }

    /// <summary>Raised when the designed size changes, for whoever shows it.</summary>
    public event EventHandler<(double Width, double Height)?>? DesignedSizeChanged;

    private static double? Number(XElement element, string name) =>
        double.TryParse(
            element.Attribute(name)?.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;

    /// <summary>Shows what a control is becoming while it is resized.</summary>
    private void DrawSizeLabel(Rect bounds)
    {
        var label = new Border
        {
            Background = Brushes.DodgerBlue,
            Padding = new Thickness(Spacing.Small, Spacing.Hairline),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = Localizer.Get(
                    StringKeys.DesignerSurfaceSize,
                    Math.Round(bounds.Width),
                    Math.Round(bounds.Height)),
                FontSize = 11,
                Foreground = Brushes.White,
            },
        };

        // Under the bottom-right corner, out of the way of the handles and of
        // whatever is being sized.
        Canvas.SetLeft(label, bounds.Right + Spacing.Tight);
        Canvas.SetTop(label, bounds.Bottom + Spacing.Tight);

        _adorners.Children.Add(label);
    }

    /// <summary>What an empty form says for itself.</summary>
    private static Control EmptyHint() => new TextBlock
    {
        Text = Localizer.Get(StringKeys.DesignerEmptyHint),
        Opacity = 0.45,
        FontSize = 13,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        IsHitTestVisible = false,
    };

    /// <summary>Where the selection is drawn. For tests.</summary>
    internal Rect? SelectionBoundsForTests => SelectionBounds();

    /// <summary>Sets the cursor for a point, as moving there does. For tests.</summary>
    internal void ShowCursorForTests(Point onSurface) => ShowCursorFor(onSurface);

    /// <summary>Whether the "drag something here" hint is up. For tests.</summary>
    internal bool IsShowingEmptyHint =>
        // Through the tree rather than one level down: the rulers put the
        // preview inside a panel of their own, so the hint is no longer a
        // direct child of the content.
        this.GetVisualDescendants()
            .OfType<TextBlock>()
            .Any(t => t.Text == Localizer.Get(StringKeys.DesignerEmptyHint));

    /// <summary>Resolves a content point to a container. For tests.</summary>
    internal XElement? ContainerAtForTests(Point content) => ContainerAt(content);

    /// <summary>The rulers, for tests.</summary>
    internal DesignerRulers RulersForTests => _rulers;

    /// <summary>Shows the drop feedback, as dragging over does. For tests.</summary>
    internal void ShowDropAtForTests(Point content)
    {
        _dropTarget = ContainerAt(content);
        _dropAt = content;

        DrawSelectionAdorner();
    }

    /// <summary>How many adorners are drawn. For tests.</summary>
    internal int AdornerCountForTests => _adorners.Children.Count;

    /// <summary>Sends a key to the surface, as pressing it does. For tests.</summary>
    internal void PressForTests(Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        OnKeyDown(new KeyEventArgs
        {
            Key = key,
            KeyModifiers = modifiers,
            RoutedEvent = KeyDownEvent,
        });

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

    /// <summary>
    /// Draws the dotted grid a Visual Basic 6 form sits on.
    /// </summary>
    /// <remarks>
    /// Through Render rather than as controls: a form of any size is thousands
    /// of dots, and thousands of Borders in the tree costs more than drawing
    /// them costs.
    /// </remarks>
    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // The surface behind the form. Left to the theme in the modern look,
        // painted here in the Visual Basic 6 one, where the grey is as much of
        // the recognition as the grid is.
        if (Look.SurfaceBrush is not null)
            context.FillRectangle(Look.SurfaceBrush, new Rect(Bounds.Size));

        if (!Look.ShowsGrid || Look.GridBrush is null || Look.GridSpacing <= 0) return;

        var pen = new Pen(Look.GridBrush, 1);

        for (var x = Look.GridSpacing; x < Bounds.Width; x += Look.GridSpacing)
        for (var y = Look.GridSpacing; y < Bounds.Height; y += Look.GridSpacing)
            context.DrawLine(pen, new Point(x, y), new Point(x + 1, y));
    }

    /// <summary>Draws the frame and handles around the selected controls.</summary>
    private void DrawSelectionAdorner()
    {
        // The scales mark what is selected, so its width can be read off them
        // rather than out of the property grid.
        if (_showRulers) ShowRulerState();

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
        if (_drag is { Started: true } drag)
        {
            bounds = Preview(bounds, drag);

            // The numbers while the handle is moving, which is when they are
            // wanted: reading them off the property grid afterwards means
            // dragging blind and correcting.
            if (drag.Handle is not null) DrawSizeLabel(bounds);
        }

        // Visual Basic 6 draws the handles and nothing between them. Drawing
        // both a frame and filled handles reads as neither look.
        if (Look.ShowsSelectionFrame)
        {
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
        }

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

        // The container, faintly: it says which panel takes the control.
        var outline = new Border
        {
            Width = bounds.Width,
            Height = bounds.Height,
            BorderBrush = Brushes.MediumSeaGreen,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);

        _adorners.Children.Add(outline);

        // And where in it, which the outline alone never said: a Grid of four
        // cells looked identical wherever the pointer was.
        DrawDropHint(target, bounds);
    }

    /// <summary>Shows the place inside the container the control will take.</summary>
    private void DrawDropHint(XElement target, Rect bounds)
    {
        var inside = _dropAt - bounds.Position;

        var children = XamlDocument
            .ControlChildren(target)
            .Select(child => BoundsOf(child) is { } box
                ? new DesignerLayout.Rect(
                    box.X - bounds.X, box.Y - bounds.Y, box.Width, box.Height)
                : new DesignerLayout.Rect(0, 0, 0, 0))
            .ToList();

        var hint = DesignerLayout.HintFor(
            target,
            new DesignerLayout.Point(inside.X, inside.Y),
            new DesignerLayout.Size(bounds.Width, bounds.Height),
            children);

        if (hint.Region is { } region) DrawHintRegion(bounds, region);
        if (hint.Line is { } line) DrawHintLine(bounds, line);
        if (hint.Label is { Length: > 0 } label) DrawHintLabel(bounds, hint, label);
    }

    /// <summary>Fills the cell or the edge the control will occupy.</summary>
    private void DrawHintRegion(Rect bounds, DesignerLayout.Rect region)
    {
        var fill = new Border
        {
            Width = region.Width,
            Height = region.Height,
            Background = new SolidColorBrush(Color.FromArgb(60, 60, 179, 113)),
            BorderBrush = Brushes.MediumSeaGreen,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };

        Canvas.SetLeft(fill, bounds.X + region.X);
        Canvas.SetTop(fill, bounds.Y + region.Y);

        _adorners.Children.Add(fill);
    }

    /// <summary>
    /// Draws where a control will be inserted in a panel that stacks.
    /// </summary>
    /// <remarks>
    /// A line rather than a region: in a stack the question is the order, and
    /// filling a rectangle would claim a place the panel does not work in.
    /// </remarks>
    private void DrawHintLine(Rect bounds, (DesignerLayout.Point From, DesignerLayout.Point To) line)
    {
        var vertical = Math.Abs(line.From.X - line.To.X) < 0.5;

        var bar = new Border
        {
            Width = vertical ? 3 : Math.Abs(line.To.X - line.From.X),
            Height = vertical ? Math.Abs(line.To.Y - line.From.Y) : 3,
            Background = Brushes.MediumSeaGreen,
            IsHitTestVisible = false,
        };

        // Centred on the line, so it marks the join rather than sitting
        // just past it.
        Canvas.SetLeft(bar, bounds.X + line.From.X - (vertical ? 1.5 : 0));
        Canvas.SetTop(bar, bounds.Y + line.From.Y - (vertical ? 0 : 1.5));

        _adorners.Children.Add(bar);
    }

    /// <summary>Names the cell or the edge, so it need not be counted.</summary>
    private void DrawHintLabel(Rect bounds, DesignerLayout.DropHint hint, string label)
    {
        var text = new Border
        {
            Background = Brushes.MediumSeaGreen,
            Padding = new Thickness(Spacing.Small, Spacing.Hairline),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Brushes.White,
            },
        };

        var region = hint.Region ?? new DesignerLayout.Rect(0, 0, bounds.Width, bounds.Height);

        Canvas.SetLeft(text, bounds.X + region.X + Spacing.Tight);
        Canvas.SetTop(text, bounds.Y + region.Y + Spacing.Tight);

        _adorners.Children.Add(text);
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
    private double HandleSize => Look.HandleSize;

    private Control DrawHandle(Rect bounds, Handle handle)
    {
        var square = new Border
        {
            Width = HandleSize,
            Height = HandleSize,
            Background = Look.HandleFill,
            BorderBrush = Look.HandleStroke,
            BorderThickness = new Thickness(Look.HandleStroke is null ? 0 : 1),
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
    private static Handle? HandleAt(Rect bounds, Point point, double size)
    {
        foreach (var handle in Enum.GetValues<Handle>())
        {
            var centre = HandleCentre(bounds, handle);

            // A little larger than the drawn square: a small target is hard to
            // hit, and missing it starts a move instead of a resize. The
            // Visual Basic 6 look draws them smaller still, so the target is
            // taken from the size rather than fixed at it.
            if (Math.Abs(point.X - centre.X) <= size &&
                Math.Abs(point.Y - centre.Y) <= size)
                return handle;
        }

        return null;
    }
}
