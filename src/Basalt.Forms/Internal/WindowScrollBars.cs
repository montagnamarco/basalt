using System.Drawing;

namespace System.Windows.Forms.Internal;

/// <summary>The state of one of a window's own scroll bars, as SCROLLINFO describes it.</summary>
internal readonly record struct WindowScrollInfo(int Minimum, int Maximum, int Page, int Position, bool Enabled)
{
    /// <summary>The thumb in the track: as long as the page is of the range, where the position puts it.</summary>
    public Rectangle ThumbBounds(Rectangle track, bool vertical)
    {
        var range = Maximum - Minimum + 1;
        var length = vertical ? track.Height : track.Width;
        if (range <= 0 || length <= 0 || !Enabled || Page >= range) return Rectangle.Empty;

        var thumb = Math.Max(length * Math.Max(Page, 1) / range, Skins.ScrollBarPainter.MinimumThumbLength);
        thumb = Math.Min(thumb, length);

        var travel = Math.Max(range - Page, 1);
        var offset = (length - thumb) * (Position - Minimum) / travel;

        return vertical
            ? new Rectangle(track.X, track.Y + offset, track.Width, thumb)
            : new Rectangle(track.X + offset, track.Y, thumb, track.Height);
    }

    /// <summary>The position a thumb dragged to an offset in the track stands for.</summary>
    public int PositionFromThumb(int thumbOffset, Rectangle track, int thumbLength, bool vertical)
    {
        var length = (vertical ? track.Height : track.Width) - thumbLength;
        var travel = Math.Max(Maximum - Minimum + 1 - Page, 0);
        if (length <= 0) return Minimum;

        return Minimum + (int)Math.Round((double)Math.Clamp(thumbOffset, 0, length) * travel / length);
    }
}

/// <summary>Where a window's scroll bars sit inside its frame.</summary>
internal static class WindowScrollBars
{
    /// <summary>The frame without the scroll bars: the borders and edges around them.</summary>
    public static Rectangle InsideBorders(Size outerSize, int style, int exStyle)
    {
        var borders = NonClientArea.FromStyles(style & ~(WindowStyles.VerticalScroll | WindowStyles.HorizontalScroll), exStyle);

        return Rectangle.FromLTRB(borders.Left, borders.Top, outerSize.Width - borders.Right, outerSize.Height - borders.Bottom);
    }

    public static Rectangle Vertical(Size outerSize, int style, int exStyle)
    {
        if ((style & WindowStyles.VerticalScroll) == 0) return Rectangle.Empty;

        var inside = InsideBorders(outerSize, style, exStyle);
        var bottom = (style & WindowStyles.HorizontalScroll) != 0 ? inside.Bottom - NonClientArea.ScrollBarSize : inside.Bottom;

        return Rectangle.FromLTRB(inside.Right - NonClientArea.ScrollBarSize, inside.Top, inside.Right, bottom);
    }

    public static Rectangle Horizontal(Size outerSize, int style, int exStyle)
    {
        if ((style & WindowStyles.HorizontalScroll) == 0) return Rectangle.Empty;

        var inside = InsideBorders(outerSize, style, exStyle);
        var right = (style & WindowStyles.VerticalScroll) != 0 ? inside.Right - NonClientArea.ScrollBarSize : inside.Right;

        return Rectangle.FromLTRB(inside.Left, inside.Bottom - NonClientArea.ScrollBarSize, right, inside.Bottom);
    }

    /// <summary>The square where the two bars meet, filled with the control colour.</summary>
    public static Rectangle Corner(Size outerSize, int style, int exStyle)
    {
        if ((style & WindowStyles.VerticalScroll) == 0 || (style & WindowStyles.HorizontalScroll) == 0) return Rectangle.Empty;

        var inside = InsideBorders(outerSize, style, exStyle);

        return Rectangle.FromLTRB(inside.Right - NonClientArea.ScrollBarSize, inside.Bottom - NonClientArea.ScrollBarSize, inside.Right, inside.Bottom);
    }
}
