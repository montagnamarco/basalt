using Basalt.Designer;
using System.Drawing;

namespace Basalt.Shell.Controls;

/// <summary>Applies designer guide and grid rules in parent-client coordinates.</summary>
internal static class WinFormsDesignerSnap
{
    internal readonly record struct SnapResult(Rectangle Bounds, double? VerticalGuide, double? HorizontalGuide);

    internal static Rectangle SnapMove(Rectangle proposed,
        IReadOnlyList<AlignmentGuides.Box> siblings, bool controls, bool grid, Size spacing)
        => SnapMoveWithGuides(proposed, siblings, controls, grid, spacing).Bounds;

    internal static SnapResult SnapMoveWithGuides(Rectangle proposed,
        IReadOnlyList<AlignmentGuides.Box> siblings, bool controls, bool grid, Size spacing)
    {
        var guides = AlignmentGuides.SnapTo(
            new AlignmentGuides.Box(proposed.X, proposed.Y, proposed.Width, proposed.Height),
            siblings, controls);
        var guideX = guides.Guides.Any(guide => guide.IsVertical);
        var guideY = guides.Guides.Any(guide => !guide.IsVertical);
        var x = guideX ? (int)Math.Round(guides.X) : grid ? SnapAxis(proposed.X, spacing.Width) : proposed.X;
        var y = guideY ? (int)Math.Round(guides.Y) : grid ? SnapAxis(proposed.Y, spacing.Height) : proposed.Y;
        return new SnapResult(new Rectangle(x, y, proposed.Width, proposed.Height),
            guides.Guides.Where(guide => guide.IsVertical).Select(guide => (double?)guide.At).FirstOrDefault(),
            guides.Guides.Where(guide => !guide.IsVertical).Select(guide => (double?)guide.At).FirstOrDefault());
    }

    internal static Rectangle SnapResize(Rectangle proposed,
        IReadOnlyList<AlignmentGuides.Box> siblings, bool controls, bool grid, Size spacing,
        bool left, bool top, bool right, bool bottom)
        => SnapResizeWithGuides(proposed, siblings, controls, grid, spacing,
            left, top, right, bottom).Bounds;

    internal static SnapResult SnapResizeWithGuides(Rectangle proposed,
        IReadOnlyList<AlignmentGuides.Box> siblings, bool controls, bool grid, Size spacing,
        bool left, bool top, bool right, bool bottom)
    {
        var leftEdge = left
            ? SnapEdge(proposed.Left, spacing.Width, siblings, controls, grid, vertical: true)
            : (proposed.Left, false);
        var topEdge = top
            ? SnapEdge(proposed.Top, spacing.Height, siblings, controls, grid, vertical: false)
            : (proposed.Top, false);
        var rightEdge = right
            ? SnapEdge(proposed.Right, spacing.Width, siblings, controls, grid, vertical: true)
            : (proposed.Right, false);
        var bottomEdge = bottom
            ? SnapEdge(proposed.Bottom, spacing.Height, siblings, controls, grid, vertical: false)
            : (proposed.Bottom, false);
        var x1 = Math.Min(leftEdge.Item1, proposed.Right - 1);
        var y1 = Math.Min(topEdge.Item1, proposed.Bottom - 1);
        var x2 = Math.Max(rightEdge.Item1, x1 + 1);
        var y2 = Math.Max(bottomEdge.Item1, y1 + 1);
        var verticalGuide = left && leftEdge.Item2 && x1 == leftEdge.Item1
            ? x1 : right && rightEdge.Item2 && x2 == rightEdge.Item1 ? x2 : (double?)null;
        var horizontalGuide = top && topEdge.Item2 && y1 == topEdge.Item1
            ? y1 : bottom && bottomEdge.Item2 && y2 == bottomEdge.Item1 ? y2 : (double?)null;
        return new SnapResult(Rectangle.FromLTRB(x1, y1, x2, y2), verticalGuide, horizontalGuide);
    }

    internal static Point SnapPoint(Point point, Size spacing) =>
        new(SnapAxis(point.X, spacing.Width), SnapAxis(point.Y, spacing.Height));

    private static int SnapAxis(int value, int spacing) =>
        (int)Math.Round((double)value / spacing, MidpointRounding.AwayFromZero) * spacing;

    private static (int Value, bool ControlGuide) SnapEdge(int value, int spacing,
        IReadOnlyList<AlignmentGuides.Box> siblings, bool controls, bool grid, bool vertical)
    {
        if (controls)
        {
            var nearest = value;
            var distance = AlignmentGuides.SnapDistance;
            var found = false;
            foreach (var sibling in siblings)
            {
                var edges = vertical
                    ? new[] { sibling.X, sibling.CentreX, sibling.Right }
                    : new[] { sibling.Y, sibling.CentreY, sibling.Bottom };
                foreach (var edge in edges)
                {
                    var candidateDistance = Math.Abs(edge - value);
                    if (candidateDistance > distance) continue;
                    nearest = (int)Math.Round(edge);
                    distance = candidateDistance;
                    found = true;
                }
            }

            if (found) return (nearest, true);
        }

        return (grid ? SnapAxis(value, spacing) : value, false);
    }
}
