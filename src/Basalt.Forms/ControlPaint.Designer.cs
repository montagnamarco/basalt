using System.Drawing;

namespace System.Windows.Forms;

/// <summary>The designer's own adornments: grab handles, locked and selection frames, the snap grid. None of these come from SystemColors - the designer draws over whatever is selected, in plain black and white.</summary>
public static partial class ControlPaint
{
    public static void DrawGrabHandle(Graphics graphics, Rectangle rectangle, bool primary, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        // A primary (the last-clicked) selection's handle is black on white;
        // the rest are white on black; disabled handles fall back to the
        // control face so a locked control's handles do not look editable.
        var fill = !enabled ? SystemColors.Control : primary ? Color.Black : Color.White;
        var border = !enabled ? SystemColors.ControlDark : primary ? Color.White : Color.Black;

        using var fillBrush = new SolidBrush(fill);
        using var pen = new Pen(border);

        graphics.FillRectangle(fillBrush, rectangle);
        graphics.DrawRectangle(pen, rectangle.Left, rectangle.Top, rectangle.Width - 1, rectangle.Height - 1);
    }

    public static void DrawContainerGrabHandle(Graphics graphics, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var fillBrush = new SolidBrush(Color.White);
        using var pen = new Pen(Color.Black);

        graphics.FillRectangle(fillBrush, bounds);
        graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);

        // The cross through the middle is what tells a container handle apart from a plain one.
        var cx = bounds.Left + bounds.Width / 2;
        var cy = bounds.Top + bounds.Height / 2;
        graphics.DrawLine(pen, bounds.Left, cy, bounds.Right - 1, cy);
        graphics.DrawLine(pen, cx, bounds.Top, cx, bounds.Bottom - 1);
    }

    public static void DrawLockedFrame(Graphics graphics, Rectangle rectangle, bool primary)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var (a, b) = primary ? (Color.White, Color.Black) : (Color.Black, Color.White);
        DrawMarchingAnts(graphics, rectangle, a, b);
    }

    public static void DrawSelectionFrame(Graphics graphics, bool active, Rectangle outsideRect, Rectangle insideRect, Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var frameColor = active ? Color.Black : Color.White;
        DrawMarchingAnts(graphics, outsideRect, frameColor, backColor);

        const int size = 7;

        foreach (var point in HandlePositions(outsideRect, size))
        {
            DrawGrabHandle(graphics, new Rectangle(point, new Size(size, size)), active, enabled: true);
        }
    }

    /// <summary>The eight handle positions of the usual selection adornment: one at each corner, one at the middle of each edge.</summary>
    private static Point[] HandlePositions(Rectangle outsideRect, int size) =>
    [
        new Point(outsideRect.Left, outsideRect.Top),
        new Point(outsideRect.Left + (outsideRect.Width - size) / 2, outsideRect.Top),
        new Point(outsideRect.Right - size, outsideRect.Top),
        new Point(outsideRect.Left, outsideRect.Top + (outsideRect.Height - size) / 2),
        new Point(outsideRect.Right - size, outsideRect.Top + (outsideRect.Height - size) / 2),
        new Point(outsideRect.Left, outsideRect.Bottom - size),
        new Point(outsideRect.Left + (outsideRect.Width - size) / 2, outsideRect.Bottom - size),
        new Point(outsideRect.Right - size, outsideRect.Bottom - size),
    ];

    /// <summary>The "marching ants" dashed rectangle: alternating pixels around the perimeter, same technique as <see cref="DrawFocusRectangle"/>.</summary>
    private static void DrawMarchingAnts(Graphics graphics, Rectangle rect, Color a, Color b)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        using var brushA = new SolidBrush(a);
        using var brushB = new SolidBrush(b);

        for (var x = rect.Left; x < rect.Right; x++)
        {
            graphics.FillRectangle(x % 2 == 0 ? brushA : brushB, x, rect.Top, 1, 1);
            graphics.FillRectangle(x % 2 == 0 ? brushA : brushB, x, rect.Bottom - 1, 1, 1);
        }

        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            graphics.FillRectangle(y % 2 == 0 ? brushA : brushB, rect.Left, y, 1, 1);
            graphics.FillRectangle(y % 2 == 0 ? brushA : brushB, rect.Right - 1, y, 1, 1);
        }
    }

    public static void DrawGrid(Graphics graphics, Rectangle area, Size pixelsBetweenDots, Color backColor)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (pixelsBetweenDots.Width <= 0 || pixelsBetweenDots.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsBetweenDots), "The spacing between dots must be positive.");
        }

        var dotColor = backColor.GetBrightness() > 0.5f ? SystemColors.ControlDark : SystemColors.ControlLightLight;
        using var brush = new SolidBrush(dotColor);

        for (var y = area.Top; y < area.Bottom; y += pixelsBetweenDots.Height)
        {
            for (var x = area.Left; x < area.Right; x += pixelsBetweenDots.Width)
            {
                graphics.FillRectangle(brush, x, y, 1, 1);
            }
        }
    }
}
