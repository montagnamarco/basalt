using System.Drawing;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// Draws a classic Windows 2000 scroll bar: two arrow buttons, a dithered
/// track and a raised thumb. Shared by <see cref="ScrollBar"/> and meant for
/// a window's own scroll bars too, which have no control of their own to
/// paint from.
/// </summary>
internal static class ScrollBarPainter
{
    /// <summary>Which part, if any, is pressed and so drawn sunken.</summary>
    internal enum PressedPart
    {
        None,
        LineUpButton,
        LineDownButton,
        Thumb,
    }

    /// <summary>The square size of each arrow button: the same width Windows gives a scroll bar.</summary>
    internal const int ButtonSize = NonClientArea.ScrollBarSize;

    /// <summary>The smallest a thumb ever shrinks to, so it never disappears under a tiny LargeChange.</summary>
    internal const int MinimumThumbLength = 8;

    /// <summary>The up/left arrow button, at the start of the bar.</summary>
    internal static Rectangle LineUpButtonBounds(Orientation orientation, Rectangle bounds)
    {
        return orientation == Orientation.Horizontal
            ? new Rectangle(bounds.Left, bounds.Top, Math.Min(ButtonSize, bounds.Width), bounds.Height)
            : new Rectangle(bounds.Left, bounds.Top, bounds.Width, Math.Min(ButtonSize, bounds.Height));
    }

    /// <summary>The down/right arrow button, at the end of the bar.</summary>
    internal static Rectangle LineDownButtonBounds(Orientation orientation, Rectangle bounds)
    {
        var size = orientation == Orientation.Horizontal ? Math.Min(ButtonSize, bounds.Width) : Math.Min(ButtonSize, bounds.Height);

        return orientation == Orientation.Horizontal
            ? new Rectangle(bounds.Right - size, bounds.Top, size, bounds.Height)
            : new Rectangle(bounds.Left, bounds.Bottom - size, bounds.Width, size);
    }

    /// <summary>The track between the two buttons, where the thumb travels.</summary>
    internal static Rectangle TrackBounds(Orientation orientation, Rectangle bounds)
    {
        var up = LineUpButtonBounds(orientation, bounds);
        var down = LineDownButtonBounds(orientation, bounds);

        return orientation == Orientation.Horizontal
            ? Rectangle.FromLTRB(up.Right, bounds.Top, down.Left, bounds.Bottom)
            : Rectangle.FromLTRB(bounds.Left, up.Bottom, bounds.Right, down.Top);
    }

    /// <summary>Draws the whole bar: the dithered track, the two buttons, and the thumb on top of it.</summary>
    internal static void Draw(Graphics graphics, Orientation orientation, Rectangle bounds, Rectangle thumbBounds, PressedPart pressed, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        DrawTrack(graphics, TrackBounds(orientation, bounds));

        var up = LineUpButtonBounds(orientation, bounds);
        var down = LineDownButtonBounds(orientation, bounds);
        var upArrow = orientation == Orientation.Horizontal ? ScrollButton.Left : ScrollButton.Up;
        var downArrow = orientation == Orientation.Horizontal ? ScrollButton.Right : ScrollButton.Down;

        ControlPaint.DrawScrollButton(graphics, up, upArrow, ArrowState(pressed == PressedPart.LineUpButton, enabled));
        ControlPaint.DrawScrollButton(graphics, down, downArrow, ArrowState(pressed == PressedPart.LineDownButton, enabled));

        if (!thumbBounds.IsEmpty) DrawThumb(graphics, thumbBounds, pressed == PressedPart.Thumb);
    }

    private static ButtonState ArrowState(bool pushed, bool enabled)
    {
        var state = pushed ? ButtonState.Pushed : ButtonState.Normal;
        if (!enabled) state |= ButtonState.Inactive;

        return state;
    }

    /// <summary>The track's classic look: ControlLightLight and Control, dithered pixel by pixel.</summary>
    private static void DrawTrack(Graphics graphics, Rectangle track)
    {
        if (track.Width <= 0 || track.Height <= 0) return;

        using var light = new SolidBrush(SystemColors.ControlLightLight);
        using var face = new SolidBrush(SystemColors.Control);

        for (var y = track.Top; y < track.Bottom; y++)
        {
            for (var x = track.Left; x < track.Right; x++)
            {
                graphics.FillRectangle((x + y) % 2 == 0 ? light : face, x, y, 1, 1);
            }
        }
    }

    /// <summary>The thumb: a raised button face, sunken while it is being dragged.</summary>
    private static void DrawThumb(Graphics graphics, Rectangle thumb, bool pushed)
    {
        ControlPaint.DrawButton(graphics, thumb, pushed ? ButtonState.Pushed : ButtonState.Normal);
    }
}
