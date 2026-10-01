using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Core;

/// <summary>Scrolling a container whose children reach beyond its client area.</summary>
public class AutoScrollTests
{
    private static Panel ScrollingPanel(out Control far)
    {
        var panel = new Panel { Size = new Size(100, 100), AutoScroll = true };
        far = new Control { Bounds = new Rectangle(10, 250, 50, 20), Parent = panel };

        return panel;
    }

    [Fact]
    public void Children_beyond_the_client_area_bring_a_scroll_bar_that_takes_room()
    {
        var panel = ScrollingPanel(out _);

        Assert.True(panel.VerticalScroll.Visible);
        Assert.False(panel.HorizontalScroll.Visible);
        Assert.Equal(new Size(100 - SystemInformation.VerticalScrollBarWidth, 100), panel.ClientSize);
    }

    [Fact]
    public void Without_auto_scroll_nothing_scrolls()
    {
        var panel = new Panel { Size = new Size(100, 100) };
        _ = new Control { Bounds = new Rectangle(10, 250, 50, 20), Parent = panel };

        Assert.False(panel.VerticalScroll.Visible);
        Assert.Equal(new Size(100, 100), panel.ClientSize);
    }

    [Fact]
    public void Scrolling_moves_the_children_and_reports_a_negative_position()
    {
        var panel = ScrollingPanel(out var far);

        panel.AutoScrollPosition = new Point(0, 100);

        Assert.Equal(new Point(0, -100), panel.AutoScrollPosition);
        Assert.Equal(150, far.Top);
        Assert.Equal(100, panel.VerticalScroll.Value);
    }

    [Fact]
    public void Scrolling_stops_at_the_end_of_the_content()
    {
        var panel = ScrollingPanel(out var far);

        panel.AutoScrollPosition = new Point(0, 10_000);

        // The far child's bottom (270) plus its margin (3) ends at the bottom of the client area.
        Assert.Equal(100 - 3, far.Bottom);
    }

    [Fact]
    public void Scrolling_a_control_into_view_moves_just_enough()
    {
        var panel = ScrollingPanel(out var far);

        panel.ScrollControlIntoView(far);

        Assert.Equal(100, far.Bottom);
    }

    [Fact]
    public void The_frames_scroll_bar_is_drawn_with_its_arrows()
    {
        var panel = ScrollingPanel(out _);
        panel.BackColor = Color.White;
        using var bitmap = new Bitmap(100, 100);
        panel.DrawToBitmap(bitmap, new Rectangle(0, 0, 100, 100));

        // The bar's top arrow button is the control colour, not the panel's white.
        Assert.Equal(SystemColors.Control.ToArgb(), bitmap.GetPixel(92, 3).ToArgb());
        Assert.Equal(Color.White.ToArgb(), bitmap.GetPixel(40, 50).ToArgb());
    }

    [AvaloniaFact]
    public void Clicking_the_down_arrow_and_the_track_scrolls_by_a_line_and_a_page()
    {
        var panel = ScrollingPanel(out _);
        panel.Location = new Point(0, 0);
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(panel);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        var events = new List<ScrollEventType>();
        panel.Scroll += (_, e) => events.Add(e.Type);

        form.Host!.MouseDown(new Avalonia.Point(92, 95), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(92, 95), MouseButton.Left);
        var afterLine = panel.VerticalScroll.Value;

        form.Host!.MouseDown(new Avalonia.Point(92, 70), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(92, 70), MouseButton.Left);

        Assert.True(afterLine > 0);
        Assert.True(panel.VerticalScroll.Value > afterLine);
        Assert.Equal([ScrollEventType.SmallIncrement, ScrollEventType.EndScroll, ScrollEventType.LargeIncrement, ScrollEventType.EndScroll], events);

        form.Close();
    }

    [AvaloniaFact]
    public void The_mouse_wheel_scrolls_the_container()
    {
        var panel = ScrollingPanel(out _);
        var form = new Form { ClientSize = new Size(200, 150) };
        form.Controls.Add(panel);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        form.Host!.MouseWheel(new Avalonia.Point(40, 40), new Avalonia.Vector(0, -1));

        Assert.True(panel.VerticalScroll.Value > 0);

        form.Close();
    }
}
