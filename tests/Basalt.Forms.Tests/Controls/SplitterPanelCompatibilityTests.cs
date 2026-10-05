using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

public class SplitterPanelCompatibilityTests
{
    [Fact]
    public void Losing_capture_stops_a_splitter_drag()
    {
        var container = new TestSplitContainer { Size = new Size(200, 100), SplitterDistance = 50 };
        container.BeginDrag();
        container.Capture = false;
        container.ContinueDrag();

        Assert.Equal(50, container.SplitterDistance);
    }

    [AvaloniaFact]
    public void Losing_capture_stops_a_standalone_splitter_drag()
    {
        var parent = new Form { ClientSize = new Size(300, 100) };
        var target = new Panel { Dock = DockStyle.Left, Width = 60 };
        var splitter = new TestSplitter { Dock = DockStyle.Left };
        parent.Controls.Add(splitter);
        parent.Controls.Add(target);
        parent.Show();
        Dispatcher.UIThread.RunJobs();

        parent.Host!.MouseDown(new Avalonia.Point(61, 50), MouseButton.Left);
        splitter.Capture = false;
        splitter.ContinueDrag();

        Assert.Equal(60, target.Width);
        parent.Close();
    }

    private sealed class TestSplitContainer : SplitContainer
    {
        public void BeginDrag() => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 52, 50, 0));

        public void ContinueDrag() => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 82, 50, 0));
    }

    private sealed class TestSplitter : Splitter
    {
        public void ContinueDrag() => OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, 20, 1, 0));
    }

    [Fact]
    public void Splitter_declares_compatibility_events_that_follow_control_state()
    {
        var splitter = new Splitter();
        var declaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(Splitter).GetProperty(nameof(Splitter.Text), declaredOnly));
        Assert.NotNull(typeof(Splitter).GetEvent(nameof(Splitter.TextChanged), declaredOnly));

        var textChanges = 0;
        splitter.TextChanged += (_, _) => textChanges++;
        splitter.Text = "Divider";

        Assert.Equal("Divider", ((Control)splitter).Text);
        Assert.Equal(1, textChanges);
    }

    [Fact]
    public void SplitContainer_declares_forwarded_properties_and_events()
    {
        var container = new SplitContainer();
        var declaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(SplitContainer).GetEvent(nameof(SplitContainer.TextChanged), declaredOnly));

        var textChanges = 0;
        container.TextChanged += (_, _) => textChanges++;
        container.Text = "Layout";

        Assert.Equal("Layout", ((Control)container).Text);
        Assert.Equal(1, textChanges);
    }

    [Fact]
    public void SplitContainer_AutoScroll_always_reports_off_and_never_shows_scroll_bars()
    {
        var container = new SplitContainer { Size = new Size(100, 60) };

        container.AutoScroll = true;
        container.AutoScrollMinSize = new Size(1000, 1000);
        container.PerformLayout();

        Assert.False(container.AutoScroll);
        Assert.False(container.HorizontalScroll.Visible);
        Assert.False(container.VerticalScroll.Visible);
    }

    [Fact]
    public void Declared_panel_members_forward_to_the_base_control_state_and_events()
    {
        var panel = new SplitContainer().Panel1;
        var declaredOnly = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(SplitterPanel).GetProperty(nameof(SplitterPanel.Anchor), declaredOnly));
        Assert.NotNull(typeof(SplitterPanel).GetEvent(nameof(SplitterPanel.TabIndexChanged), declaredOnly));

        var indexChanges = 0;
        panel.TabIndexChanged += (_, _) => indexChanges++;
        panel.TabIndex = 7;
        panel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

        Assert.Equal(AnchorStyles.Bottom | AnchorStyles.Right, ((Control)panel).Anchor);
        Assert.Equal(7, ((Control)panel).TabIndex);
        Assert.Equal(1, indexChanges);
    }
}
