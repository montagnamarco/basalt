namespace System.Windows.Forms.Internal;

/// <summary>
/// The frame a window's styles put around its client area: what Win32's
/// <c>AdjustWindowRectEx</c> answers, for the classic look.
/// </summary>
/// <remarks>
/// WinForms derives <c>ClientSize</c> from <c>Size</c> by asking Windows how
/// thick the frame of the control's styles is, so a Panel with
/// <c>BorderStyle.Fixed3D</c> loses two pixels on each side to its sunken
/// edge. The thicknesses are those of the classic (Windows 2000) theme at
/// 96 dpi; a skin draws the frame but does not change its size, or forms
/// would lay out differently from Windows.
/// </remarks>
internal readonly record struct NonClientArea(int Left, int Top, int Right, int Bottom)
{
    /// <summary>A plain border: WS_BORDER, or WS_EX_STATICEDGE.</summary>
    public const int BorderWidth = 1;

    /// <summary>A three-dimensional edge: WS_EX_CLIENTEDGE, WS_EX_WINDOWEDGE.</summary>
    public const int EdgeWidth = 2;

    /// <summary>A dialog frame, WS_DLGFRAME without a sizing frame: SM_CXFIXEDFRAME.</summary>
    public const int FixedFrameWidth = 3;

    /// <summary>A sizing frame, WS_THICKFRAME: SM_CXSIZEFRAME.</summary>
    public const int SizingFrameWidth = 4;

    /// <summary>
    /// The title bar with the line under it, SM_CYCAPTION, and the smaller one
    /// of tool windows, SM_CYSMCAPTION: 18 and 15 pixels of bar in the classic
    /// theme, plus one. Not yet compared with a Windows 2000 machine.
    /// </summary>
    public const int CaptionHeight = 19;

    public const int ToolCaptionHeight = 16;

    /// <summary>The width of a vertical scroll bar and the height of a horizontal one: SM_CXVSCROLL.</summary>
    public const int ScrollBarSize = 16;

    public static NonClientArea None => default;

    public int Horizontal => Left + Right;

    public int Vertical => Top + Bottom;

    /// <summary>The frame the styles describe, scroll bars included when their bits are set.</summary>
    public static NonClientArea FromStyles(int style, int exStyle)
    {
        var frame = FrameWidth(style, exStyle);
        var edge = (exStyle & WindowStyles.ExClientEdge) != 0 ? EdgeWidth : 0;
        if ((exStyle & WindowStyles.ExStaticEdge) != 0) edge += BorderWidth;

        var side = frame + edge;
        var top = side;

        if ((style & WindowStyles.Caption) == WindowStyles.Caption)
        {
            top += (exStyle & WindowStyles.ExToolWindow) != 0 ? ToolCaptionHeight : CaptionHeight;
        }

        var right = side;
        var bottom = side;
        if ((style & WindowStyles.VerticalScroll) != 0) right += ScrollBarSize;
        if ((style & WindowStyles.HorizontalScroll) != 0) bottom += ScrollBarSize;

        // A scroll bar on the left in right-to-left layouts.
        if ((exStyle & WindowStyles.ExLeftScrollBar) != 0 && (style & WindowStyles.VerticalScroll) != 0)
        {
            return new NonClientArea(side + ScrollBarSize, top, side, bottom);
        }

        return new NonClientArea(side, top, right, bottom);
    }

    /// <summary>
    /// The outer frame alone. A caption implies a border, a sizing frame
    /// replaces the dialog frame, and the window edge of a top-level window
    /// is part of the frame Windows already counts.
    /// </summary>
    private static int FrameWidth(int style, int exStyle)
    {
        if ((style & WindowStyles.ThickFrame) != 0) return SizingFrameWidth;

        if ((style & WindowStyles.DialogFrame) != 0 || (exStyle & WindowStyles.ExDialogModalFrame) != 0) return FixedFrameWidth;

        if ((style & WindowStyles.Border) != 0) return BorderWidth;

        return (exStyle & WindowStyles.ExWindowEdge) != 0 ? EdgeWidth : 0;
    }
}
