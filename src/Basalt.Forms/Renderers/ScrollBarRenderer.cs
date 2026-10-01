using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a scroll bar's arrows, thumb and track in the classic Windows 2000 look.</summary>
public static class ScrollBarRenderer
{
    private static readonly Size ThumbGripSize = new(5, 4);
    private static readonly Size SizeBoxSizeValue = new(16, 16);

    /// <summary>Always false: Basalt never loads a visual style, so ScrollBarRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawArrowButton(Graphics g, Rectangle bounds, ScrollBarArrowButtonState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        var (button, buttonState) = Decompose(state);
        ControlPaint.DrawScrollButton(g, bounds, button, buttonState);
    }

    public static void DrawHorizontalThumb(Graphics g, Rectangle bounds, ScrollBarState state) => DrawThumb(g, bounds, state);

    public static void DrawVerticalThumb(Graphics g, Rectangle bounds, ScrollBarState state) => DrawThumb(g, bounds, state);

    public static void DrawHorizontalThumbGrip(Graphics g, Rectangle bounds, ScrollBarState state) => DrawGrip(g, bounds, horizontal: true);

    public static void DrawVerticalThumbGrip(Graphics g, Rectangle bounds, ScrollBarState state) => DrawGrip(g, bounds, horizontal: false);

    public static void DrawLeftHorizontalTrack(Graphics g, Rectangle bounds, ScrollBarState state) => DrawTrack(g, bounds);

    public static void DrawRightHorizontalTrack(Graphics g, Rectangle bounds, ScrollBarState state) => DrawTrack(g, bounds);

    public static void DrawLowerVerticalTrack(Graphics g, Rectangle bounds, ScrollBarState state) => DrawTrack(g, bounds);

    public static void DrawUpperVerticalTrack(Graphics g, Rectangle bounds, ScrollBarState state) => DrawTrack(g, bounds);

    public static void DrawSizeBox(Graphics g, Rectangle bounds, ScrollBarSizeBoxState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawSizeGrip(g, SystemColors.Control, bounds);
    }

    public static Size GetSizeBoxSize(Graphics g, ScrollBarState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        return SizeBoxSizeValue;
    }

    public static Size GetThumbGripSize(Graphics g, ScrollBarState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        return ThumbGripSize;
    }

    private static void DrawThumb(Graphics g, Rectangle bounds, ScrollBarState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        var buttonState = state switch
        {
            ScrollBarState.Pressed => ButtonState.Pushed,
            ScrollBarState.Disabled => ButtonState.Inactive,
            _ => ButtonState.Normal,
        };

        ControlPaint.DrawButton(g, bounds, buttonState);
    }

    private static void DrawGrip(Graphics g, Rectangle bounds, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(g);

        var cx = bounds.Left + bounds.Width / 2;
        var cy = bounds.Top + bounds.Height / 2;

        using var dark = new Pen(SystemColors.ControlDark);
        using var light = new Pen(SystemColors.ControlLightLight);

        for (var offset = -4; offset <= 4; offset += 2)
        {
            if (horizontal)
            {
                g.DrawLine(dark, cx + offset, cy - 3, cx + offset, cy + 3);
                g.DrawLine(light, cx + offset + 1, cy - 3, cx + offset + 1, cy + 3);
            }
            else
            {
                g.DrawLine(dark, cx - 3, cy + offset, cx + 3, cy + offset);
                g.DrawLine(light, cx - 3, cy + offset + 1, cx + 3, cy + offset + 1);
            }
        }
    }

    private static void DrawTrack(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        using var brush = new SolidBrush(SystemColors.ScrollBar);
        g.FillRectangle(brush, bounds);
    }

    private static (ScrollButton Button, ButtonState State) Decompose(ScrollBarArrowButtonState state) => state switch
    {
        ScrollBarArrowButtonState.UpNormal => (ScrollButton.Up, ButtonState.Normal),
        ScrollBarArrowButtonState.UpHot => (ScrollButton.Up, ButtonState.Normal),
        ScrollBarArrowButtonState.UpPressed => (ScrollButton.Up, ButtonState.Pushed),
        ScrollBarArrowButtonState.UpDisabled => (ScrollButton.Up, ButtonState.Inactive),
        ScrollBarArrowButtonState.DownNormal => (ScrollButton.Down, ButtonState.Normal),
        ScrollBarArrowButtonState.DownHot => (ScrollButton.Down, ButtonState.Normal),
        ScrollBarArrowButtonState.DownPressed => (ScrollButton.Down, ButtonState.Pushed),
        ScrollBarArrowButtonState.DownDisabled => (ScrollButton.Down, ButtonState.Inactive),
        ScrollBarArrowButtonState.LeftNormal => (ScrollButton.Left, ButtonState.Normal),
        ScrollBarArrowButtonState.LeftHot => (ScrollButton.Left, ButtonState.Normal),
        ScrollBarArrowButtonState.LeftPressed => (ScrollButton.Left, ButtonState.Pushed),
        ScrollBarArrowButtonState.LeftDisabled => (ScrollButton.Left, ButtonState.Inactive),
        ScrollBarArrowButtonState.RightNormal => (ScrollButton.Right, ButtonState.Normal),
        ScrollBarArrowButtonState.RightHot => (ScrollButton.Right, ButtonState.Normal),
        ScrollBarArrowButtonState.RightPressed => (ScrollButton.Right, ButtonState.Pushed),
        ScrollBarArrowButtonState.RightDisabled => (ScrollButton.Right, ButtonState.Inactive),
        _ => (ScrollButton.Up, ButtonState.Normal),
    };
}
