using System.Windows.Forms.Internal.Hosting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Basalt.Designer;
using FormsControl = System.Windows.Forms.Control;
using Rectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace Basalt.Shell.Controls;

/// <summary>One control's bounds in an undoable group movement.</summary>
public sealed record WinFormsBoundsEdit(FormsControl Control, Rectangle Before, Rectangle After);

/// <summary>Displays a Basalt.Forms form with designer input above its live rendering.</summary>
public sealed partial class WinFormsDesignSurface : ContentControl
{
    private enum ResizeHandle
    {
        TopLeft, Top, TopRight, Left, Right, BottomLeft, Bottom, BottomRight,
    }

    private sealed record Gesture(
        FormsControl Control, Rectangle OriginalBounds, DrawingSize OriginalClientSize, Avalonia.Point Origin,
        ResizeHandle? Handle, IPointer Pointer,
        IReadOnlyList<(FormsControl Control, Rectangle Bounds)> Group,
        FormsControl? CollapseOnClick)
    {
        public bool Started { get; set; }
    }

    private const double Gutter = 12;
    private const double HandleSize = 7;
    private const double DragThreshold = 3;

    private readonly Canvas _host = new() { Background = Brushes.Transparent };
    private readonly WinFormsDesignerGrid _grid = new();
    private readonly WinFormsDesignerRulers _rulers = new() { IsHitTestVisible = false };
    private readonly Canvas _adorners = new() { IsHitTestVisible = false };
    private readonly WinFormsDesignerGuides _guides = new() { IsHitTestVisible = false };
    private readonly Canvas _dropAdorners = new() { IsHitTestVisible = false };
    private readonly Canvas _input = new() { Background = Brushes.Transparent };
    private readonly List<FormsControl> _selection = [];
    private readonly Dictionary<string, int> _nextTabSlot = new(StringComparer.Ordinal);
    private FormsCanvas? _canvas;
    private FormsControl? _rootForm;
    private Gesture? _gesture;
    private bool _tabOrderMode;
    private bool _showGrid = true;
    private DrawingSize _gridSize = new(8, 8);

    public WinFormsDesignSurface()
    {
        Focusable = true;
        Background = Brushes.Transparent;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        VerticalContentAlignment = VerticalAlignment.Top;

        // The transparent canvas takes every design-time pointer event. The
        // FormsCanvas below it remains the real painter but cannot click a
        // button, enter text, or start any runtime control interaction.
        _host.Children.Add(_grid);
        _host.Children.Add(_rulers);
        _host.Children.Add(_adorners);
        _host.Children.Add(_guides);
        _host.Children.Add(_dropAdorners);
        _host.Children.Add(_input);
        Content = _host;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, HandleDragOver);
        AddHandler(DragDrop.DragLeaveEvent, HandleDragLeave);
        AddHandler(DragDrop.DropEvent, HandleDrop);
        InitializeContextMenu();
    }

    public FormsControl? RootForm
    {
        get => _rootForm;
        set
        {
            if (ReferenceEquals(_rootForm, value)) return;

            CancelGesture();
            _guides.Clear();
            _rootForm = value;
            _grid.RootForm = value;
            _selection.Clear();
            _canvas = value is null ? null : new FormsCanvas(value);

            _host.Children.Clear();

            if (_canvas is not null) _host.Children.Add(_canvas);

            _host.Children.Add(_grid);
            _host.Children.Add(_rulers);
            _host.Children.Add(_adorners);
            _host.Children.Add(_guides);
            _host.Children.Add(_dropAdorners);
            _host.Children.Add(_input);
            _dropAdorners.Children.Clear();
            UpdateSize();
            DrawSelection();
            SelectionChanged?.Invoke(null);
        }
    }

    public FormsControl? SelectedControl
    {
        get => _selection.LastOrDefault();
        set => Select(value);
    }

    public IReadOnlyList<FormsControl> SelectedControls => _selection.ToArray();

    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            if (_showGrid == value) return;
            _showGrid = value;
            _grid.ShowGrid = value;
        }
    }

    public bool SnapToGrid { get; set; }

    public bool SnapToControls { get; set; } = true;

    public bool ShowRulers
    {
        get => _rulers.ShowRulers;
        set => _rulers.ShowRulers = value;
    }

    public DrawingSize GridSize
    {
        get => _gridSize;
        set
        {
            if (value.Width < 1 || value.Height < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "Grid spacing must be positive.");
            _gridSize = value;
            _grid.GridSize = value;
        }
    }

    public bool TabOrderMode
    {
        get => _tabOrderMode;
        set
        {
            if (_tabOrderMode == value) return;
            CancelGesture();
            _tabOrderMode = value;
            _nextTabSlot.Clear();
            DrawSelection();
            TabOrderModeChanged?.Invoke(value);
        }
    }

    public event Action<FormsControl?>? SelectionChanged;

    public event Action<bool>? TabOrderModeChanged;

    public event Action<IReadOnlyList<WinFormsTabIndexEdit>>? TabOrderEdits;

    /// <summary>Raised once per finished gesture with the bounds before and after it.</summary>
    public event Action<FormsControl, Rectangle, Rectangle>? BoundsEdited;

    /// <summary>Raised once for every group drag or nudge, with all changed controls.</summary>
    public event Action<IReadOnlyList<WinFormsBoundsEdit>>? BoundsEdits;

    public event Action<FormsControl>? DeleteRequested;

    public event Action<WinFormsControlDrop>? ControlDropRequested;

    public void Select(FormsControl? control)
    {
        if (control is not null && !BelongsToRoot(control)) return;
        if (_selection.Count == 1 && ReferenceEquals(_selection[0], control)) return;
        if (_selection.Count == 0 && control is null) return;

        _selection.Clear();
        if (control is not null) _selection.Add(control);
        DrawSelection();
        SelectionChanged?.Invoke(control);
    }

    public void SelectControls(IEnumerable<FormsControl> controls, FormsControl? primary = null)
    {
        ArgumentNullException.ThrowIfNull(controls);
        var selected = controls.Where(control => !control.IsDisposed && BelongsToRoot(control))
            .Distinct<FormsControl>(ReferenceEqualityComparer.Instance).ToList();
        if (selected.Count > 1)
        {
            var parent = primary is not null && selected.Contains(primary) && !ReferenceEquals(primary, _rootForm)
                ? primary.Parent : selected.First(control => !ReferenceEquals(control, _rootForm)).Parent;
            selected.RemoveAll(control => ReferenceEquals(control, _rootForm)
                || !ReferenceEquals(control.Parent, parent));
        }
        if (primary is not null && selected.Remove(primary)) selected.Add(primary);
        if (_selection.SequenceEqual(selected)) return;

        _selection.Clear();
        _selection.AddRange(selected);
        DrawSelection();
        SelectionChanged?.Invoke(SelectedControl);
    }

    private void ToggleSelection(FormsControl control)
    {
        if (ReferenceEquals(control, _rootForm)
            || (_selection.Count > 0 && !ReferenceEquals(_selection[0].Parent, control.Parent)))
        {
            Select(control);
            return;
        }

        _selection.Remove(_rootForm!);
        if (!_selection.Remove(control)) _selection.Add(control);

        DrawSelection();
        SelectionChanged?.Invoke(SelectedControl);
    }

    public void RefreshForm()
    {
        UpdateSize();
        PositionInlineMenuEditor();
        PositionSmartTag();
        _canvas?.RequestFrame();
        _grid.InvalidateVisual();
        DrawSelection();
    }

    public void ApplyLayoutCommand(WinFormsLayoutCommand command)
    {
        if (_tabOrderMode || _gesture is not null) return;
        var edits = WinFormsDesignerLayout.Apply(_selection, command);
        if (edits.Count == 0) return;
        foreach (var edit in edits) edit.Control.Bounds = edit.After;
        RefreshForm();
        BoundsEdits?.Invoke(edits);
    }

    private bool BelongsToRoot(FormsControl control)
    {
        for (var current = control; current is not null; current = current.Parent)
            if (ReferenceEquals(current, _rootForm)) return true;

        return false;
    }

    private void UpdateSize()
    {
        var size = _rootForm?.ClientSize ?? DrawingSize.Empty;
        var width = Math.Max(1, size.Width);
        var height = Math.Max(1, size.Height);

        _host.Width = width + Gutter * 2;
        _host.Height = height + Gutter * 2;
        MinWidth = _host.Width;
        MinHeight = _host.Height;
        _adorners.Width = _host.Width;
        _adorners.Height = _host.Height;
        _guides.Width = _host.Width;
        _guides.Height = _host.Height;
        _grid.Width = _host.Width;
        _grid.Height = _host.Height;
        _rulers.Width = _host.Width;
        _rulers.Height = _host.Height;
        _rulers.ClientSize = new Size(width, height);
        _dropAdorners.Width = _host.Width;
        _dropAdorners.Height = _host.Height;
        _input.Width = _host.Width;
        _input.Height = _host.Height;

        if (_canvas is null) return;

        _canvas.Width = width;
        _canvas.Height = height;
        Canvas.SetLeft(_canvas, Gutter);
        Canvas.SetTop(_canvas, Gutter);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (IsInlineMenuInput(e.Source)) return;
        if (_rootForm is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        Focus();
        var at = e.GetPosition(_host);
        if (_tabOrderMode)
        {
            var target = WinFormsDesignerTabOrder.HitBadge(_rootForm, at, BoundsOnHost) ?? HitTest(at);
            if (target is not null && !ReferenceEquals(target, _rootForm)) AssignTabIndex(target);
            e.Handled = true;
            return;
        }
        var primary = SelectedControl;
        var handle = primary is not null
            ? HandleAt(BoundsOnHost(primary), at)
            : null;
        var hit = handle is null ? HitTest(at) : primary;

        if (handle is not null && _selection.Count > 1)
            Select(primary);

        if (handle is null && hit is not null && (e.KeyModifiers.HasFlag(KeyModifiers.Control)
                                || e.KeyModifiers.HasFlag(KeyModifiers.Meta)
                                || e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            ToggleSelection(hit);
            e.Handled = true;
            return;
        }

        var alreadySelected = hit is not null && _selection.Contains(hit);
        if (!alreadySelected) Select(hit);

        if (hit is not null && (handle is not null || !ReferenceEquals(hit, _rootForm)))
        {
            var group = _selection.Select(control => (control, control.Bounds)).ToArray();
            var collapse = alreadySelected && group.Length > 1 ? hit : null;
            _gesture = new Gesture(hit, hit.Bounds, hit.ClientSize, at, handle,
                e.Pointer, group, collapse);
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_gesture is not { } gesture) return;

        var offset = e.GetPosition(_host) - gesture.Origin;
        if (!gesture.Started && Math.Abs(offset.X) < DragThreshold && Math.Abs(offset.Y) < DragThreshold)
            return;

        gesture.Started = true;
        var dx = (int)Math.Round(offset.X);
        var dy = (int)Math.Round(offset.Y);

        if (gesture.Group.Count > 1 && gesture.Handle is null)
        {
            var groupBounds = gesture.Group.Select(item => item.Bounds)
                .Aggregate(Rectangle.Union);
            var proposedGroup = new Rectangle(groupBounds.X + dx, groupBounds.Y + dy,
                groupBounds.Width, groupBounds.Height);

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                var snapped = SnapMove(gesture.Control, proposedGroup, _selection);
                dx = snapped.X - groupBounds.X;
                dy = snapped.Y - groupBounds.Y;
            }
            else
            {
                _guides.Clear();
            }

            foreach (var (control, bounds) in gesture.Group)
                control.Bounds = new Rectangle(bounds.X + dx, bounds.Y + dy,
                    bounds.Width, bounds.Height);

            RefreshForm();
            e.Handled = true;
            return;
        }

        var original = ReferenceEquals(gesture.Control, _rootForm)
            ? new Rectangle(System.Drawing.Point.Empty, gesture.OriginalClientSize)
            : gesture.OriginalBounds;
        var proposed = gesture.Handle is { } handle
            ? Resize(original, handle, dx, dy)
            : new Rectangle(original.X + dx, original.Y + dy, original.Width, original.Height);

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            proposed = gesture.Handle is { } resizing
                ? SnapResize(gesture.Control, proposed, resizing)
                : SnapMove(gesture.Control, proposed, [gesture.Control]);
        else
            _guides.Clear();

        if (ReferenceEquals(gesture.Control, _rootForm))
            gesture.Control.ClientSize = proposed.Size;
        else
            gesture.Control.Bounds = proposed;

        RefreshForm();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_gesture is not { } gesture) return;

        _gesture = null;
        e.Pointer.Capture(null);
        _guides.Clear();

        if (!gesture.Started && gesture.CollapseOnClick is { } clicked)
            Select(clicked);
        else if (gesture.Group.Count > 1)
            CompleteGroup(gesture.Group);
        else
            Complete(gesture.Control, gesture.OriginalBounds);

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelGesture();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsInlineMenuInput(e.Source)) return;
        if (_tabOrderMode)
        {
            if (e.Key == Key.Escape)
            {
                TabOrderMode = false;
                e.Handled = true;
            }
            return;
        }
        if (_gesture is not null && e.Key == Key.Escape)
        {
            CancelGesture();
            e.Handled = true;
            return;
        }

        if (SelectedControl is not { } selected || _rootForm is null) return;

        if (e.Key is Key.Delete or Key.Back && !ReferenceEquals(selected, _rootForm))
        {
            DeleteRequested?.Invoke(selected);
            e.Handled = true;
            return;
        }

        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
        var (dx, dy) = e.Key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0),
        };

        if ((dx == 0 && dy == 0) || ReferenceEquals(selected, _rootForm)) return;

        var group = _selection.Select(control => (control, control.Bounds)).ToArray();
        foreach (var (control, before) in group)
            control.Bounds = new Rectangle(before.X + dx, before.Y + dy,
                before.Width, before.Height);

        RefreshForm();

        if (group.Length > 1) CompleteGroup(group);
        else Complete(selected, group[0].Bounds);

        e.Handled = true;
    }

    private void CancelGesture()
    {
        _guides.Clear();
        if (_gesture is not { } gesture) return;

        _gesture = null;
        gesture.Pointer.Capture(null);

        if (gesture.Group.Count > 1)
        {
            foreach (var (control, bounds) in gesture.Group)
                control.Bounds = bounds;
        }
        else if (ReferenceEquals(gesture.Control, _rootForm))
            gesture.Control.ClientSize = gesture.OriginalClientSize;
        else
            gesture.Control.Bounds = gesture.OriginalBounds;

        RefreshForm();
    }

    private void Complete(FormsControl control, Rectangle before)
    {
        if (control.Bounds != before) BoundsEdited?.Invoke(control, before, control.Bounds);
    }

    private void CompleteGroup(IReadOnlyList<(FormsControl Control, Rectangle Bounds)> group)
    {
        var changes = group
            .Where(item => item.Control.Bounds != item.Bounds)
            .Select(item => new WinFormsBoundsEdit(item.Control, item.Bounds, item.Control.Bounds))
            .ToArray();

        if (changes.Length > 0) BoundsEdits?.Invoke(changes);
    }

    private FormsControl? HitTest(Avalonia.Point at)
    {
        if (_rootForm is null) return null;

        var x = at.X - Gutter;
        var y = at.Y - Gutter;
        if (x < 0 || y < 0 || x >= _rootForm.ClientSize.Width || y >= _rootForm.ClientSize.Height)
            return null;

        return HitChild(_rootForm, new System.Drawing.Point((int)x, (int)y));
    }

    private void HandleDragOver(object? sender, DragEventArgs e)
    {
        var drop = ResolveDrop(e);
        e.DragEffects = drop is null ? DragDropEffects.None : DragDropEffects.Copy;
        WinFormsDesignerDrop.DrawPreview(_dropAdorners, drop, _rootForm, BoundsOnHost);
        e.Handled = true;
    }

    private void HandleDragLeave(object? sender, DragEventArgs e)
    {
        _dropAdorners.Children.Clear();
        e.Handled = true;
    }

    private void HandleDrop(object? sender, DragEventArgs e)
    {
        _dropAdorners.Children.Clear();
        if (ResolveDrop(e) is { } drop) ControlDropRequested?.Invoke(drop);
        e.Handled = true;
    }

    private WinFormsControlDrop? ResolveDrop(DragEventArgs e)
    {
        if (_rootForm is null || _tabOrderMode ||
            e.DataTransfer.TryGetValue(WinFormsToolboxDrag.Format) is not { } item)
            return null;
        var drop = WinFormsDesignerDrop.Resolve(item.ControlType, _rootForm, e.GetPosition(_host),
            HitTest, BoundsOnHost);
        if (drop is null || !SnapToGrid || e.KeyModifiers.HasFlag(KeyModifiers.Alt)) return drop;
        return drop with { Location = WinFormsDesignerSnap.SnapPoint(drop.Location, GridSize) };
    }

    private void AssignTabIndex(FormsControl control)
    {
        var parent = control.Parent!;
        var key = WinFormsDesignerTabOrder.ParentPath(parent);
        var count = parent.Controls.Count;
        var slot = _nextTabSlot.GetValueOrDefault(key);
        if (slot >= count) slot = 0;
        var edits = WinFormsDesignerTabOrder.MoveToSlot(control, slot);
        _nextTabSlot[key] = slot + 1;
        foreach (var edit in edits) edit.Control.TabIndex = edit.After;
        DrawSelection();
        if (edits.Count > 0) TabOrderEdits?.Invoke(edits);
    }

    private static FormsControl HitChild(FormsControl parent, System.Drawing.Point point)
    {
        foreach (FormsControl child in parent.Controls)
        {
            if (!child.DesiredVisibility || !child.Bounds.Contains(point)) continue;

            var inside = new System.Drawing.Point(
                point.X - child.Left - child.NonClient.Left,
                point.Y - child.Top - child.NonClient.Top);

            return HitChild(child, inside);
        }

        return parent;
    }

    private Rect BoundsOnHost(FormsControl control)
    {
        if (ReferenceEquals(control, _rootForm))
            return new Rect(Gutter, Gutter, control.ClientSize.Width, control.ClientSize.Height);

        var x = control.Left;
        var y = control.Top;

        for (var parent = control.Parent; parent is not null && !ReferenceEquals(parent, _rootForm);
             parent = parent.Parent)
        {
            x += parent.Left + parent.NonClient.Left;
            y += parent.Top + parent.NonClient.Top;
        }

        return new Rect(x + Gutter, y + Gutter, control.Width, control.Height);
    }

    private Rectangle SnapMove(
        FormsControl control, Rectangle proposed, IReadOnlyCollection<FormsControl> excluded)
    {
        if (control.Parent is not { } parent) return proposed;

        var others = parent.Controls.Cast<FormsControl>()
            .Where(sibling => !excluded.Contains(sibling) && sibling.DesiredVisibility)
            .Select(sibling => new AlignmentGuides.Box(
                sibling.Left, sibling.Top, sibling.Width, sibling.Height))
            .ToList();

        var result = WinFormsDesignerSnap.SnapMoveWithGuides(proposed, others,
            SnapToControls, SnapToGrid, GridSize);
        ShowGuides(parent, result);
        return result.Bounds;
    }

    private Rectangle SnapResize(FormsControl control, Rectangle proposed, ResizeHandle handle)
    {
        if (ReferenceEquals(control, _rootForm)) return proposed;
        var siblings = control.Parent?.Controls.Cast<FormsControl>()
            .Where(sibling => !ReferenceEquals(sibling, control) && sibling.DesiredVisibility)
            .Select(sibling => new AlignmentGuides.Box(
                sibling.Left, sibling.Top, sibling.Width, sibling.Height))
            .ToArray() ?? [];
        var result = WinFormsDesignerSnap.SnapResizeWithGuides(proposed, siblings,
            SnapToControls, SnapToGrid, GridSize,
            handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft,
            handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight,
            handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight,
            handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight);
        if (control.Parent is { } parent) ShowGuides(parent, result);
        return result.Bounds;
    }

    private static Rectangle Resize(Rectangle original, ResizeHandle handle, int dx, int dy)
    {
        var left = handle is ResizeHandle.TopLeft or ResizeHandle.Left or ResizeHandle.BottomLeft;
        var top = handle is ResizeHandle.TopLeft or ResizeHandle.Top or ResizeHandle.TopRight;
        var right = handle is ResizeHandle.TopRight or ResizeHandle.Right or ResizeHandle.BottomRight;
        var bottom = handle is ResizeHandle.BottomLeft or ResizeHandle.Bottom or ResizeHandle.BottomRight;

        var x = original.X + (left ? Math.Min(dx, original.Width - 1) : 0);
        var y = original.Y + (top ? Math.Min(dy, original.Height - 1) : 0);
        var width = Math.Max(1, original.Width + (right ? dx : 0) - (left ? dx : 0));
        var height = Math.Max(1, original.Height + (bottom ? dy : 0) - (top ? dy : 0));

        return new Rectangle(x, y, width, height);
    }

    private void DrawSelection()
    {
        _adorners.Children.Clear();
        if (_tabOrderMode)
        {
            if (_rootForm is null) return;
            foreach (var control in WinFormsDesignerTabOrder.Descendants(_rootForm))
                if (control.DesiredVisibility)
                    _adorners.Children.Add(WinFormsDesignerTabOrder.CreateBadge(
                        control.TabIndex, BoundsOnHost(control)));
            return;
        }
        foreach (var selected in _selection)
        {
            if (!BelongsToRoot(selected)) continue;

            var box = BoundsOnHost(selected);
            _adorners.Children.Add(new Border
            {
                Width = box.Width,
                Height = box.Height,
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                [Canvas.LeftProperty] = box.X,
                [Canvas.TopProperty] = box.Y,
            });
        }

        if (SelectedControl is not { } primary) return;

        var bounds = BoundsOnHost(primary);

        foreach (var handle in Enum.GetValues<ResizeHandle>())
        {
            var centre = HandleCentre(bounds, handle);
            _adorners.Children.Add(new Border
            {
                Width = HandleSize,
                Height = HandleSize,
                Background = Brushes.White,
                BorderBrush = Brushes.DodgerBlue,
                BorderThickness = new Thickness(1),
                [Canvas.LeftProperty] = centre.X - HandleSize / 2,
                [Canvas.TopProperty] = centre.Y - HandleSize / 2,
            });
        }
    }

    private static ResizeHandle? HandleAt(Rect bounds, Avalonia.Point point)
    {
        foreach (var handle in Enum.GetValues<ResizeHandle>())
        {
            var centre = HandleCentre(bounds, handle);
            if (Math.Abs(point.X - centre.X) <= HandleSize &&
                Math.Abs(point.Y - centre.Y) <= HandleSize)
                return handle;
        }

        return null;
    }

    private static Avalonia.Point HandleCentre(Rect bounds, ResizeHandle handle) => handle switch
    {
        ResizeHandle.TopLeft => bounds.TopLeft,
        ResizeHandle.Top => new Avalonia.Point(bounds.Center.X, bounds.Top),
        ResizeHandle.TopRight => bounds.TopRight,
        ResizeHandle.Left => new Avalonia.Point(bounds.Left, bounds.Center.Y),
        ResizeHandle.Right => new Avalonia.Point(bounds.Right, bounds.Center.Y),
        ResizeHandle.BottomLeft => bounds.BottomLeft,
        ResizeHandle.Bottom => new Avalonia.Point(bounds.Center.X, bounds.Bottom),
        _ => bounds.BottomRight,
    };
}
