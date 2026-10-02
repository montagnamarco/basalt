using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Mdi;

/// <summary>
/// The multiple-document interface: child forms inside a container's MdiClient,
/// with their own frame and title bar, activation, moving, the caption buttons,
/// LayoutMdi, the MDI keys and closing order.
/// </summary>
public class MdiTests
{
    private static (Form Container, Form First, Form Second) ShowContainerWithTwoChildren()
    {
        var container = new Form { IsMdiContainer = true, ClientSize = new Size(600, 400) };
        container.Show();
        Dispatcher.UIThread.RunJobs();

        var first = new Form { MdiParent = container, Text = "First", Bounds = new Rectangle(10, 10, 220, 160) };
        var second = new Form { MdiParent = container, Text = "Second", Bounds = new Rectangle(260, 40, 220, 160) };
        first.Show();
        second.Show();
        Dispatcher.UIThread.RunJobs();

        return (container, first, second);
    }

    /// <summary>A point given in a child's outer coordinates, in the container's client coordinates (what form.Host takes).</summary>
    private static Avalonia.Point InContainer(Form container, Form child, Point outer)
    {
        var screen = child.Parent!.PointToScreen(new Point(child.Left + outer.X, child.Top + outer.Y));
        var client = container.PointToClient(screen);

        return new Avalonia.Point(client.X, client.Y);
    }

    private static void Click(Form container, Avalonia.Point point)
    {
        container.Host!.MouseDown(point, MouseButton.Left);
        container.Host!.MouseUp(point, MouseButton.Left);
    }

    [AvaloniaFact]
    public void An_MDI_child_is_a_child_of_the_MdiClient_without_a_window_of_its_own()
    {
        var (container, first, _) = ShowContainerWithTwoChildren();

        Assert.True(first.IsMdiChild);
        Assert.False(first.TopLevel);
        Assert.Null(first.Host);
        Assert.IsType<MdiClient>(first.Parent);
        Assert.Equal(2, container.MdiChildren.Length);

        container.Close();
    }

    [AvaloniaFact]
    public void The_last_child_shown_is_active_and_a_click_in_another_activates_it()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var activations = 0;
        container.MdiChildActivate += (_, _) => activations++;

        Assert.Same(second, container.ActiveMdiChild);

        var inFirstsClient = InContainer(container, first, new Point(first.Width / 2, first.Height / 2));
        Click(container, inFirstsClient);

        Assert.Same(first, container.ActiveMdiChild);
        Assert.Equal(1, activations);

        container.Close();
    }

    [AvaloniaFact]
    public void The_active_childs_title_bar_is_drawn_in_the_active_caption_colour()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();

        using var bitmap = new Bitmap(container.ClientSize.Width, container.ClientSize.Height, PixelFormat.Format32bppArgb);
        container.DrawToBitmap(bitmap, new Rectangle(Point.Empty, container.ClientSize));

        // Just inside the sizing frame, at the left end of each title bar, where the gradient starts.
        Color CaptionPixel(Form child)
        {
            var point = InContainer(container, child, new Point(6, 6));
            return bitmap.GetPixel((int)point.X, (int)point.Y);
        }

        // The bar is a gradient, so a pixel near its start is close to the caption colour, not equal to it.
        static int Distance(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        bool LooksActive(Color pixel) => Distance(pixel, SystemColors.ActiveCaption) < Distance(pixel, SystemColors.InactiveCaption);

        Assert.True(LooksActive(CaptionPixel(second)), $"active title bar was {CaptionPixel(second)}");
        Assert.False(LooksActive(CaptionPixel(first)), $"inactive title bar was {CaptionPixel(first)}");

        container.Close();
    }

    [AvaloniaFact]
    public void Dragging_the_title_bar_moves_the_child()
    {
        var (container, first, _) = ShowContainerWithTwoChildren();
        var start = first.Location;
        var onCaption = InContainer(container, first, new Point(60, 12));

        container.Host!.MouseDown(onCaption, MouseButton.Left);
        container.Host!.MouseMove(new Avalonia.Point(onCaption.X + 30, onCaption.Y + 20), RawInputModifiers.LeftMouseButton);
        container.Host!.MouseUp(new Avalonia.Point(onCaption.X + 30, onCaption.Y + 20), MouseButton.Left);

        Assert.Equal(new Point(start.X + 30, start.Y + 20), first.Location);

        container.Close();
    }

    [AvaloniaFact]
    public void The_close_button_closes_the_child_and_the_other_becomes_active()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var closeButton = InContainer(container, second, new Point(second.Width - 4 - 8, 4 + 8));

        Click(container, closeButton);

        Assert.True(second.IsDisposed);
        Assert.Equal([first], container.MdiChildren);
        Assert.Same(first, container.ActiveMdiChild);

        container.Close();
    }

    [AvaloniaFact]
    public void The_maximize_button_fills_the_client_area_and_restore_puts_it_back()
    {
        var (container, first, _) = ShowContainerWithTwoChildren();
        first.Activate();
        var normal = first.Bounds;
        var maximizeButton = InContainer(container, first, new Point(first.Width - 4 - 16 - 2 - 8, 4 + 8));

        Click(container, maximizeButton);

        Assert.Equal(FormWindowState.Maximized, first.WindowState);
        Assert.Equal(first.Parent!.ClientSize.Width, first.ClientSize.Width);

        first.WindowState = FormWindowState.Normal;
        Assert.Equal(normal, first.Bounds);

        container.Close();
    }

    [AvaloniaFact]
    public void TileVertical_puts_the_children_side_by_side_across_the_client_area()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var area = first.Parent!.ClientRectangle;

        container.LayoutMdi(MdiLayout.TileVertical);

        Assert.Equal(new Rectangle(area.Left, area.Top, area.Width / 2, area.Height), first.Bounds);
        Assert.Equal(area.Right, second.Right);
        Assert.Equal(first.Right, second.Left);

        container.Close();
    }

    [AvaloniaFact]
    public void Control_F4_closes_the_active_child_and_Control_Tab_moves_to_the_next()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var third = new Form { MdiParent = container, Text = "Third" };
        third.Show();
        Dispatcher.UIThread.RunJobs();

        container.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        container.Host!.KeyReleaseQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Same(first, container.ActiveMdiChild);

        container.Host!.KeyPressQwerty(PhysicalKey.F4, RawInputModifiers.Control);
        container.Host!.KeyReleaseQwerty(PhysicalKey.F4, RawInputModifiers.Control);
        Assert.True(first.IsDisposed);
        Assert.False(second.IsDisposed);

        container.Close();
    }

    [AvaloniaFact]
    public void Closing_the_container_asks_each_child_first_and_one_refusal_keeps_everything_open()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var order = new List<string>();
        var refuse = true;
        first.FormClosing += (_, e) => order.Add($"first:{e.CloseReason}");
        second.FormClosing += (_, e) =>
        {
            order.Add($"second:{e.CloseReason}");
            e.Cancel = refuse;
        };
        container.FormClosing += (_, e) => order.Add($"container:{e.Cancel}");

        container.Close();

        Assert.Equal(["first:MdiFormClosing", "second:MdiFormClosing", "container:True"], order);
        Assert.False(container.IsDisposed);
        Assert.False(first.IsDisposed);

        refuse = false;
        container.Close();
        Assert.True(first.IsDisposed && second.IsDisposed && container.IsDisposed);
    }

    [AvaloniaFact]
    public void A_form_with_TopLevel_false_inside_a_panel_shows_there_without_a_window()
    {
        var host = new Form { ClientSize = new Size(400, 300) };
        var panel = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(panel);
        host.Show();
        Dispatcher.UIThread.RunJobs();

        var inner = new Form { TopLevel = false, Text = "Inner", Bounds = new Rectangle(20, 20, 200, 150) };
        panel.Controls.Add(inner);
        inner.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.True(inner.Visible);
        Assert.Null(inner.Host);
        Assert.Same(panel, inner.Parent);
        Assert.True(inner.ClientSize.Height < inner.Height - 19, "the title bar and frame take room above the client area");

        host.Close();
    }

    [AvaloniaFact]
    public void A_shown_child_released_from_its_container_becomes_a_window_again()
    {
        var (container, first, _) = ShowContainerWithTwoChildren();

        first.MdiParent = null;
        Dispatcher.UIThread.RunJobs();

        Assert.True(first.TopLevel);
        Assert.True(first.Visible);
        Assert.NotNull(first.Host);
        Assert.Single(container.MdiChildren);

        first.Close();
        container.Close();
    }

    [AvaloniaFact]
    public void A_shown_window_made_an_MDI_child_closes_its_own_window()
    {
        var container = new Form { IsMdiContainer = true, ClientSize = new Size(600, 400) };
        container.Show();
        var loose = new Form { Text = "Loose" };
        loose.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(loose.Host);

        loose.MdiParent = container;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(loose.Host);
        Assert.True(loose.Visible);
        Assert.Same(loose, container.ActiveMdiChild);

        container.Close();
    }

    [AvaloniaFact]
    public void Activating_a_child_raises_Deactivate_on_the_old_one_and_Activated_on_the_new_one()
    {
        var (container, first, second) = ShowContainerWithTwoChildren();
        var order = new List<string>();
        first.Activated += (_, _) => order.Add("first activated");
        second.Deactivate += (_, _) => order.Add("second deactivated");
        container.MdiChildActivate += (_, _) => order.Add("container");

        first.Activate();

        Assert.Equal(["second deactivated", "first activated", "container"], order);
        container.Close();
    }

    [AvaloniaFact]
    public void A_title_bar_too_narrow_for_its_buttons_drops_them_instead_of_overlapping()
    {
        var (container, first, _) = ShowContainerWithTwoChildren();
        first.Width = 40;

        var bar = System.Windows.Forms.Internal.Skins.CaptionLayout.CaptionBounds(first.Size, 0x00C00000 | 0x00040000 | 0x00800000, 0);
        var minimize = System.Windows.Forms.Internal.Skins.CaptionLayout.ButtonBounds(bar, System.Windows.Forms.Internal.Skins.CaptionHitArea.MinimizeButton, toolWindow: false);

        Assert.True(minimize.IsEmpty || minimize.Left >= bar.Left, $"minimize button at {minimize} outside the bar {bar}");
        container.Close();
    }
}
