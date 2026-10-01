using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public static partial class ControlPaint
{
    public static void DrawBorder3D(Graphics graphics, Rectangle rectangle) => DrawBorder3D(graphics, rectangle, Border3DStyle.Raised, AllEdges);

    public static void DrawBorder3D(Graphics graphics, Rectangle rectangle, Border3DStyle style) => DrawBorder3D(graphics, rectangle, style, AllEdges);

    public static void DrawBorder3D(Graphics graphics, Rectangle rectangle, Border3DStyle style, Border3DSide sides)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        DrawBorder3DCore(graphics, rectangle, style, sides);
    }

    public static void DrawBorder3D(Graphics graphics, int x, int y, int width, int height) =>
        DrawBorder3D(graphics, new Rectangle(x, y, width, height));

    public static void DrawBorder3D(Graphics graphics, int x, int y, int width, int height, Border3DStyle style) =>
        DrawBorder3D(graphics, new Rectangle(x, y, width, height), style);

    public static void DrawBorder3D(Graphics graphics, int x, int y, int width, int height, Border3DStyle style, Border3DSide sides) =>
        DrawBorder3D(graphics, new Rectangle(x, y, width, height), style, sides);

    /// <summary>
    /// The style's flag bits set a marker (Flat) and two independent rings
    /// (outer, inner), each either raised or sunken; Adjust has no visible
    /// effect here because none of the public overloads pass the rectangle
    /// by reference for it to shrink.
    /// </summary>
    private const Border3DStyle FlatMarker = (Border3DStyle)0x4000;

    private static void DrawBorder3DCore(Graphics graphics, Rectangle bounds, Border3DStyle style, Border3DSide sides)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        if ((style & FlatMarker) != 0)
        {
            FillEdge(graphics, bounds, sides, SystemColors.ControlDark, SystemColors.ControlDark);
            FillMiddle(graphics, Inset(bounds, sides, 1), sides);
            return;
        }

        var outer = style & (Border3DStyle.RaisedOuter | Border3DStyle.SunkenOuter);
        var inner = style & (Border3DStyle.RaisedInner | Border3DStyle.SunkenInner);
        var remaining = bounds;

        if (outer != 0)
        {
            var (topLeft, bottomRight) = outer == Border3DStyle.RaisedOuter
                ? (SystemColors.ControlLightLight, SystemColors.ControlDarkDark)
                : (SystemColors.ControlDark, SystemColors.ControlLightLight);

            FillEdge(graphics, remaining, sides, topLeft, bottomRight);
            remaining = Inset(remaining, sides, 1);
        }

        if (inner != 0)
        {
            var (topLeft, bottomRight) = inner == Border3DStyle.RaisedInner
                ? (SystemColors.ControlLight, SystemColors.ControlDark)
                : (SystemColors.ControlDarkDark, SystemColors.ControlLight);

            FillEdge(graphics, remaining, sides, topLeft, bottomRight);
            remaining = Inset(remaining, sides, 1);
        }

        FillMiddle(graphics, remaining, sides);
    }

    /// <summary>
    /// One 1-pixel-wide ring, drawn as Win32's DrawEdge lays it out: the top
    /// and left lines run short of the far corner, the right and bottom
    /// lines run the full length and so paint over it - the bottom/right
    /// colour ends up owning every corner except the top-left one.
    /// </summary>
    private static void FillEdge(Graphics graphics, Rectangle rect, Border3DSide sides, Color topLeft, Color bottomRight)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var hasRight = (sides & Border3DSide.Right) != 0;
        var hasBottom = (sides & Border3DSide.Bottom) != 0;

        using var first = new SolidBrush(topLeft);
        using var second = new SolidBrush(bottomRight);

        if ((sides & Border3DSide.Top) != 0) graphics.FillRectangle(first, rect.Left, rect.Top, rect.Width - (hasRight ? 1 : 0), 1);
        if ((sides & Border3DSide.Left) != 0) graphics.FillRectangle(first, rect.Left, rect.Top, 1, rect.Height - (hasBottom ? 1 : 0));
        if (hasRight) graphics.FillRectangle(second, rect.Right - 1, rect.Top, 1, rect.Height);
        if (hasBottom) graphics.FillRectangle(second, rect.Left, rect.Bottom - 1, rect.Width, 1);
    }

    /// <summary>Shrinks only the sides that were just drawn, so a side left out of <paramref name="sides"/> keeps its original extent.</summary>
    private static Rectangle Inset(Rectangle rect, Border3DSide sides, int amount)
    {
        var left = rect.Left + ((sides & Border3DSide.Left) != 0 ? amount : 0);
        var top = rect.Top + ((sides & Border3DSide.Top) != 0 ? amount : 0);
        var right = rect.Right - ((sides & Border3DSide.Right) != 0 ? amount : 0);
        var bottom = rect.Bottom - ((sides & Border3DSide.Bottom) != 0 ? amount : 0);

        return Rectangle.FromLTRB(left, top, Math.Max(left, right), Math.Max(top, bottom));
    }

    /// <summary>Win32's DrawEdge fills what is left in the middle with the button face when BF_MIDDLE is asked for.</summary>
    private static void FillMiddle(Graphics graphics, Rectangle rect, Border3DSide sides)
    {
        if ((sides & Border3DSide.Middle) == 0 || rect.Width <= 0 || rect.Height <= 0) return;

        using var brush = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(brush, rect);
    }

    public static void DrawBorder(Graphics graphics, Rectangle bounds, Color color, ButtonBorderStyle style) =>
        DrawBorder(graphics, bounds, color, 1, style, color, 1, style, color, 1, style, color, 1, style);

    public static void DrawBorder(
        Graphics graphics,
        Rectangle bounds,
        Color leftColor,
        int leftWidth,
        ButtonBorderStyle leftStyle,
        Color topColor,
        int topWidth,
        ButtonBorderStyle topStyle,
        Color rightColor,
        int rightWidth,
        ButtonBorderStyle rightStyle,
        Color bottomColor,
        int bottomWidth,
        ButtonBorderStyle bottomStyle)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        DrawBorderSide(graphics, new Rectangle(bounds.Left, bounds.Top, bounds.Width, topWidth), topColor, topWidth, topStyle, horizontal: true);
        DrawBorderSide(graphics, new Rectangle(bounds.Left, bounds.Bottom - bottomWidth, bounds.Width, bottomWidth), bottomColor, bottomWidth, bottomStyle, horizontal: true);
        DrawBorderSide(graphics, new Rectangle(bounds.Left, bounds.Top, leftWidth, bounds.Height), leftColor, leftWidth, leftStyle, horizontal: false);
        DrawBorderSide(graphics, new Rectangle(bounds.Right - rightWidth, bounds.Top, rightWidth, bounds.Height), rightColor, rightWidth, rightStyle, horizontal: false);
    }

    private static void DrawBorderSide(Graphics graphics, Rectangle rect, Color color, int width, ButtonBorderStyle style, bool horizontal)
    {
        if (style == ButtonBorderStyle.None || width <= 0 || rect.Width <= 0 || rect.Height <= 0) return;

        switch (style)
        {
            case ButtonBorderStyle.Solid:
                using (var brush = new SolidBrush(color)) graphics.FillRectangle(brush, rect);
                break;

            case ButtonBorderStyle.Dotted:
                DrawDashedSide(graphics, rect, color, width, DashStyle.Dot, horizontal);
                break;

            case ButtonBorderStyle.Dashed:
                DrawDashedSide(graphics, rect, color, width, DashStyle.Dash, horizontal);
                break;

            case ButtonBorderStyle.Inset:
                DrawBevelSide(graphics, rect, color, width, horizontal, darkFirst: true);
                break;

            case ButtonBorderStyle.Outset:
                DrawBevelSide(graphics, rect, color, width, horizontal, darkFirst: false);
                break;
        }
    }

    /// <summary>A single dashed or dotted line through the middle of the side's thickness.</summary>
    private static void DrawDashedSide(Graphics graphics, Rectangle rect, Color color, int width, DashStyle dash, bool horizontal)
    {
        using var pen = new Pen(color, width) { DashStyle = dash };

        if (horizontal)
        {
            var y = rect.Top + rect.Height / 2f;
            graphics.DrawLine(pen, rect.Left, y, rect.Right, y);
        }
        else
        {
            var x = rect.Left + rect.Width / 2f;
            graphics.DrawLine(pen, x, rect.Top, x, rect.Bottom);
        }
    }

    /// <summary>
    /// Inset/Outset have only one colour to work with, so the bevel comes
    /// from splitting the width in half and shading each half with
    /// <see cref="Dark"/> and <see cref="Light"/> of that same colour.
    /// </summary>
    private static void DrawBevelSide(Graphics graphics, Rectangle rect, Color color, int width, bool horizontal, bool darkFirst)
    {
        var half = Math.Max(width / 2, 1);
        var firstColor = darkFirst ? Dark(color) : Light(color);
        var secondColor = darkFirst ? Light(color) : Dark(color);

        using var firstBrush = new SolidBrush(firstColor);
        using var secondBrush = new SolidBrush(secondColor);

        if (horizontal)
        {
            graphics.FillRectangle(firstBrush, rect.Left, rect.Top, rect.Width, half);
            graphics.FillRectangle(secondBrush, rect.Left, rect.Top + half, rect.Width, Math.Max(rect.Height - half, 0));
        }
        else
        {
            graphics.FillRectangle(firstBrush, rect.Left, rect.Top, half, rect.Height);
            graphics.FillRectangle(secondBrush, rect.Left + half, rect.Top, Math.Max(rect.Width - half, 0), rect.Height);
        }
    }

    public static void DrawVisualStyleBorder(Graphics graphics, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        using var pen = new Pen(SystemColors.ControlDark);
        graphics.DrawRectangle(pen, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }
}
