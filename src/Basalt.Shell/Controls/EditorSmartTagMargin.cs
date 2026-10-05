using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using Basalt.Core.Localization;

namespace Basalt.Shell.Controls;

internal sealed class EditorSmartTagMargin : Control
{
    private static readonly IBrush Bulb = new SolidColorBrush(Color.FromRgb(255, 205, 55));
    private static readonly Pen Outline = new(Brushes.DarkGoldenrod, 1);
    private static readonly IBrush BoxBackground = new SolidColorBrush(Color.FromRgb(255, 250, 230));
    private Rect _indicator;

    internal TextView? TextView { get; set; }
    internal int AnchorOffset { get; private set; }

    internal int AvailableLine { get; private set; }

    internal event EventHandler? Requested;

    internal EditorSmartTagMargin()
    {
        Width = 18;
        Height = 18;
        IsVisible = false;
        Name = "EditorSmartTag";
        ToolTip.SetTip(this, Localizer.Get(StringKeys.EditorSmartTags));
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override Size MeasureOverride(Size availableSize) => new(18, 18);

    internal void Show(int line, int offset = 0)
    {
        AvailableLine = line;
        AnchorOffset = offset;
        UpdatePosition();
        _indicator = default;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs args)
    {
        base.OnPointerPressed(args);
        if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed && _indicator.Contains(args.GetPosition(this)))
        {
            args.Handled = true;
            Requested?.Invoke(this, EventArgs.Empty);
        }
    }

    internal void UpdatePosition()
    {
        if (AvailableLine <= 0 || TextView is not { VisualLinesValid: true } view ||
            Parent is not Canvas canvas || AnchorOffset > view.Document.TextLength)
        {
            IsVisible = false;
            return;
        }

        var location = view.Document.GetLocation(AnchorOffset);
        if (!view.VisualLines.Any(line => line.FirstDocumentLine.LineNumber <= location.Line &&
                line.LastDocumentLine.LineNumber >= location.Line))
        {
            IsVisible = false;
            return;
        }

        var position = view.GetVisualPosition(new TextViewPosition(location), VisualYPosition.LineBottom)
            - view.ScrollOffset;
        if (position.X < 0 || position.X >= view.Bounds.Width || position.Y < 0 || position.Y >= view.Bounds.Height)
        {
            IsVisible = false;
            return;
        }

        var origin = view.TranslatePoint(position, canvas);
        if (origin is not { } point)
        {
            IsVisible = false;
            return;
        }

        Canvas.SetLeft(this, point.X);
        Canvas.SetTop(this, point.Y);
        IsVisible = true;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _indicator = new Rect(0, 0, 18, 18);
        if (!IsVisible)
        {
            return;
        }

        context.DrawRectangle(BoxBackground, Outline, new Rect(0.5, 0.5, 17, 17), 2, 2);
        context.DrawEllipse(Bulb, Outline, new Point(9, 6), 4, 4);
        context.DrawRectangle(Bulb, Outline, new Rect(7, 10, 4, 3));
        context.DrawLine(Outline, new Point(7, 15), new Point(11, 15));
    }
}
