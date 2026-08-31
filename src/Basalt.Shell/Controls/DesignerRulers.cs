using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// The scales down the top and the left of the design surface.
/// </summary>
/// <remarks>
/// A form is laid out against numbers — a control 24 high, a margin of 8 —
/// and without a ruler those numbers exist only in the property grid. Reading
/// a size means selecting the control and looking away from the form; judging
/// whether two gaps match means measuring both that way and remembering.
///
/// Drawn rather than assembled from controls: a ruler is a few hundred lines
/// and a dozen labels that change on every pan, and building that many
/// controls per frame would cost more than the surface it decorates.
///
/// It reads the zoom and where the content ended up rather than being scaled
/// with it,
/// so the numbers stay legible and keep saying device-independent pixels —
/// the unit the XAML is written in — at any magnification.
/// </remarks>
public sealed class DesignerRulers : Control
{
    /// <summary>How thick the scales are.</summary>
    public const double Thickness = 18;

    public static readonly StyledProperty<double> ZoomProperty =
        AvaloniaProperty.Register<DesignerRulers, double>(nameof(Zoom), 1);

    /// <summary>
    /// Where the content's own origin sits on the surface.
    /// </summary>
    /// <remarks>
    /// Measured rather than derived: the layout centres the preview as well
    /// as the pan moving it, and only asking where it ended up accounts for
    /// both.
    /// </remarks>
    public static readonly StyledProperty<Point> OriginProperty =
        AvaloniaProperty.Register<DesignerRulers, Point>(nameof(Origin));

    /// <summary>Where the pointer is, in the content's own coordinates.</summary>
    public static readonly StyledProperty<Point?> PointerAtProperty =
        AvaloniaProperty.Register<DesignerRulers, Point?>(nameof(PointerAt));

    /// <summary>What is selected, so its edges can be marked.</summary>
    public static readonly StyledProperty<Rect?> HighlightProperty =
        AvaloniaProperty.Register<DesignerRulers, Rect?>(nameof(Highlight));

    static DesignerRulers()
    {
        AffectsRender<DesignerRulers>(
            ZoomProperty, OriginProperty, PointerAtProperty, HighlightProperty);
    }

    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public Point Origin
    {
        get => GetValue(OriginProperty);
        set => SetValue(OriginProperty, value);
    }

    public Point? PointerAt
    {
        get => GetValue(PointerAtProperty);
        set => SetValue(PointerAtProperty, value);
    }

    public Rect? Highlight
    {
        get => GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    public DesignerRulers()
    {
        // Decoration: a click belongs to whatever is being designed under it.
        IsHitTestVisible = false;
    }

    /// <summary>
    /// How far apart the numbered marks are, in content pixels.
    /// </summary>
    /// <remarks>
    /// Chosen so the marks stay roughly the same distance apart on screen
    /// however far the view is zoomed: a fixed step gives marks touching each
    /// other at 25% and one lonely mark at 400%. The steps are the ones a
    /// person reads without arithmetic — 1, 2, 5 and their tens.
    /// </remarks>
    internal static double StepFor(double zoom)
    {
        const double wanted = 80;

        foreach (var step in Steps)
            if (step * zoom >= wanted) return step;

        return Steps[^1];
    }

    private static readonly double[] Steps =
        [1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000];

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var face = GetBrush("SideBarBackgroundBrush", Colors.WhiteSmoke);
        var line = GetBrush("PanelBorderBrush", Colors.Silver);
        var ink = GetBrush("EditorForegroundBrush", Colors.Black);

        var width = Bounds.Width;
        var height = Bounds.Height;

        if (width <= Thickness || height <= Thickness) return;

        // The two strips and the square where they meet.
        context.FillRectangle(face, new Rect(0, 0, width, Thickness));
        context.FillRectangle(face, new Rect(0, 0, Thickness, height));

        var edge = new Pen(line, 1);

        context.DrawLine(edge, new Point(0, Thickness), new Point(width, Thickness));
        context.DrawLine(edge, new Point(Thickness, 0), new Point(Thickness, height));

        DrawScale(context, ink, horizontal: true, length: width);
        DrawScale(context, ink, horizontal: false, length: height);

        DrawHighlight(context);
        DrawPointer(context);
    }

    /// <summary>Draws one of the two scales.</summary>
    private void DrawScale(DrawingContext context, IBrush ink, bool horizontal, double length)
    {
        var zoom = Math.Max(Zoom, 0.01);
        var step = StepFor(zoom);

        var offset = horizontal ? Origin.X : Origin.Y;

        // The first mark at or before the visible edge, so the scale does not
        // start wherever the content happens to.
        var first = Math.Floor((Thickness - offset) / zoom / step) * step;

        var faint = new Pen(ink, 1) { Brush = new SolidColorBrush(ink is ISolidColorBrush s ? s.Color : Colors.Black, 0.35) };
        var strong = new Pen(new SolidColorBrush(ink is ISolidColorBrush t ? t.Color : Colors.Black, 0.7), 1);

        for (var value = first; ; value += step / 5)
        {
            var at = value * zoom + offset;

            if (at > length) break;
            if (at < Thickness) continue;

            // Every fifth mark is a numbered one; the rest are shorter and
            // fainter, so the eye finds the numbers without reading them.
            var numbered = Math.Abs(value / step - Math.Round(value / step)) < 0.001;

            var size = numbered ? Thickness * 0.55 : Thickness * 0.3;

            if (horizontal)
            {
                context.DrawLine(
                    numbered ? strong : faint,
                    new Point(at, Thickness - size),
                    new Point(at, Thickness));
            }
            else
            {
                context.DrawLine(
                    numbered ? strong : faint,
                    new Point(Thickness - size, at),
                    new Point(Thickness, at));
            }

            if (!numbered) continue;

            var text = new FormattedText(
                ((int)Math.Round(value)).ToString(System.Globalization.CultureInfo.CurrentCulture),
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                ink);

            // Along the strip for the top ruler; the side one puts its
            // numbers upright rather than turned, since a rotated digit is
            // read more slowly than a slightly crowded one.
            //
            // Both are lifted by half the line so the number sits *on* its
            // mark rather than below it: drawn from the corner, a label reads
            // as belonging to the tick after it.
            if (horizontal)
                context.DrawText(text, new Point(at + 2, (Thickness - text.Height) / 2));
            else
                context.DrawText(text, new Point(1, at - text.Height / 2));
        }
    }

    /// <summary>
    /// Marks where the selected control begins and ends.
    /// </summary>
    /// <remarks>
    /// The question a ruler is really asked — "how wide is this, and where
    /// does it sit" — answered without selecting anything or reading a
    /// number off another panel.
    /// </remarks>
    private void DrawHighlight(DrawingContext context)
    {
        if (Highlight is not { } box) return;

        var fill = new SolidColorBrush(Colors.DodgerBlue, 0.25);

        var left = box.X * Zoom + Origin.X;
        var top = box.Y * Zoom + Origin.Y;

        var across = Math.Max(box.Width * Zoom, 1);
        var down = Math.Max(box.Height * Zoom, 1);

        context.FillRectangle(fill, new Rect(left, 0, across, Thickness));
        context.FillRectangle(fill, new Rect(0, top, Thickness, down));
    }

    /// <summary>Shows where the pointer is on both scales.</summary>
    private void DrawPointer(DrawingContext context)
    {
        if (PointerAt is not { } at) return;

        var pen = new Pen(new SolidColorBrush(Colors.Crimson, 0.8), 1);

        var x = at.X * Zoom + Origin.X;
        var y = at.Y * Zoom + Origin.Y;

        context.DrawLine(pen, new Point(x, 0), new Point(x, Thickness));
        context.DrawLine(pen, new Point(0, y), new Point(Thickness, y));
    }

    /// <summary>A themed brush, or a plain colour where the theme has none.</summary>
    private IBrush GetBrush(string key, Color fallback) =>
        this.TryFindResource(key, out var found) && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);
}
