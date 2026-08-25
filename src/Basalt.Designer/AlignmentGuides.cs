namespace Basalt.Designer;

/// <summary>
/// Where a control being dragged lines up with the ones around it.
/// </summary>
/// <remarks>
/// Placing controls by eye is what makes a hand-built layout look untidy: two
/// buttons a pixel apart are visibly wrong and invisible while dragging.
/// Guides show the alignment and snapping makes it exact.
///
/// Here rather than in the surface so it can be tested without a window, and
/// so a Visual Basic form designer can use the same arithmetic.
/// </remarks>
public static class AlignmentGuides
{
    /// <summary>How near an edge must be before it snaps, in pixels.</summary>
    public const double SnapDistance = 5;

    /// <summary>A line to draw, and what it lines up with.</summary>
    public readonly record struct Guide(bool IsVertical, double At);

    /// <summary>A rectangle, as the surface knows it.</summary>
    public readonly record struct Box(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
        public double CentreX => X + Width / 2;
        public double CentreY => Y + Height / 2;
    }

    /// <summary>What a dragged box snaps to, and the guides to draw for it.</summary>
    public readonly record struct Snap(double X, double Y, IReadOnlyList<Guide> Guides);

    /// <summary>
    /// Snaps a dragged box to the others, and says which lines to draw.
    /// </summary>
    /// <param name="dragged">Where the box would land with no snapping.</param>
    /// <param name="others">The boxes it may line up with, itself excluded.</param>
    /// <param name="enabled">
    /// False while Alt is held, which is how a designer places something
    /// deliberately off the grid.
    /// </param>
    public static Snap SnapTo(Box dragged, IReadOnlyList<Box> others, bool enabled = true)
    {
        if (!enabled || others.Count == 0) return new Snap(dragged.X, dragged.Y, []);

        var guides = new List<Guide>();

        var x = SnapAxis(
            [dragged.X, dragged.CentreX, dragged.Right],
            [.. others.SelectMany(o => new[] { o.X, o.CentreX, o.Right })],
            guides, isVertical: true);

        var y = SnapAxis(
            [dragged.Y, dragged.CentreY, dragged.Bottom],
            [.. others.SelectMany(o => new[] { o.Y, o.CentreY, o.Bottom })],
            guides, isVertical: false);

        return new Snap(dragged.X + x, dragged.Y + y, guides);
    }

    /// <summary>
    /// How far to shift along one axis so an edge lines up, or zero.
    /// </summary>
    /// <remarks>
    /// The nearest candidate within the snap distance wins. Every edge of the
    /// dragged box is tried against every edge of the others, so a control
    /// snaps by whichever of its own sides happens to be closest — which is
    /// what makes the gesture feel like it is helping rather than fighting.
    /// </remarks>
    private static double SnapAxis(
        double[] edges, double[] candidates, List<Guide> guides, bool isVertical)
    {
        var best = 0d;
        var bestDistance = SnapDistance;
        var bestAt = 0d;
        var found = false;

        foreach (var edge in edges)
        {
            foreach (var candidate in candidates)
            {
                var distance = Math.Abs(candidate - edge);

                if (distance > bestDistance) continue;

                bestDistance = distance;
                best = candidate - edge;
                bestAt = candidate;
                found = true;
            }
        }

        if (found) guides.Add(new Guide(isVertical, bestAt));

        return best;
    }
}
