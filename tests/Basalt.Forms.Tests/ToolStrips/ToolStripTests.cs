using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStrip's items, their layout, mouse-driven hover/press/click states,
/// and painting through the system and the professional renderer.
/// </summary>
public class ToolStripTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static bool InvokeMnemonic(Control control, char character) =>
        (bool)typeof(Control).GetMethod("ProcessMnemonic", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, [character])!;

    // --- Items, the collection, layout ---------------------------------------

    [Fact]
    public void Adding_an_item_raises_ItemAdded_and_places_it_after_the_grip()
    {
        var strip = new ToolStrip { Font = Font, Size = new Size(200, 25) };
        ToolStripItem? added = null;
        strip.ItemAdded += (_, e) => added = e.Item;
        var button = new ToolStripButton("One");

        strip.Items.Add(button);

        Assert.Same(button, added);
        Assert.Same(strip, button.Owner);
        Assert.Single(strip.Items);

        // GripMargin(2) + the 3-pixel grip + GripMargin(2) sits before the first item.
        Assert.Equal(7, button.Bounds.X);
        Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
    }

    [Fact]
    public void Removing_an_item_raises_ItemRemoved_and_clears_its_owner()
    {
        var strip = new ToolStrip { Font = Font };
        var button = new ToolStripButton("One");
        strip.Items.Add(button);

        var removed = false;
        strip.ItemRemoved += (_, _) => removed = true;

        strip.Items.Remove(button);

        Assert.True(removed);
        Assert.Null(button.Owner);
        Assert.Empty(strip.Items);
    }

    [Fact]
    public void Right_aligned_items_pack_against_the_trailing_edge()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var left = new ToolStripButton("L") { AutoSize = false, Size = new Size(30, 23) };
        var right = new ToolStripButton("R") { AutoSize = false, Size = new Size(30, 23), Alignment = ToolStripItemAlignment.Right };

        strip.Items.Add(left);
        strip.Items.Add(right);

        Assert.Equal(0, left.Bounds.Left);
        Assert.Equal(strip.ClientSize.Width, right.Bounds.Right);
        Assert.True(right.Bounds.Left > left.Bounds.Right);
    }

    [Fact]
    public void An_autosized_strip_grows_to_its_tallest_items_height()
    {
        var strip = new ToolStrip { Font = Font };
        var tall = new ToolStripButton("Tall") { AutoSize = false, Size = new Size(50, 40) };

        strip.Items.Add(tall);

        Assert.Equal(40, strip.Height);
    }

    [Fact]
    public void A_mnemonic_clicks_the_matching_item()
    {
        var strip = new ToolStrip { Font = Font };
        var button = new ToolStripButton("&Save");
        strip.Items.Add(button);
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        ToolStripItem? clicked = null;
        strip.ItemClicked += (_, e) => clicked = e.ClickedItem;

        Assert.True(InvokeMnemonic(strip, 's'));
        Assert.False(InvokeMnemonic(strip, 'x'));

        Assert.Equal(1, clicks);
        Assert.Same(button, clicked);
    }

    // --- Painting through the renderer ---------------------------------------

    [Fact]
    public void The_classic_renderer_paints_the_strips_face_and_an_etched_separator()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25), BackColor = Color.FromArgb(255, 200, 200, 200) };
        var separator = new ToolStripSeparator();
        strip.Items.Add(separator);

        using var bitmap = Draw(strip);

        // The background is the strip's own back colour, painted by DrawToolStripBackground.
        Assert.Equal(strip.BackColor.ToArgb(), bitmap.GetPixel(90, 12).ToArgb());

        // The separator is an etched line: a dark pixel immediately followed by a light one, at its middle.
        var midX = separator.Bounds.Left + separator.Bounds.Width / 2;
        Assert.Equal(SystemColors.ControlDark.ToArgb(), bitmap.GetPixel(midX, 12).ToArgb());
        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), bitmap.GetPixel(midX + 1, 12).ToArgb());
    }

    [Fact]
    public void An_idle_button_has_no_bevel_but_a_hovered_one_is_raised_and_a_pressed_one_is_sunken()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25) };
        var button = new ToolStripButton("B") { AutoSize = false, Size = new Size(40, 23) };
        strip.Items.Add(button);

        using var idle = Draw(strip);
        Assert.NotEqual(SystemColors.ControlLightLight.ToArgb(), idle.GetPixel(button.Bounds.Left, button.Bounds.Top).ToArgb());

        button.SetSelectedInternal(true);
        using var hovered = Draw(strip);
        Assert.Equal(SystemColors.ControlLightLight.ToArgb(), hovered.GetPixel(button.Bounds.Left, button.Bounds.Top).ToArgb());

        button.SetPressedInternal(true);
        using var pressed = Draw(strip);
        Assert.Equal(SystemColors.ControlDark.ToArgb(), pressed.GetPixel(button.Bounds.Left, button.Bounds.Top).ToArgb());
    }

    [Fact]
    public void The_grip_draws_dots_when_visible_and_leaves_the_background_plain_when_hidden()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25), BackColor = Color.White, GripStyle = ToolStripGripStyle.Visible };

        using var visible = Draw(strip);
        var dotX = strip.GripRectangle.Left + Math.Max(strip.GripRectangle.Width / 2 - 1, 0);
        var dotY = strip.GripRectangle.Top + 2;
        Assert.Equal(SystemColors.ControlDark.ToArgb(), visible.GetPixel(dotX, dotY).ToArgb());

        strip.GripStyle = ToolStripGripStyle.Hidden;
        using var hidden = Draw(strip);
        Assert.Equal(Color.White.ToArgb(), hidden.GetPixel(dotX, dotY).ToArgb());
    }

    [Fact]
    public void The_professional_renderer_paints_a_different_background_than_the_system_one()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25) };
        strip.RenderMode = ToolStripRenderMode.System;
        using var system = Draw(strip);

        strip.RenderMode = ToolStripRenderMode.Professional;
        using var professional = Draw(strip);

        // The classic renderer fills flat with BackColor; the professional one fills a top-to-bottom
        // gradient, so its top and bottom rows differ from each other even though the system render does
        // not (sampled away from row 0 and the bottom border line the system renderer also draws).
        Assert.Equal(system.GetPixel(50, 2).ToArgb(), system.GetPixel(50, 20).ToArgb());
        Assert.NotEqual(professional.GetPixel(50, 2).ToArgb(), professional.GetPixel(50, 20).ToArgb());
    }

    // --- Mouse-driven states and click events, with a real host -------------

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Hovering_an_item_selects_it_and_moving_off_deselects_it()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, Bounds = new Rectangle(0, 0, 200, 25) };
        var button = new ToolStripButton("B") { AutoSize = false, Size = new Size(40, 23) };
        strip.Items.Add(button);
        var form = ShowWith(strip);

        form.Host!.MouseMove(new Avalonia.Point(button.Bounds.Left + 5, button.Bounds.Top + 5));
        Assert.True(button.Selected);

        form.Host!.MouseMove(new Avalonia.Point(150, 20));
        Assert.False(button.Selected);

        form.Close();
    }

    [AvaloniaFact]
    public void A_click_released_over_the_same_item_raises_Click_and_ItemClicked()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, Bounds = new Rectangle(0, 0, 200, 25) };
        var button = new ToolStripButton("B") { AutoSize = false, Size = new Size(40, 23) };
        strip.Items.Add(button);
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        ToolStripItem? clicked = null;
        strip.ItemClicked += (_, e) => clicked = e.ClickedItem;
        var form = ShowWith(strip);

        var point = new Avalonia.Point(button.Bounds.Left + 5, button.Bounds.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        Assert.True(button.Pressed);

        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.False(button.Pressed);
        Assert.Equal(1, clicks);
        Assert.Same(button, clicked);

        form.Close();
    }

    [AvaloniaFact]
    public void Releasing_outside_the_pressed_item_does_not_click_it()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, Bounds = new Rectangle(0, 0, 200, 25) };
        var button = new ToolStripButton("B") { AutoSize = false, Size = new Size(40, 23) };
        strip.Items.Add(button);
        var clicks = 0;
        button.Click += (_, _) => clicks++;
        var form = ShowWith(strip);

        form.Host!.MouseDown(new Avalonia.Point(button.Bounds.Left + 5, button.Bounds.Top + 5), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(190, 20));
        form.Host!.MouseUp(new Avalonia.Point(190, 20), MouseButton.Left);

        Assert.Equal(0, clicks);
        Assert.False(button.Pressed);

        form.Close();
    }

    [AvaloniaFact]
    public void CheckOnClick_toggles_Checked_when_the_button_is_clicked()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, Bounds = new Rectangle(0, 0, 200, 25) };
        var button = new ToolStripButton("B") { AutoSize = false, Size = new Size(40, 23), CheckOnClick = true };
        strip.Items.Add(button);
        var form = ShowWith(strip);

        var point = new Avalonia.Point(button.Bounds.Left + 5, button.Bounds.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(button.Checked);

        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.False(button.Checked);

        form.Close();
    }

    [Fact]
    public void Hiding_an_item_makes_it_unavailable_so_it_leaves_the_layout()
    {
        var strip = new ToolStrip { Font = Font };
        var first = new ToolStripButton("First");
        var second = new ToolStripButton("Second");
        strip.Items.AddRange([first, second]);
        strip.PerformLayout();
        var secondLeftWithFirst = second.Bounds.Left;

        first.Visible = false;
        strip.PerformLayout();

        Assert.False(first.Available);
        Assert.True(second.Bounds.Left < secondLeftWithFirst);
    }

    // On the UI thread, as an application changes the manager's renderer: it repaints every live strip.
    [AvaloniaFact]
    public void A_strip_following_the_manager_repaints_when_the_managers_renderer_changes()
    {
        var original = ToolStripManager.RenderMode;
        try
        {
            ToolStripManager.RenderMode = ToolStripManagerRenderMode.System;
            var strip = new ToolStrip();
            var ownRenderer = new ToolStrip { RenderMode = ToolStripRenderMode.System };
            var raised = 0;
            var raisedOnOwn = 0;
            strip.RendererChanged += (_, _) => raised++;
            ownRenderer.RendererChanged += (_, _) => raisedOnOwn++;

            ToolStripManager.RenderMode = ToolStripManagerRenderMode.Professional;

            Assert.Equal(1, raised);
            Assert.Equal(0, raisedOnOwn);
            Assert.IsType<ToolStripProfessionalRenderer>(strip.Renderer);
        }
        finally
        {
            ToolStripManager.RenderMode = original;
        }
    }
}
