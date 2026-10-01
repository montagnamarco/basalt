using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.Internal;
using Avalonia.Headless.XUnit;

namespace Basalt.Forms.Tests.Components;

/// <summary>SystemInformation: the classic Windows 2000 theme at 96 dpi.</summary>
public class SystemInformationTests
{
    [Fact]
    public void Frame_sizes_match_the_non_client_area_the_forms_draw()
    {
        Assert.Equal(new Size(NonClientArea.BorderWidth, NonClientArea.BorderWidth), SystemInformation.BorderSize);
        Assert.Equal(new Size(NonClientArea.EdgeWidth, NonClientArea.EdgeWidth), SystemInformation.Border3DSize);
        Assert.Equal(new Size(NonClientArea.FixedFrameWidth, NonClientArea.FixedFrameWidth), SystemInformation.FixedFrameBorderSize);
        Assert.Equal(new Size(NonClientArea.SizingFrameWidth, NonClientArea.SizingFrameWidth), SystemInformation.FrameBorderSize);
        Assert.Equal(NonClientArea.CaptionHeight, SystemInformation.CaptionHeight);
        Assert.Equal(NonClientArea.ToolCaptionHeight, SystemInformation.ToolWindowCaptionHeight);
        Assert.Equal(NonClientArea.ScrollBarSize, SystemInformation.VerticalScrollBarWidth);
        Assert.Equal(NonClientArea.ScrollBarSize, SystemInformation.HorizontalScrollBarHeight);
        Assert.Equal(NonClientArea.ScrollBarSize, SystemInformation.VerticalScrollBarArrowHeight);
    }

    [Fact]
    public void Classic_theme_measurements_are_those_of_Windows_2000_at_96_dpi()
    {
        Assert.Equal(new Size(32, 32), SystemInformation.IconSize);
        Assert.Equal(new Size(16, 16), SystemInformation.SmallIconSize);
        Assert.Equal(new Size(32, 32), SystemInformation.CursorSize);
        Assert.Equal(new Size(4, 4), SystemInformation.DoubleClickSize);
        Assert.Equal(500, SystemInformation.DoubleClickTime);
        Assert.Equal(400, SystemInformation.MouseHoverTime);
        Assert.Equal(new Size(4, 4), SystemInformation.MouseHoverSize);
        Assert.Equal(1, SystemInformation.KeyboardDelay);
        Assert.Equal(530, SystemInformation.CaretBlinkTime);
        Assert.Equal(3, SystemInformation.MouseWheelScrollLines);
        Assert.Equal(120, SystemInformation.MouseWheelScrollDelta);
        Assert.Equal(new Size(4, 4), SystemInformation.DragSize);
        Assert.Equal(19, SystemInformation.MenuHeight);
    }

    [Fact]
    public void Platform_values_come_straight_from_the_environment()
    {
        Assert.Equal(Environment.MachineName, SystemInformation.ComputerName);
        Assert.Equal(Environment.UserName, SystemInformation.UserName);
        Assert.Equal(Environment.UserDomainName, SystemInformation.UserDomainName);
        Assert.Equal(Environment.UserInteractive, SystemInformation.UserInteractive);
    }

    [Fact]
    public void A_classic_desktop_reports_a_three_button_mouse_with_a_wheel()
    {
        Assert.Equal(3, SystemInformation.MouseButtons);
        Assert.True(SystemInformation.MousePresent);
        Assert.True(SystemInformation.MouseWheelPresent);
        Assert.False(SystemInformation.MouseButtonsSwapped);
    }

    [Fact]
    public void A_desktop_without_a_remote_session_or_special_accessibility_mode_reports_the_classic_defaults()
    {
        Assert.False(SystemInformation.TerminalServerSession);
        Assert.False(SystemInformation.HighContrast);
        Assert.False(SystemInformation.IsDropShadowEnabled);
        Assert.False(SystemInformation.IsMenuAnimationEnabled);
        Assert.False(SystemInformation.UIEffectsEnabled);
    }

    [Fact]
    public void Without_a_native_battery_the_power_status_reports_line_power_and_no_battery()
    {
        var status = SystemInformation.PowerStatus;

        Assert.Equal(BatteryChargeStatus.NoSystemBattery, status.BatteryChargeStatus);
        Assert.Equal(PowerLineStatus.Online, status.PowerLineStatus);
    }

    [Fact]
    public void Members_that_scale_with_dpi_double_at_twice_the_reference_dpi()
    {
        Assert.Equal(new Size(2, 2), SystemInformation.GetBorderSizeForDpi(192));
        Assert.Equal(SystemInformation.VerticalScrollBarWidth * 2, SystemInformation.GetVerticalScrollBarWidthForDpi(192));
        Assert.Equal(SystemInformation.HorizontalScrollBarHeight * 2, SystemInformation.GetHorizontalScrollBarHeightForDpi(192));
        Assert.Equal(SystemInformation.VerticalScrollBarArrowHeight * 2, SystemInformation.VerticalScrollBarArrowHeightForDpi(192));
    }

    [Fact]
    public void The_menu_font_for_a_dpi_scales_with_that_dpi()
    {
        var atReferenceDpi = SystemInformation.GetMenuFontForDpi(96);
        var atDoubleDpi = SystemInformation.GetMenuFontForDpi(192);

        Assert.Equal(SystemInformation.MenuFont.Size, atReferenceDpi.Size, 2);
        Assert.Equal(SystemInformation.MenuFont.Size * 2, atDoubleDpi.Size, 2);
    }

    [AvaloniaFact]
    public void Screen_derived_values_agree_with_the_Screen_class()
    {
        Assert.Equal(Screen.AllScreens.Length, SystemInformation.MonitorCount);
        Assert.Equal(Screen.PrimaryScreen!.Bounds.Size, SystemInformation.PrimaryMonitorSize);
        Assert.Equal(Screen.PrimaryScreen.WorkingArea, SystemInformation.WorkingArea);

        var virtualScreen = SystemInformation.VirtualScreen;
        foreach (var screen in Screen.AllScreens)
        {
            Assert.True(virtualScreen.Contains(screen.Bounds), $"{virtualScreen} does not contain {screen.Bounds}.");
        }
    }
}
