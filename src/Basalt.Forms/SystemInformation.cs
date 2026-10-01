using System.Drawing;
using System.Net.NetworkInformation;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// Measurements and settings a control reads instead of hard-coding, as it
/// would call GetSystemMetrics or SystemParametersInfo on Windows.
/// </summary>
/// <remarks>
/// Every value here is that of the classic (Windows 2000) theme at 96 dpi,
/// the look ControlPaint draws: the same numbers as
/// <see cref="Internal.NonClientArea"/> where that type already names one,
/// so a form's non-client frame and what this class reports about it never
/// disagree. Visual-effect toggles (animation, fades, drop shadows, flat
/// menus, hot-tracking) are reported off, as the classic theme turns them
/// off; none of them is yet checked against an actual Windows 2000 machine.
/// A handful of members ask Win32 for something this platform has no
/// equivalent of (a window handle, a registry setting with no analogue) and
/// are left out rather than guessed.
/// </remarks>
public static class SystemInformation
{
    public static int ActiveWindowTrackingDelay => 0; // Classic theme: "Activate window on hover" is off, so no delay applies.

    public static ArrangeDirection ArrangeDirection => ArrangeDirection.Left; // Classic default: minimized windows arrange left to right.

    public static ArrangeStartingPosition ArrangeStartingPosition => ArrangeStartingPosition.BottomLeft; // Classic default start corner.

    public static BootMode BootMode => BootMode.Normal; // Not running in Windows Safe Mode.

    public static Size Border3DSize => new(NonClientArea.EdgeWidth, NonClientArea.EdgeWidth);

    public static int BorderMultiplierFactor => 1; // Classic theme: a one-pixel border is not multiplied for non-client maths.

    public static Size BorderSize => new(NonClientArea.BorderWidth, NonClientArea.BorderWidth);

    public static Size CaptionButtonSize => new(18, 18); // Classic theme: SM_CXSIZE/SM_CYSIZE at 96 dpi.

    public static int CaptionHeight => NonClientArea.CaptionHeight;

    public static int CaretBlinkTime => 530; // Classic/default Windows caret blink rate in milliseconds.

    public static int CaretWidth => 1; // Classic theme: a one-pixel caret, before the "indicate text editing" accessibility option widens it.

    public static string ComputerName => Environment.MachineName;

    public static Size CursorSize => new(32, 32);

    public static bool DbcsEnabled => false; // No double-byte character set code page installed.

    public static bool DebugOS => false; // A retail, not checked, build of Windows.

    public static Size DoubleClickSize => new(4, 4);

    public static int DoubleClickTime => 500;

    public static bool DragFullWindows => true; // Classic Windows 2000 default: "Show window contents while dragging" is checked.

    public static Size DragSize => new(4, 4);

    public static Size FixedFrameBorderSize => new(NonClientArea.FixedFrameWidth, NonClientArea.FixedFrameWidth);

    public static int FontSmoothingContrast => 0; // Unused while font smoothing (below) is off, as the classic theme leaves it.

    public static int FontSmoothingType => 1; // FE_FONTSMOOTHINGSTANDARD; ClearType is an XP-era default, not Windows 2000's.

    public static Size FrameBorderSize => new(NonClientArea.SizingFrameWidth, NonClientArea.SizingFrameWidth);

    public static bool HighContrast => false; // No accessibility high-contrast theme active.

    public static int HorizontalFocusThickness => 1; // Classic theme: a one-pixel focus rectangle.

    public static int HorizontalResizeBorderThickness => NonClientArea.SizingFrameWidth;

    public static int HorizontalScrollBarArrowWidth => NonClientArea.ScrollBarSize;

    public static int HorizontalScrollBarHeight => NonClientArea.ScrollBarSize;

    public static int HorizontalScrollBarThumbWidth => NonClientArea.ScrollBarSize;

    public static int IconHorizontalSpacing => 75; // Classic desktop icon grid spacing at 96 dpi.

    public static Size IconSize => new(32, 32);

    public static Size IconSpacingSize => new(IconHorizontalSpacing, IconVerticalSpacing);

    public static int IconVerticalSpacing => 75;

    public static bool IsActiveWindowTrackingEnabled => false; // "Activate window on hover" is off by default.

    public static bool IsComboBoxAnimationEnabled => false;

    public static bool IsDropShadowEnabled => false; // Drop shadows under menus and windows are an XP-era default, not Windows 2000's.

    public static bool IsFlatMenuEnabled => false; // Classic theme: raised, three-dimensional menu items, not XP's flat ones.

    public static bool IsFontSmoothingEnabled => false;

    public static bool IsHotTrackingEnabled => false;

    public static bool IsIconTitleWrappingEnabled => true; // Classic theme: a desktop icon's title wraps onto a second line.

    public static bool IsKeyboardPreferred => false;

    public static bool IsListBoxSmoothScrollingEnabled => false;

    public static bool IsMenuAnimationEnabled => false;

    public static bool IsMenuFadeEnabled => false;

    public static bool IsMinimizeRestoreAnimationEnabled => false;

    public static bool IsSelectionFadeEnabled => false;

    public static bool IsSnapToDefaultEnabled => false;

    public static bool IsTitleBarGradientEnabled => false;

    public static bool IsToolTipAnimationEnabled => false;

    public static int KanjiWindowHeight => 0; // No Japanese IME conversion window: not a DBCS system.

    public static int KeyboardDelay => 1; // Classic/default Windows repeat-delay slider position (0-3, 1 default).

    public static int KeyboardSpeed => 31; // Classic/default Windows repeat-speed slider position (0-31, fastest default).

    /// <summary>
    /// The largest a window's frame could be dragged to: the whole virtual
    /// desktop, plus the sizing frame that a maximized window's edges hide
    /// just offscreen.
    /// </summary>
    public static Size MaxWindowTrackSize => new(
        VirtualScreen.Width + FrameBorderSize.Width * 2,
        VirtualScreen.Height + FrameBorderSize.Height * 2);

    public static bool MenuAccessKeysUnderlined => true; // Classic theme: mnemonics are always underlined, before XP's "hide until Alt" default.

    public static Size MenuBarButtonSize => new(MenuHeight, MenuHeight); // A maximized MDI child's menu-bar buttons, square at the menu's height.

    public static Size MenuButtonSize => new(MenuHeight, MenuHeight);

    public static Size MenuCheckSize => new(13, 13); // Classic theme: the checkmark glyph drawn in a checked menu item.

    public static Font MenuFont => SystemFonts.MenuFont;

    public static int MenuHeight => 19; // Classic theme: SM_CYMENU at 96 dpi.

    public static int MenuShowDelay => 400; // Classic/default Windows submenu hover-to-open delay in milliseconds.

    public static bool MidEastEnabled => false; // No Arabic/Hebrew (bidirectional) language support installed.

    public static Size MinWindowTrackSize => new(112, 27); // Classic/default Windows minimum resizable window size at 96 dpi.

    public static Size MinimizedWindowSize => new(160, CaptionHeight); // Classic/default Windows minimized window icon size at 96 dpi.

    public static Size MinimizedWindowSpacingSize => MinimizedWindowSize;

    public static Size MinimumWindowSize => MinWindowTrackSize;

    public static int MonitorCount => Screen.AllScreens.Length;

    public static bool MonitorsSameDisplayFormat => Screen.AllScreens.Select(screen => screen.BitsPerPixel).Distinct().Count() <= 1;

    public static int MouseButtons => 3; // A classic three-button mouse (left, right, middle); wheel and extra buttons are not counted here.

    public static bool MouseButtonsSwapped => false; // The primary (left) button is not swapped for left-handed use.

    public static Size MouseHoverSize => new(4, 4);

    public static int MouseHoverTime => 400;

    public static bool MousePresent => true;

    public static int MouseSpeed => 10; // Classic/default Windows pointer-speed slider position (1-20, middle default).

    public static bool MouseWheelPresent => true;

    public static int MouseWheelScrollDelta => 120; // WHEEL_DELTA: one notch of a mouse wheel.

    public static int MouseWheelScrollLines => 3;

    public static bool NativeMouseWheelSupport => true; // The OS itself delivers wheel messages; no driver-level emulation is needed.

    /// <summary>Whether a network is reachable, as GetSystemMetrics(SM_NETWORK) answers on Windows.</summary>
    public static bool Network => NetworkInterface.GetIsNetworkAvailable();

    public static bool PenWindows => false; // No pen-computing extensions installed.

    public static LeftRightAlignment PopupMenuAlignment => LeftRightAlignment.Left;

    /// <summary>
    /// The current machine's power state. Without a native battery API to
    /// ask, this reports a desktop on line power with no battery, the most
    /// common case for a development machine or a build agent.
    /// </summary>
    public static PowerStatus PowerStatus => new(
        BatteryChargeStatus.NoSystemBattery,
        batteryFullLifetime: -1,
        batteryLifePercent: 1.0f,
        batteryLifeRemaining: -1,
        PowerLineStatus.Online);

    /// <summary>
    /// The size a maximized window on the primary monitor would have: the
    /// working area, plus the sizing frame that hides just offscreen.
    /// </summary>
    public static Size PrimaryMonitorMaximizedWindowSize => new(
        WorkingArea.Width + FrameBorderSize.Width * 2,
        WorkingArea.Height + FrameBorderSize.Height * 2);

    public static Size PrimaryMonitorSize => Screen.PrimaryScreen?.Bounds.Size ?? new Size(1024, 768);

    public static bool RightAlignedMenus => false; // Not a right-to-left system.

    public static ScreenOrientation ScreenOrientation => ScreenOrientation.Angle0;

    public static bool Secure => false; // No mandatory Ctrl+Alt+Del secure desktop requirement configured.

    public static bool ShowSounds => false; // Accessibility "show sounds" captioning is off.

    public static int SizingBorderWidth => NonClientArea.SizingFrameWidth;

    public static Size SmallCaptionButtonSize => new(15, 15); // Classic theme, scaled from CaptionButtonSize by ToolWindowCaptionHeight/CaptionHeight.

    public static Size SmallIconSize => new(16, 16);

    public static bool TerminalServerSession => false; // Not running under Remote Desktop / Terminal Services.

    public static Size ToolWindowCaptionButtonSize => SmallCaptionButtonSize;

    public static int ToolWindowCaptionHeight => NonClientArea.ToolCaptionHeight;

    public static bool UIEffectsEnabled => false; // The master "use visual styles/effects" toggle, off with every effect above.

    public static string UserDomainName => Environment.UserDomainName;

    public static bool UserInteractive => Environment.UserInteractive;

    public static string UserName => Environment.UserName;

    public static int VerticalFocusThickness => 1;

    public static int VerticalResizeBorderThickness => NonClientArea.SizingFrameWidth;

    public static int VerticalScrollBarArrowHeight => NonClientArea.ScrollBarSize;

    public static int VerticalScrollBarThumbHeight => NonClientArea.ScrollBarSize;

    public static int VerticalScrollBarWidth => NonClientArea.ScrollBarSize;

    public static Rectangle VirtualScreen
    {
        get
        {
            var screens = Screen.AllScreens;
            if (screens.Length == 0) return Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1024, 768);

            var union = screens[0].Bounds;
            for (var i = 1; i < screens.Length; i++) union = Rectangle.Union(union, screens[i].Bounds);

            return union;
        }
    }

    public static Rectangle WorkingArea => Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1024, 768);

    public static Size GetBorderSizeForDpi(int dpi) => Scale(BorderSize, dpi);

    public static int GetHorizontalScrollBarArrowWidthForDpi(int dpi) => Scale(HorizontalScrollBarArrowWidth, dpi);

    public static int GetHorizontalScrollBarHeightForDpi(int dpi) => Scale(HorizontalScrollBarHeight, dpi);

    public static Font GetMenuFontForDpi(int dpi)
    {
        var font = MenuFont;
        return new Font(font.FontFamily, font.Size * dpi / 96f, font.Style, font.Unit);
    }

    public static int GetVerticalScrollBarWidthForDpi(int dpi) => Scale(VerticalScrollBarWidth, dpi);

    public static int VerticalScrollBarArrowHeightForDpi(int dpi) => Scale(VerticalScrollBarArrowHeight, dpi);

    private static int Scale(int value, int dpi) => (int)Math.Round(value * dpi / 96.0);

    private static Size Scale(Size value, int dpi) => new(Scale(value.Width, dpi), Scale(value.Height, dpi));
}

/// <summary>
/// A snapshot of the machine's battery and power line, as GetSystemPowerStatus
/// answers on Windows.
/// </summary>
/// <remarks>
/// No other reference sheet claims this type, and <see cref="SystemInformation.PowerStatus"/>
/// cannot compile without it, so it lives here instead of staying unfinished.
/// Microsoft gives it no public constructor either: an application only ever
/// reads one back from SystemInformation.PowerStatus.
/// </remarks>
public class PowerStatus
{
    internal PowerStatus(
        BatteryChargeStatus batteryChargeStatus,
        int batteryFullLifetime,
        float batteryLifePercent,
        int batteryLifeRemaining,
        PowerLineStatus powerLineStatus)
    {
        BatteryChargeStatus = batteryChargeStatus;
        BatteryFullLifetime = batteryFullLifetime;
        BatteryLifePercent = batteryLifePercent;
        BatteryLifeRemaining = batteryLifeRemaining;
        PowerLineStatus = powerLineStatus;
    }

    public BatteryChargeStatus BatteryChargeStatus { get; }

    /// <summary>Seconds a full battery lasts, or -1 when unknown (there is no battery here).</summary>
    public int BatteryFullLifetime { get; }

    public float BatteryLifePercent { get; }

    /// <summary>Seconds of battery left, or -1 when unknown (there is no battery here).</summary>
    public int BatteryLifeRemaining { get; }

    public PowerLineStatus PowerLineStatus { get; }
}
