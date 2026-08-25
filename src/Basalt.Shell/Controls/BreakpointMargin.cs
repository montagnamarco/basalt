using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Rendering;

namespace Basalt.Shell.Controls;

/// <summary>
/// The strip left of the line numbers where breakpoints are set.
///
/// Clicking a line toggles a breakpoint there, the way every other IDE does
/// it, and the current line is marked while execution is stopped.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double Diameter = 12;

    /// <summary>
    /// How wide the strip is.
    ///
    /// Wide enough to be an easy target: a few pixels is something the user
    /// misses, and setting a breakpoint is done often.
    /// </summary>
    private const double StripWidth = 20;

    private static readonly IBrush BreakpointFill =
        new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00));

    private static readonly IBrush DisabledFill =
        new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00), 0.25);

    /// <summary>Shown under the pointer, before anything is set.</summary>
    private static readonly IBrush HoverFill =
        new SolidColorBrush(Color.FromRgb(0xE5, 0x14, 0x00), 0.30);

    private static readonly IBrush CurrentLineFill =
        new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

    private readonly HashSet<int> _lines = [];
    private readonly HashSet<int> _disabled = [];

    /// <summary>Raised when the user sets or clears a breakpoint on a line.</summary>
    public event EventHandler<int>? Toggled;

    /// <summary>Lines that currently carry a breakpoint.</summary>
    public IReadOnlyCollection<int> Lines => _lines;

    /// <summary>The line execution is stopped on, or 0 when it is not.</summary>
    public int CurrentLine { get; private set; }

    /// <summary>The line the pointer is over, or 0 when it is elsewhere.</summary>
    private int _hoveredLine;

    protected override Size MeasureOverride(Size availableSize) =>
        new(StripWidth, 0);

    public void Show(IReadOnlyCollection<int> lines, IReadOnlyCollection<int>? disabled = null)
    {
        _lines.Clear();
        foreach (var line in lines) _lines.Add(line);

        _disabled.Clear();
        if (disabled is not null) foreach (var line in disabled) _disabled.Add(line);

        InvalidateVisual();
    }

    /// <summary>Marks where execution is stopped; 0 clears the mark.</summary>
    public void ShowCurrentLine(int line)
    {
        if (CurrentLine == line) return;

        CurrentLine = line;
        InvalidateVisual();
    }

    /// <summary>Sets or clears a breakpoint, reporting which it did.</summary>
    public void Toggle(int line)
    {
        if (line <= 0) return;

        if (!_lines.Remove(line)) _lines.Add(line);
        else _disabled.Remove(line);

        InvalidateVisual();
        Toggled?.Invoke(this, line);
    }

    /// <summary>
    /// Listens for clicks.
    ///
    /// Registered rather than left to an override of OnPointerPressed: the
    /// base margin does not route the event to the virtual method, so the
    /// override compiles and is simply never called. That is how the toggle
    /// came to look finished while doing nothing at all.
    /// </summary>
    public BreakpointMargin()
    {
        AddHandler(PointerPressedEvent, OnMarginPressed, RoutingStrategies.Bubble);

        ContextMenu = BuildContextMenu();
    }

    /// <summary>Raised when the user asks to set a condition on a line.</summary>
    public event EventHandler<int>? ConditionRequested;

    /// <summary>Raised when the user asks to run as far as a line.</summary>
    public event EventHandler<int>? RunToLineRequested;

    /// <summary>The line the context menu was opened on.</summary>
    private int _menuLine;

    /// <summary>
    /// The menu shown on a right click.
    ///
    /// Built once and pointed at whichever line was clicked: a menu per line
    /// would be thousands of objects for a long file.
    /// </summary>
    private ContextMenu BuildContextMenu()
    {
        var toggle = new MenuItem { Header = "Toggle Breakpoint" };
        toggle.Click += (_, _) => Toggle(_menuLine);

        var condition = new MenuItem { Header = "Condition…" };
        condition.Click += (_, _) => ConditionRequested?.Invoke(this, _menuLine);

        var runTo = new MenuItem { Header = "Run to Here" };
        runTo.Click += (_, _) => RunToLineRequested?.Invoke(this, _menuLine);

        var menu = new ContextMenu();

        menu.Opening += (_, _) =>
        {
            // A condition only means something once there is a breakpoint to
            // put it on.
            condition.IsEnabled = _lines.Contains(_menuLine);

            toggle.Header = _lines.Contains(_menuLine)
                ? "Remove Breakpoint"
                : "Add Breakpoint";
        };

        menu.ItemsSource = new object[]
        {
            toggle, condition, new Separator(), runTo
        };

        return menu;
    }

    internal void OpenMenuForTests(int line) => _menuLine = line;

    internal ContextMenu? Menu => ContextMenu;

    private void OnMarginPressed(object? sender, PointerPressedEventArgs e)
    {
        if (LineAt(e.GetPosition(this).Y) is not { } line) return;

        // The right button opens the menu, which needs to know which line it
        // was opened on; only the left button toggles.
        _menuLine = line;

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;

        Toggle(line);
        e.Handled = true;
    }

    /// <summary>
    /// Marks the line the pointer is over.
    ///
    /// Without it there is nothing to say a click will do anything, and the
    /// strip looks like decoration rather than a control.
    /// </summary>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var line = LineAt(e.GetPosition(this).Y) ?? 0;

        if (line == _hoveredLine) return;

        _hoveredLine = line;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);

        if (_hoveredLine == 0) return;

        _hoveredLine = 0;
        InvalidateVisual();
    }

    /// <summary>The document line at a vertical position, or null when there is none.</summary>
    private int? LineAt(double y)
    {
        var view = TextView;

        if (view?.Document is null) return null;

        var visualLine = view.GetVisualLineFromVisualTop(y + view.VerticalOffset);

        return visualLine?.FirstDocumentLine.LineNumber;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        // A control that paints nothing is not hit-testable in Avalonia, so
        // without this the strip swallows no clicks — it never receives any,
        // and the handler above is never called. That is exactly how the
        // toggle came to be written, tested and still broken.
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));

        var view = TextView;
        if (view?.VisualLinesValid != true) return;

        var centreX = Bounds.Width / 2;

        foreach (var visualLine in view.VisualLines)
        {
            var line = visualLine.FirstDocumentLine.LineNumber;

            var y = visualLine.GetTextLineVisualYPosition(
                visualLine.TextLines[0], VisualYPosition.TextMiddle) - view.VerticalOffset;

            // A faint mark where the pointer is, so a click looks like it will
            // do something.
            if (line == _hoveredLine && !_lines.Contains(line))
            {
                context.DrawEllipse(
                    HoverFill, null, new Point(centreX, y), Diameter / 2, Diameter / 2);
            }

            if (line == CurrentLine)
            {
                // An arrow rather than a dot: it says "here", not "stop here".
                var arrow = new StreamGeometry();

                using (var figure = arrow.Open())
                {
                    figure.BeginFigure(new Point(centreX - 4, y - 5), isFilled: true);
                    figure.LineTo(new Point(centreX + 5, y));
                    figure.LineTo(new Point(centreX - 4, y + 5));
                    figure.EndFigure(isClosed: true);
                }

                context.DrawGeometry(CurrentLineFill, null, arrow);
                continue;
            }

            if (!_lines.Contains(line)) continue;

            context.DrawEllipse(
                _disabled.Contains(line) ? DisabledFill : BreakpointFill,
                null,
                new Point(centreX, y),
                Diameter / 2,
                Diameter / 2);
        }
    }
}
