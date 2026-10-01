using System.Drawing;
using System.Windows.Forms.VisualStyles;

namespace System.Windows.Forms;

/// <summary>Draws a track bar's track, ticks and thumb in the classic Windows 2000 look.</summary>
public static class TrackBarRenderer
{
    /// <summary>The thumb Basalt's classic TrackBar draws: a flat 11x21 shape pointing along the track.</summary>
    private static readonly Size HorizontalThumbSize = new(11, 21);

    private static readonly Size VerticalThumbSize = new(21, 11);

    /// <summary>Always false: Basalt never loads a visual style, so TrackBarRenderer always draws the classic look.</summary>
    public static bool IsSupported => false;

    public static void DrawHorizontalTrack(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        var groove = new Rectangle(bounds.Left, bounds.Top + bounds.Height / 2 - 2, bounds.Width, 4);
        ControlPaint.DrawBorder3D(g, groove, Border3DStyle.Sunken);
    }

    public static void DrawVerticalTrack(Graphics g, Rectangle bounds)
    {
        ArgumentNullException.ThrowIfNull(g);

        var groove = new Rectangle(bounds.Left + bounds.Width / 2 - 2, bounds.Top, 4, bounds.Height);
        ControlPaint.DrawBorder3D(g, groove, Border3DStyle.Sunken);
    }

    public static void DrawHorizontalTicks(Graphics g, Rectangle bounds, int numTicks, EdgeStyle edgeStyle) =>
        DrawTicks(g, bounds, numTicks, horizontal: true);

    public static void DrawVerticalTicks(Graphics g, Rectangle bounds, int numTicks, EdgeStyle edgeStyle) =>
        DrawTicks(g, bounds, numTicks, horizontal: false);

    public static void DrawHorizontalThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPlainThumb(g, bounds, state);

    public static void DrawVerticalThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPlainThumb(g, bounds, state);

    public static void DrawTopPointingThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPointedThumb(g, bounds, state, PointingTo.Top);

    public static void DrawBottomPointingThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPointedThumb(g, bounds, state, PointingTo.Bottom);

    public static void DrawLeftPointingThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPointedThumb(g, bounds, state, PointingTo.Left);

    public static void DrawRightPointingThumb(Graphics g, Rectangle bounds, TrackBarThumbState state) => DrawPointedThumb(g, bounds, state, PointingTo.Right);

    public static Size GetTopPointingThumbSize(Graphics g, TrackBarThumbState state) => HorizontalThumbSize;

    public static Size GetBottomPointingThumbSize(Graphics g, TrackBarThumbState state) => HorizontalThumbSize;

    public static Size GetLeftPointingThumbSize(Graphics g, TrackBarThumbState state) => VerticalThumbSize;

    public static Size GetRightPointingThumbSize(Graphics g, TrackBarThumbState state) => VerticalThumbSize;

    private enum PointingTo { Top, Bottom, Left, Right }

    private static void DrawTicks(Graphics g, Rectangle bounds, int numTicks, bool horizontal)
    {
        ArgumentNullException.ThrowIfNull(g);
        if (numTicks <= 0) return;

        using var pen = new Pen(SystemColors.ControlDark);

        for (var i = 0; i < numTicks; i++)
        {
            var fraction = numTicks == 1 ? 0f : i / (float)(numTicks - 1);

            if (horizontal)
            {
                var x = bounds.Left + fraction * (bounds.Width - 1);
                g.DrawLine(pen, x, bounds.Top, x, bounds.Bottom);
            }
            else
            {
                var y = bounds.Top + fraction * (bounds.Height - 1);
                g.DrawLine(pen, bounds.Left, y, bounds.Right, y);
            }
        }
    }

    private static void DrawPlainThumb(Graphics g, Rectangle bounds, TrackBarThumbState state)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawButton(g, bounds, ToButtonState(state));
    }

    /// <summary>
    /// The classic thumb is a raised block with its pointing edge marked by a
    /// thin dark line, since the real tapered arrowhead needs a theme's
    /// nine-grid bitmap Basalt does not have.
    /// </summary>
    private static void DrawPointedThumb(Graphics g, Rectangle bounds, TrackBarThumbState state, PointingTo pointingTo)
    {
        ArgumentNullException.ThrowIfNull(g);

        ControlPaint.DrawBorder3D(g, bounds, Border3DStyle.Raised);

        var faceColor = state == TrackBarThumbState.Disabled ? SystemColors.ControlLight : SystemColors.Control;
        using var face = new SolidBrush(faceColor);
        g.FillRectangle(face, Rectangle.Inflate(bounds, -2, -2));

        var accentColor = state == TrackBarThumbState.Disabled ? SystemColors.ControlDark : SystemColors.ControlDarkDark;
        using var pen = new Pen(accentColor);
        switch (pointingTo)
        {
            case PointingTo.Top: g.DrawLine(pen, bounds.Left + 2, bounds.Top + 2, bounds.Right - 3, bounds.Top + 2); break;
            case PointingTo.Bottom: g.DrawLine(pen, bounds.Left + 2, bounds.Bottom - 3, bounds.Right - 3, bounds.Bottom - 3); break;
            case PointingTo.Left: g.DrawLine(pen, bounds.Left + 2, bounds.Top + 2, bounds.Left + 2, bounds.Bottom - 3); break;
            case PointingTo.Right: g.DrawLine(pen, bounds.Right - 3, bounds.Top + 2, bounds.Right - 3, bounds.Bottom - 3); break;
        }
    }

    private static ButtonState ToButtonState(TrackBarThumbState state) => state switch
    {
        TrackBarThumbState.Pressed => ButtonState.Pushed,
        TrackBarThumbState.Disabled => ButtonState.Inactive,
        _ => ButtonState.Normal,
    };
}
