using System.Drawing;

namespace System.Windows.Forms;

/// <summary>Draws a progress bar's chunked fill in the classic Windows 2000 look.</summary>
public static class ProgressBarRenderer
{
    /// <summary>The classic chunk is a 6-pixel-wide block of SystemColors.Highlight.</summary>
    public static int ChunkThickness => 6;

    /// <summary>The classic gap between chunks is 2 pixels.</summary>
    public static int ChunkSpaceThickness => 2;

    /// <summary>Always false: Basalt never loads a visual style, so ProgressBarRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawHorizontalBar(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        DrawBar(g, bounds);
    }

    public static void DrawVerticalBar(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        DrawBar(g, bounds);
    }

    public static void DrawHorizontalChunks(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        using var brush = new SolidBrush(SystemColors.Highlight);
        var step = ChunkThickness + ChunkSpaceThickness;

        for (var x = bounds.Left; x < bounds.Right; x += step)
        {
            var width = Math.Min(ChunkThickness, bounds.Right - x);
            if (width <= 0) break;
            g.FillRectangle(brush, x, bounds.Top, width, bounds.Height);
        }
    }

    public static void DrawVerticalChunks(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        using var brush = new SolidBrush(SystemColors.Highlight);
        var step = ChunkThickness + ChunkSpaceThickness;

        for (var y = bounds.Bottom - ChunkThickness; y > bounds.Top - step; y -= step)
        {
            var top = Math.Max(y, bounds.Top);
            var height = Math.Min(ChunkThickness, bounds.Bottom - top);
            if (height <= 0) break;
            g.FillRectangle(brush, bounds.Left, top, bounds.Width, height);
        }
    }

    private static void DrawBar(Graphics g, Rectangle bounds)
    {
        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Sunken);

        using var face = new SolidBrush(SystemColors.Window);
        g.FillRectangle(face, Rectangle.Inflate(bounds, -2, -2));
    }
}
