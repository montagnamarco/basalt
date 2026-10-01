using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms;

public static partial class ControlPaint
{
    public static void DrawButton(Graphics graphics, Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        using var face = new SolidBrush(SystemColors.Control);

        // Flat beats Pushed beats Inactive beats Normal: a flat button stays a
        // single dark line no matter its other state flags, and an inactive
        // one is drawn as a face with no bevel at all.
        if ((state & ButtonState.Flat) != 0)
        {
            using var pen = new Pen(SystemColors.ControlDark);
            graphics.DrawRectangle(pen, rectangle.Left, rectangle.Top, rectangle.Width - 1, rectangle.Height - 1);
            graphics.FillRectangle(face, Shrink(rectangle, 1));
        }
        else if ((state & ButtonState.Pushed) != 0)
        {
            DrawBorder3DCore(graphics, rectangle, Border3DStyle.Sunken, AllEdges);
            graphics.FillRectangle(face, Shrink(rectangle, 2));
        }
        else if ((state & ButtonState.Inactive) != 0)
        {
            graphics.FillRectangle(face, rectangle);
        }
        else
        {
            DrawBorder3DCore(graphics, rectangle, Border3DStyle.Raised, AllEdges);
            graphics.FillRectangle(face, Shrink(rectangle, 2));
        }
    }

    public static void DrawButton(Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawButton(graphics, new Rectangle(x, y, width, height), state);

    /// <summary>The classic check box is always 13x13, at the rectangle's top-left corner, whatever size the caller passed.</summary>
    private const int CheckBoxSize = 13;

    public static void DrawCheckBox(Graphics graphics, Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var box = new Rectangle(rectangle.X, rectangle.Y, CheckBoxSize, CheckBoxSize);
        var interiorColor = (state & (ButtonState.Pushed | ButtonState.Inactive)) != 0 ? SystemColors.Control : SystemColors.Window;

        Rectangle interior;

        if ((state & ButtonState.Flat) != 0)
        {
            using var pen = new Pen(SystemColors.ControlDark);
            graphics.DrawRectangle(pen, box.Left, box.Top, box.Width - 1, box.Height - 1);
            interior = Shrink(box, 1);
        }
        else
        {
            DrawBorder3DCore(graphics, box, Border3DStyle.Sunken, AllEdges);
            interior = Shrink(box, 2);
        }

        using var interiorBrush = new SolidBrush(interiorColor);
        graphics.FillRectangle(interiorBrush, interior);

        if ((state & ButtonState.Checked) != 0)
        {
            var glyphColor = (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText;
            DrawCheckGlyph(graphics, interior, glyphColor);
        }
    }

    public static void DrawCheckBox(Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawCheckBox(graphics, new Rectangle(x, y, width, height), state);

    /// <summary>
    /// A tick made of two strokes: a short one down to the low point, a long
    /// one back up to the top-right - the usual two-stroke check mark, sized
    /// to whatever interior it is given.
    /// </summary>
    private static void DrawCheckGlyph(Graphics graphics, Rectangle area, Color color)
    {
        if (area.Width <= 1 || area.Height <= 1) return;

        using var pen = new Pen(color, Math.Max(area.Width / 5, 1));

        var low = new Point(area.Left + area.Width / 3, area.Bottom - area.Height / 6 - 1);
        var start = new Point(area.Left + area.Width / 8, area.Top + area.Height / 2);
        var end = new Point(area.Right - area.Width / 8 - 1, area.Top + area.Height / 6);

        graphics.DrawLine(pen, start, low);
        graphics.DrawLine(pen, low, end);
    }

    public static void DrawMixedCheckBox(Graphics graphics, Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var box = new Rectangle(rectangle.X, rectangle.Y, CheckBoxSize, CheckBoxSize);

        DrawBorder3DCore(graphics, box, Border3DStyle.Sunken, AllEdges);
        var interior = Shrink(box, 2);

        // The indeterminate state's "light pattern": window and face colour
        // dithered pixel by pixel, the classic look of a mixed check box.
        FillDithered(graphics, interior, SystemColors.Window, SystemColors.Control);

        if ((state & ButtonState.Checked) != 0)
        {
            var glyphColor = (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlDark;
            DrawCheckGlyph(graphics, interior, glyphColor);
        }
    }

    public static void DrawMixedCheckBox(Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawMixedCheckBox(graphics, new Rectangle(x, y, width, height), state);

    private static void FillDithered(Graphics graphics, Rectangle area, Color a, Color b)
    {
        using var brushA = new SolidBrush(a);
        using var brushB = new SolidBrush(b);

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                graphics.FillRectangle((x + y) % 2 == 0 ? brushA : brushB, x, y, 1, 1);
            }
        }
    }

    private const int RadioButtonSize = 12;

    public static void DrawRadioButton(Graphics graphics, Rectangle rectangle, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);

        var box = new Rectangle(rectangle.X, rectangle.Y, RadioButtonSize, RadioButtonSize);
        var interiorColor = (state & (ButtonState.Pushed | ButtonState.Inactive)) != 0 ? SystemColors.Control : SystemColors.Window;

        using var fill = new SolidBrush(interiorColor);
        graphics.FillEllipse(fill, box);

        // Two concentric sunken rings, the same colours DrawBorder3D's Sunken
        // style uses for a square edge, bent into a circle instead.
        DrawBevelArc(graphics, box, SystemColors.ControlDark, SystemColors.ControlLightLight);
        DrawBevelArc(graphics, Shrink(box, 1), SystemColors.ControlDarkDark, SystemColors.ControlLight);

        if ((state & ButtonState.Checked) != 0)
        {
            var dotColor = (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText;
            var dot = new Rectangle(box.Left + box.Width / 2 - 2, box.Top + box.Height / 2 - 2, 4, 4);

            using var dotBrush = new SolidBrush(dotColor);
            graphics.FillEllipse(dotBrush, dot);
        }
    }

    public static void DrawRadioButton(Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawRadioButton(graphics, new Rectangle(x, y, width, height), state);

    private static void DrawBevelArc(Graphics graphics, Rectangle box, Color topLeft, Color bottomRight)
    {
        using var first = new Pen(topLeft);
        using var second = new Pen(bottomRight);

        // 135..315 degrees sweeps the upper-right point, over the top, down
        // the left side, to the lower-left point; the other half is the rest.
        graphics.DrawArc(first, box, 135f, 180f);
        graphics.DrawArc(second, box, 315f, 180f);
    }

    public static void DrawScrollButton(Graphics graphics, Rectangle rectangle, ScrollButton button, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        var pushed = (state & ButtonState.Pushed) != 0;

        DrawBorder3DCore(graphics, rectangle, pushed ? Border3DStyle.Sunken : Border3DStyle.Raised, AllEdges);

        using var face = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(face, Shrink(rectangle, 2));

        var arrowColor = (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText;
        var glyphArea = Shrink(rectangle, 4);
        if (pushed) glyphArea.Offset(1, 1);

        using var brush = new SolidBrush(arrowColor);
        graphics.FillPolygon(brush, ArrowPoints(glyphArea, button));
    }

    public static void DrawScrollButton(Graphics graphics, int x, int y, int width, int height, ScrollButton button, ButtonState state) =>
        DrawScrollButton(graphics, new Rectangle(x, y, width, height), button, state);

    public static void DrawComboButton(Graphics graphics, Rectangle rectangle, ButtonState state) =>
        DrawScrollButton(graphics, rectangle, ScrollButton.Down, state);

    public static void DrawComboButton(Graphics graphics, int x, int y, int width, int height, ButtonState state) =>
        DrawComboButton(graphics, new Rectangle(x, y, width, height), state);

    /// <summary>A filled isoceles triangle pointing the way the given arrow does, centred in the area.</summary>
    private static Point[] ArrowPoints(Rectangle area, ScrollButton button)
    {
        var cx = area.Left + area.Width / 2;
        var cy = area.Top + area.Height / 2;
        var half = Math.Max(Math.Min(area.Width, area.Height) / 2, 1);

        return button switch
        {
            ScrollButton.Up => [new Point(cx - half, cy + half), new Point(cx + half, cy + half), new Point(cx, cy - half)],
            ScrollButton.Down => [new Point(cx - half, cy - half), new Point(cx + half, cy - half), new Point(cx, cy + half)],
            ScrollButton.Left => [new Point(cx + half, cy - half), new Point(cx + half, cy + half), new Point(cx - half, cy)],
            ScrollButton.Right => [new Point(cx - half, cy - half), new Point(cx - half, cy + half), new Point(cx + half, cy)],
            _ => [],
        };
    }

    public static void DrawCaptionButton(Graphics graphics, Rectangle rectangle, CaptionButton button, ButtonState state)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (rectangle.Width <= 0 || rectangle.Height <= 0) return;

        var pushed = (state & ButtonState.Pushed) != 0;

        DrawBorder3DCore(graphics, rectangle, pushed ? Border3DStyle.Sunken : Border3DStyle.Raised, AllEdges);

        using var face = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(face, Shrink(rectangle, 2));

        var glyphColor = (state & ButtonState.Inactive) != 0 ? SystemColors.GrayText : SystemColors.ControlText;
        var glyph = Shrink(rectangle, 4);
        if (pushed) glyph.Offset(1, 1);
        if (glyph.Width <= 0 || glyph.Height <= 0) return;

        using var pen = new Pen(glyphColor);

        switch (button)
        {
            case CaptionButton.Close:
                graphics.DrawLine(pen, glyph.Left, glyph.Top, glyph.Right, glyph.Bottom);
                graphics.DrawLine(pen, glyph.Left, glyph.Bottom, glyph.Right, glyph.Top);
                break;

            case CaptionButton.Minimize:
                graphics.DrawLine(pen, glyph.Left, glyph.Bottom - 1, glyph.Right, glyph.Bottom - 1);
                break;

            case CaptionButton.Maximize:
                graphics.DrawRectangle(pen, glyph.Left, glyph.Top, glyph.Width - 1, glyph.Height - 1);
                break;

            case CaptionButton.Restore:
                DrawRestoreGlyph(graphics, pen, glyph);
                break;

            case CaptionButton.Help:
                DrawHelpGlyph(graphics, glyph, glyphColor);
                break;
        }
    }

    public static void DrawCaptionButton(Graphics graphics, int x, int y, int width, int height, CaptionButton button, ButtonState state) =>
        DrawCaptionButton(graphics, new Rectangle(x, y, width, height), button, state);

    /// <summary>Two overlapping squares: the back one erased where the front one covers it, as Windows draws Restore.</summary>
    private static void DrawRestoreGlyph(Graphics graphics, Pen pen, Rectangle glyph)
    {
        var size = Math.Max(glyph.Width, glyph.Height) * 2 / 3;
        if (size <= 0) return;

        var back = new Rectangle(glyph.Left, glyph.Top, size, size);
        var front = new Rectangle(glyph.Right - size, glyph.Bottom - size, size, size);

        graphics.DrawRectangle(pen, back.Left, back.Top, back.Width - 1, back.Height - 1);

        using var eraser = new SolidBrush(SystemColors.Control);
        graphics.FillRectangle(eraser, front);
        graphics.DrawRectangle(pen, front.Left, front.Top, front.Width - 1, front.Height - 1);
    }

    private static void DrawHelpGlyph(Graphics graphics, Rectangle glyph, Color color)
    {
        using var font = new Font(Control.DefaultFont.FontFamily, Math.Max(glyph.Height, 1), GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(color);

        graphics.DrawString("?", font, brush, (RectangleF)glyph, format);
    }
}
