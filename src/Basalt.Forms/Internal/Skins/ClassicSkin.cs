using System.Drawing;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// The Windows 2000 look: flat colours from SystemColors and the two-line
/// three-dimensional edges of the classic theme.
/// </summary>
internal sealed class ClassicSkin : Skin
{
    public override void DrawNonClientFrame(Graphics graphics, Size outerSize, int style, int exStyle, WindowScrollInfo? horizontal, WindowScrollInfo? vertical, WindowCaption? caption = null)
    {
        var bounds = new Rectangle(Point.Empty, outerSize);

        // A window frame and title bar: only for a form that is not a top-level window.
        if (caption is { } title && (style & WindowStyles.Caption) == WindowStyles.Caption)
        {
            bounds = DrawWindowFrame(graphics, bounds, style, exStyle, title.Active, title.BorderColor);
            bounds = ClassicCaption.Draw(graphics, outerSize, bounds, style, exStyle, title);
        }

        // Outside in, as Windows draws them: border, then the edges inside it.
        if ((style & WindowStyles.Border) != 0 && (style & WindowStyles.Caption) != WindowStyles.Caption)
        {
            DrawEdge(graphics, bounds, SystemColors.WindowFrame, SystemColors.WindowFrame);
            bounds.Inflate(-1, -1);
        }

        if ((exStyle & WindowStyles.ExClientEdge) != 0)
        {
            DrawEdge(graphics, bounds, SystemColors.ControlDark, SystemColors.ControlLightLight);
            bounds.Inflate(-1, -1);
            DrawEdge(graphics, bounds, SystemColors.ControlDarkDark, SystemColors.ControlLight);
            bounds.Inflate(-1, -1);
        }

        if ((exStyle & WindowStyles.ExStaticEdge) != 0)
        {
            DrawEdge(graphics, bounds, SystemColors.ControlDark, SystemColors.ControlLightLight);
            bounds.Inflate(-1, -1);
        }

        DrawScrollBars(graphics, outerSize, style, exStyle, horizontal, vertical);
    }

    /// <summary>
    /// The outer frame of a window: the raised edge (EDGE_RAISED), then the rest of a sizing frame
    /// in the active or inactive border colour, or of a dialog frame in the face colour, or a
    /// single dark line for a plain border. Returns what is inside it.
    /// </summary>
    private static Rectangle DrawWindowFrame(Graphics graphics, Rectangle bounds, int style, int exStyle, bool active, Color customColor)
    {
        var sizing = (style & WindowStyles.ThickFrame) != 0;
        var dialog = (style & WindowStyles.DialogFrame) != 0 || (exStyle & WindowStyles.ExDialogModalFrame) != 0;

        if (!customColor.IsEmpty)
        {
            var width = sizing ? NonClientArea.SizingFrameWidth : dialog ? NonClientArea.FixedFrameWidth : NonClientArea.BorderWidth;
            for (var line = 0; line < width; line++)
            {
                DrawEdge(graphics, bounds, customColor, customColor);
                bounds.Inflate(-1, -1);
            }
            return bounds;
        }

        if (!sizing && !dialog)
        {
            DrawEdge(graphics, bounds, SystemColors.WindowFrame, SystemColors.WindowFrame);
            bounds.Inflate(-NonClientArea.BorderWidth, -NonClientArea.BorderWidth);
            return bounds;
        }

        DrawEdge(graphics, bounds, SystemColors.ControlLight, SystemColors.ControlDarkDark);
        bounds.Inflate(-1, -1);
        DrawEdge(graphics, bounds, SystemColors.ControlLightLight, SystemColors.ControlDark);
        bounds.Inflate(-1, -1);

        var band = (sizing ? NonClientArea.SizingFrameWidth : NonClientArea.FixedFrameWidth) - NonClientArea.EdgeWidth;
        var bandColor = sizing ? (active ? SystemColors.ActiveBorder : SystemColors.InactiveBorder) : SystemColors.Control;
        for (var line = 0; line < band; line++)
        {
            DrawEdge(graphics, bounds, bandColor, bandColor);
            bounds.Inflate(-1, -1);
        }

        return bounds;
    }

    /// <summary>
    /// One line of a three-dimensional edge, as Win32's DrawEdge lays it: the
    /// top and left in one colour up to the last pixel, the bottom and right
    /// in the other over their whole length, so that colour owns the corners.
    /// Filled as one-pixel rectangles, which have no end-point ambiguity.
    /// </summary>
    internal static void DrawEdge(Graphics graphics, Rectangle bounds, Color topLeft, Color bottomRight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        using var first = new SolidBrush(topLeft);
        using var second = new SolidBrush(bottomRight);

        graphics.FillRectangle(first, bounds.Left, bounds.Top, bounds.Width - 1, 1);
        graphics.FillRectangle(first, bounds.Left, bounds.Top, 1, bounds.Height - 1);
        graphics.FillRectangle(second, bounds.Left, bounds.Bottom - 1, bounds.Width, 1);
        graphics.FillRectangle(second, bounds.Right - 1, bounds.Top, 1, bounds.Height);
    }

    /// <summary>The window's own scroll bars, drawn like the ScrollBar control, and the corner where they meet.</summary>
    private static void DrawScrollBars(Graphics graphics, Size outerSize, int style, int exStyle, WindowScrollInfo? horizontal, WindowScrollInfo? vertical)
    {
        var verticalBounds = WindowScrollBars.Vertical(outerSize, style, exStyle);
        if (!verticalBounds.IsEmpty)
        {
            var info = vertical ?? new WindowScrollInfo(0, 0, 0, 0, false);
            var thumb = info.ThumbBounds(ScrollBarPainter.TrackBounds(Orientation.Vertical, verticalBounds), vertical: true);
            ScrollBarPainter.Draw(graphics, Orientation.Vertical, verticalBounds, thumb, ScrollBarPainter.PressedPart.None, info.Enabled);
        }

        var horizontalBounds = WindowScrollBars.Horizontal(outerSize, style, exStyle);
        if (!horizontalBounds.IsEmpty)
        {
            var info = horizontal ?? new WindowScrollInfo(0, 0, 0, 0, false);
            var thumb = info.ThumbBounds(ScrollBarPainter.TrackBounds(Orientation.Horizontal, horizontalBounds), vertical: false);
            ScrollBarPainter.Draw(graphics, Orientation.Horizontal, horizontalBounds, thumb, ScrollBarPainter.PressedPart.None, info.Enabled);
        }

        var corner = WindowScrollBars.Corner(outerSize, style, exStyle);
        if (!corner.IsEmpty)
        {
            using var brush = new SolidBrush(SystemColors.Control);
            graphics.FillRectangle(brush, corner);
        }
    }
}
