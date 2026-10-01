namespace System.Windows.Forms.Internal;

/// <summary>
/// The Win32 window style bits that <see cref="CreateParams"/> carries.
/// </summary>
/// <remarks>
/// There is no Win32 window underneath, but WinForms decides a control's
/// border, its client rectangle and its scroll bars from these bits, and
/// controls written for it set them in <c>CreateParams</c> overrides. Reading
/// them is what keeps a third-party control's frame the size it was on
/// Windows. Values from the Windows SDK, winuser.h.
/// </remarks>
internal static class WindowStyles
{
    public const int Child = 0x40000000;
    public const int Popup = unchecked((int)0x80000000);
    public const int Visible = 0x10000000;
    public const int Disabled = 0x08000000;
    public const int ClipSiblings = 0x04000000;
    public const int ClipChildren = 0x02000000;
    public const int Caption = 0x00C00000;
    public const int Border = 0x00800000;
    public const int DialogFrame = 0x00400000;
    public const int VerticalScroll = 0x00200000;
    public const int HorizontalScroll = 0x00100000;
    public const int SystemMenu = 0x00080000;
    public const int ThickFrame = 0x00040000;
    public const int MinimizeBox = 0x00020000;
    public const int MaximizeBox = 0x00010000;
    public const int TabStop = 0x00010000;

    public const int ExDialogModalFrame = 0x00000001;
    public const int ExTopmost = 0x00000008;
    public const int ExTransparent = 0x00000020;
    public const int ExToolWindow = 0x00000080;
    public const int ExWindowEdge = 0x00000100;
    public const int ExClientEdge = 0x00000200;
    public const int ExContextHelp = 0x00000400;
    public const int ExRight = 0x00001000;
    public const int ExRtlReading = 0x00002000;
    public const int ExLeftScrollBar = 0x00004000;
    public const int ExControlParent = 0x00010000;
    public const int ExStaticEdge = 0x00020000;
    public const int ExAppWindow = 0x00040000;
    public const int ExLayered = 0x00080000;
    public const int ExLayoutRtl = 0x00400000;
    public const int ExComposited = 0x02000000;
}
