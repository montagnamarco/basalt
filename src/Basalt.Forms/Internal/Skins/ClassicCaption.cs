using System.Drawing;
using System.Drawing.Drawing2D;

namespace System.Windows.Forms.Internal.Skins;

/// <summary>
/// The Windows 2000 title bar: a left-to-right gradient from the caption
/// colour to its gradient colour, the title in the bold caption font, and the
/// minimize, maximize (or restore) and close buttons on the right.
/// </summary>
internal static class ClassicCaption
{
    /// <summary>Draws the bar inside the frame; returns what lies under it and its one-pixel line.</summary>
    public static Rectangle Draw(Graphics graphics, Size outerSize, Rectangle inside, int style, int exStyle, WindowCaption caption)
    {
        var bar = CaptionLayout.CaptionBounds(outerSize, style, exStyle);
        if (bar.Width <= 0 || bar.Height <= 0) return inside;

        var toolWindow = (exStyle & WindowStyles.ExToolWindow) != 0;
        var start = caption.Active ? SystemColors.ActiveCaption : SystemColors.InactiveCaption;
        var end = caption.Active ? SystemColors.GradientActiveCaption : SystemColors.GradientInactiveCaption;
        if (!caption.BackColor.IsEmpty) start = end = caption.BackColor;

        using (var brush = new LinearGradientBrush(new Rectangle(bar.X, bar.Y, Math.Max(bar.Width, 1), bar.Height), start, end, LinearGradientMode.Horizontal))
        {
            graphics.FillRectangle(brush, bar);
        }

        // The line under the bar belongs to the frame, in the face colour.
        using (var line = new SolidBrush(SystemColors.Control))
        {
            graphics.FillRectangle(line, bar.X, bar.Bottom, bar.Width, 1);
        }

        var textRight = DrawButtons(graphics, bar, caption, toolWindow);
        var textLeft = DrawIcon(graphics, bar, caption, toolWindow);
        DrawTitle(graphics, bar, textLeft, textRight, caption, toolWindow);

        return Rectangle.FromLTRB(inside.Left, bar.Bottom + 1, inside.Right, inside.Bottom);
    }

    /// <summary>
    /// The system menu icon: Windows draws it where the close box is drawn on
    /// the other side, only for a window that has a system menu and is not a
    /// tool window (whose small caption has no room for it). Returns where the
    /// title must start.
    /// </summary>
    private static int DrawIcon(Graphics graphics, Rectangle bar, WindowCaption caption, bool toolWindow)
    {
        if (toolWindow || !caption.HasSystemMenu || caption.Icon is not { } icon) return bar.Left + 2;

        var size = Math.Max(bar.Height - 6, 1);
        var top = bar.Top + (bar.Height - size) / 2;
        var left = bar.Left + 2;

        graphics.DrawIcon(icon, new Rectangle(left, top, size, size));

        return left + size + 2;
    }

    /// <summary>Draws the buttons the window has; returns where the title must stop.</summary>
    private static int DrawButtons(Graphics graphics, Rectangle bar, WindowCaption caption, bool toolWindow)
    {
        if (!caption.HasSystemMenu) return bar.Right - 2;

        var close = CaptionLayout.ButtonBounds(bar, CaptionHitArea.CloseButton, toolWindow);
        if (close.IsEmpty) return bar.Right - 2;

        ControlPaint.DrawCaptionButton(graphics, close, CaptionButton.Close, StateOf(caption, CaptionHitArea.CloseButton, enabled: true));
        var textRight = close.Left;

        // Windows shows both boxes when either is wanted, the missing one greyed out.
        if (!toolWindow && (caption.HasMinimizeBox || caption.HasMaximizeBox))
        {
            var maximize = CaptionLayout.ButtonBounds(bar, CaptionHitArea.MaximizeButton, toolWindow);
            var minimize = CaptionLayout.ButtonBounds(bar, CaptionHitArea.MinimizeButton, toolWindow);
            if (maximize.IsEmpty || minimize.IsEmpty) return textRight - 2;

            var maximizeGlyph = caption.Maximized || caption.Minimized ? CaptionButton.Restore : CaptionButton.Maximize;
            ControlPaint.DrawCaptionButton(graphics, maximize, maximizeGlyph, StateOf(caption, CaptionHitArea.MaximizeButton, caption.HasMaximizeBox));

            var minimizeGlyph = caption.Minimized ? CaptionButton.Restore : CaptionButton.Minimize;
            ControlPaint.DrawCaptionButton(graphics, minimize, minimizeGlyph, StateOf(caption, CaptionHitArea.MinimizeButton, caption.HasMinimizeBox));

            textRight = minimize.Left;
        }

        return textRight - 2;
    }

    private static ButtonState StateOf(WindowCaption caption, CaptionHitArea button, bool enabled)
    {
        if (!enabled) return ButtonState.Inactive;

        return caption.Pressed == button ? ButtonState.Pushed : ButtonState.Normal;
    }

    private static void DrawTitle(Graphics graphics, Rectangle bar, int left, int right, WindowCaption caption, bool toolWindow)
    {
        if (string.IsNullOrEmpty(caption.Text)) return;

        var bounds = Rectangle.FromLTRB(left, bar.Top, Math.Max(right, left), bar.Bottom);
        var font = toolWindow ? SystemFonts.SmallCaptionFont : SystemFonts.CaptionFont;
        var color = caption.Active ? SystemColors.ActiveCaptionText : SystemColors.InactiveCaptionText;
        if (!caption.TextColor.IsEmpty) color = caption.TextColor;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        TextRenderer.DrawText(graphics, caption.Text, font, bounds, color, flags);
    }
}
