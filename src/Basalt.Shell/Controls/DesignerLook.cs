using Avalonia;
using Avalonia.Media;

namespace Basalt.Shell.Controls;

/// <summary>
/// How the design surface draws itself.
/// </summary>
/// <remarks>
/// Two looks rather than one, because the people opening a Visual Basic 6 form
/// and the people drawing a new Avalonia window want different things. The
/// first want what they had: a dotted grid, small filled handles, a form that
/// sits at a fixed size on a plain background. The second want what every
/// designer since has looked like.
///
/// Kept as data rather than as branches through the drawing code, so adding a
/// look is a value here and not a condition in twenty places.
/// </remarks>
public sealed record DesignerLook
{
    /// <summary>What a designer looks like now.</summary>
    public static DesignerLook Modern { get; } = new();

    /// <summary>
    /// What Visual Basic 6 looked like.
    /// </summary>
    /// <remarks>
    /// The measurements are the ones VB6 used: a grid every 8 pixels — 120
    /// twips, its default — and handles of six pixels rather than seven.
    /// Small differences, and together they are most of why a screenshot of
    /// one is recognisable at a glance.
    /// </remarks>
    public static DesignerLook VisualBasic6 { get; } = new()
    {
        ShowsGrid = true,
        GridSpacing = 8,
        GridBrush = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
        SurfaceBrush = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0)),
        FormBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xD0, 0xC8)),

        // Filled, not outlined: VB6 draws solid squares and no frame between
        // them. The frame is a later idea, and drawing both reads as neither.
        HandleSize = 6,
        HandleFill = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)),
        HandleStroke = null,
        ShowsSelectionFrame = false,

        // A control that is selected but not the primary gets hollow handles
        // in VB6 rather than an outline, which is how it showed which one a
        // resize would act on.
        SecondaryHandleFill = Brushes.White,
        SecondaryHandleStroke = new SolidColorBrush(Color.FromRgb(0x00, 0x00, 0x80)),
    };

    /// <summary>Whether a dotted grid is drawn behind the form.</summary>
    public bool ShowsGrid { get; init; }

    /// <summary>How far apart the grid dots are, in pixels.</summary>
    public double GridSpacing { get; init; } = 8;

    public IBrush? GridBrush { get; init; }

    /// <summary>Behind the form, where the form is not.</summary>
    public IBrush? SurfaceBrush { get; init; }

    /// <summary>The form's own background, when it names none.</summary>
    public IBrush? FormBrush { get; init; }

    public double HandleSize { get; init; } = 7;

    public IBrush HandleFill { get; init; } = Brushes.White;

    public IBrush? HandleStroke { get; init; } = Brushes.DodgerBlue;

    public IBrush SecondaryHandleFill { get; init; } = Brushes.Transparent;

    public IBrush? SecondaryHandleStroke { get; init; } = Brushes.DodgerBlue;

    /// <summary>Whether a frame is drawn around the selection.</summary>
    public bool ShowsSelectionFrame { get; init; } = true;

    /// <summary>
    /// Rounds a position to the grid, when the look has one.
    /// </summary>
    /// <remarks>
    /// VB6 snapped to the grid and drew it, so where a control landed was
    /// visible before it was dropped. A snap with no grid to see is the same
    /// behaviour with the explanation removed.
    /// </remarks>
    public double SnapToGrid(double value) =>
        ShowsGrid && GridSpacing > 0
            ? Math.Round(value / GridSpacing) * GridSpacing
            : value;

    public Point SnapToGrid(Point point) =>
        new(SnapToGrid(point.X), SnapToGrid(point.Y));
}
