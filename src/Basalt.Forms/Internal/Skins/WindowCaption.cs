using System.Drawing;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// What a title bar drawn inside the canvas shows: the caption of a form that
/// is not a top-level window (an MDI child, or a form placed in a panel),
/// whose frame Windows would draw itself. A top-level form's title bar
/// belongs to the native window and never comes here.
/// </summary>
internal readonly record struct WindowCaption(
    string Text,
    bool Active,
    bool HasSystemMenu,
    bool HasMinimizeBox,
    bool HasMaximizeBox,
    bool Maximized,
    bool Minimized,
    CaptionHitArea Pressed);

/// <summary>Where on a frame a point falls: what a press there does.</summary>
internal enum CaptionHitArea
{
    None,
    Caption,
    MinimizeButton,
    MaximizeButton,
    CloseButton,
    Left,
    Right,
    Top,
    Bottom,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}

/// <summary>
/// The geometry of the classic title bar and frame, in the window's outer
/// coordinates. Drawing and hit-testing both read it, so a click always lands
/// on the part that was drawn there.
/// </summary>
internal static class CaptionLayout
{
    /// <summary>How far in from a corner a press on the sizing frame resizes both ways.</summary>
    private const int CornerSize = 16;

    /// <summary>The title bar, without the one-pixel line under it.</summary>
    public static Rectangle CaptionBounds(Size outerSize, int style, int exStyle)
    {
        if ((style & WindowStyles.Caption) != WindowStyles.Caption) return Rectangle.Empty;

        var frame = NonClientArea.FromStyles(style & ~(WindowStyles.Caption | WindowStyles.VerticalScroll | WindowStyles.HorizontalScroll) | WindowStyles.Border, exStyle & ~(WindowStyles.ExClientEdge | WindowStyles.ExStaticEdge));
        var height = ((exStyle & WindowStyles.ExToolWindow) != 0 ? NonClientArea.ToolCaptionHeight : NonClientArea.CaptionHeight) - 1;

        return new Rectangle(frame.Left, frame.Top, Math.Max(outerSize.Width - frame.Horizontal, 0), height);
    }

    /// <summary>
    /// A caption button's place, right to left as Windows lays them: close at
    /// the end, a two-pixel gap, then maximize and minimize side by side.
    /// </summary>
    public static Rectangle ButtonBounds(Rectangle caption, CaptionHitArea button, bool toolWindow)
    {
        if (caption.IsEmpty) return Rectangle.Empty;

        var width = caption.Height - 2;
        var height = caption.Height - 4;
        var top = caption.Top + 2;
        var closeLeft = caption.Right - 2 - width;

        var bounds = button switch
        {
            CaptionHitArea.CloseButton => new Rectangle(closeLeft, top, width, height),
            CaptionHitArea.MaximizeButton when !toolWindow => new Rectangle(closeLeft - 2 - width, top, width, height),
            CaptionHitArea.MinimizeButton when !toolWindow => new Rectangle(closeLeft - 2 - 2 * width, top, width, height),
            _ => Rectangle.Empty,
        };

        // A title bar too narrow for a button drops it, as Windows does, rather than overlapping.
        return bounds.Left >= caption.Left + 2 ? bounds : Rectangle.Empty;
    }

    /// <summary>Which part of the frame a point in outer coordinates is on.</summary>
    public static CaptionHitArea HitTest(Point point, Size outerSize, int style, int exStyle, WindowCaption caption)
    {
        var outer = new Rectangle(Point.Empty, outerSize);
        if (!outer.Contains(point)) return CaptionHitArea.None;

        var bar = CaptionBounds(outerSize, style, exStyle);
        var toolWindow = (exStyle & WindowStyles.ExToolWindow) != 0;

        if (caption.HasSystemMenu)
        {
            if (ButtonBounds(bar, CaptionHitArea.CloseButton, toolWindow).Contains(point)) return CaptionHitArea.CloseButton;

            if (caption.HasMinimizeBox || caption.HasMaximizeBox)
            {
                if (ButtonBounds(bar, CaptionHitArea.MaximizeButton, toolWindow).Contains(point)) return CaptionHitArea.MaximizeButton;
                if (ButtonBounds(bar, CaptionHitArea.MinimizeButton, toolWindow).Contains(point)) return CaptionHitArea.MinimizeButton;
            }
        }

        if (bar.Contains(point)) return CaptionHitArea.Caption;

        // Only a sizing frame resizes, and not while the window is maximized or minimized.
        if ((style & WindowStyles.ThickFrame) == 0 || caption.Maximized || caption.Minimized) return CaptionHitArea.None;

        var frame = NonClientArea.SizingFrameWidth;
        var left = point.X < frame;
        var right = point.X >= outerSize.Width - frame;
        var top = point.Y < frame;
        var bottom = point.Y >= outerSize.Height - frame;
        var nearLeft = point.X < CornerSize;
        var nearRight = point.X >= outerSize.Width - CornerSize;
        var nearTop = point.Y < CornerSize;
        var nearBottom = point.Y >= outerSize.Height - CornerSize;

        if ((top && nearLeft) || (left && nearTop)) return CaptionHitArea.TopLeft;
        if ((top && nearRight) || (right && nearTop)) return CaptionHitArea.TopRight;
        if ((bottom && nearLeft) || (left && nearBottom)) return CaptionHitArea.BottomLeft;
        if ((bottom && nearRight) || (right && nearBottom)) return CaptionHitArea.BottomRight;
        if (left) return CaptionHitArea.Left;
        if (right) return CaptionHitArea.Right;
        if (top) return CaptionHitArea.Top;
        if (bottom) return CaptionHitArea.Bottom;

        return CaptionHitArea.None;
    }
}
