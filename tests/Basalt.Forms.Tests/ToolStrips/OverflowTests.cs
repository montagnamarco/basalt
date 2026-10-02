using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// A ToolStrip too narrow for every item: which ones are shown on
/// <see cref="ToolStrip.OverflowButton"/>'s drop-down (while staying in Items), <see cref="ToolStripItem.Placement"/>
/// and <see cref="ToolStripItem.Overflow"/> driving the choice, and the
/// button itself appearing, painting and opening that drop-down.
/// </summary>
public class OverflowTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static ToolStripButton Button(string text) => new(text) { AutoSize = false, Size = new Size(40, 23) };

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void Everything_that_fits_stays_on_the_strip_with_the_button_hidden()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var two = Button("2");
        strip.Items.AddRange([one, two]);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, one.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, two.Placement);
        Assert.False(one.IsOnOverflow);
        Assert.Equal(Rectangle.Empty, strip.OverflowButton.Bounds);
        Assert.Empty(strip.OverflowButton.DropDownItems);
    }

    [Fact]
    public void An_item_that_does_not_fit_moves_to_the_overflow_drop_down()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var two = Button("2");
        var three = Button("3");
        strip.Items.AddRange([one, two, three]);

        strip.PerformLayout();

        // 100px fits two 40px buttons (80px, within the 87px left once the 13px overflow button is reserved) but not a third (120px).
        Assert.Equal(ToolStripItemPlacement.Main, one.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, two.Placement);
        Assert.Equal(ToolStripItemPlacement.Overflow, three.Placement);
        Assert.True(three.IsOnOverflow);

        // As in WinForms the item only changes where it is shown: Items and Owner stay as they were.
        Assert.Equal([one, two, three], strip.Items.Cast<ToolStripItem>());
        Assert.Same(strip, three.Owner);
        Assert.Same(strip.OverflowButton.DropDown, three.GetCurrentParent());
        Assert.Equal([three], strip.OverflowButton.DropDownItems.Cast<ToolStripItem>());
        Assert.True(strip.OverflowButton.DropDownItems.IsReadOnly);

        Assert.NotEqual(Rectangle.Empty, strip.OverflowButton.Bounds);
        Assert.Equal(strip.ClientSize.Width, strip.OverflowButton.Bounds.Right);
    }

    [Fact]
    public void An_item_that_fits_again_after_growing_the_strip_comes_back_from_overflow()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(70, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var two = Button("2");
        strip.Items.AddRange([one, two]);
        strip.PerformLayout();
        Assert.Equal(ToolStripItemPlacement.Overflow, two.Placement);

        strip.Size = new Size(200, 25);
        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, two.Placement);
        Assert.Same(strip, two.GetCurrentParent());
        Assert.Equal([one, two], strip.Items.Cast<ToolStripItem>());
        Assert.Empty(strip.OverflowButton.DropDownItems);
    }

    [Fact]
    public void Overflow_Always_sends_an_item_to_the_drop_down_even_with_room_to_spare()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var always = Button("2");
        always.Overflow = ToolStripItemOverflow.Always;
        strip.Items.AddRange([one, always]);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, one.Placement);
        Assert.Equal(ToolStripItemPlacement.Overflow, always.Placement);
        Assert.Contains(always, strip.OverflowButton.DropDownItems.Cast<ToolStripItem>());
    }

    [Fact]
    public void Overflow_Never_keeps_an_item_on_the_strip_however_crowded_it_gets()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(50, 25), GripStyle = ToolStripGripStyle.Hidden };
        var never = Button("1");
        never.Overflow = ToolStripItemOverflow.Never;
        var other = Button("2");
        strip.Items.AddRange([never, other]);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, never.Placement);
        Assert.Equal(ToolStripItemPlacement.Overflow, other.Placement);
    }

    [Fact]
    public void An_unavailable_item_is_placed_nowhere_rather_than_on_the_overflow()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(200, 25), GripStyle = ToolStripGripStyle.Hidden };
        var hidden = Button("1");
        hidden.Available = false;
        strip.Items.Add(hidden);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.None, hidden.Placement);
        Assert.False(hidden.IsOnOverflow);
    }

    [Fact]
    public void CanOverflow_false_keeps_every_available_item_on_the_strip()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(50, 25), GripStyle = ToolStripGripStyle.Hidden, CanOverflow = false };
        var one = Button("1");
        var two = Button("2");
        strip.Items.AddRange([one, two]);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, one.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, two.Placement);
        Assert.Equal(Rectangle.Empty, strip.OverflowButton.Bounds);
    }

    [Fact]
    public void The_overflow_button_paints_a_glyph_distinct_from_a_blank_background()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(90, 25), GripStyle = ToolStripGripStyle.Hidden, BackColor = Color.White };
        strip.Items.AddRange([Button("1"), Button("2"), Button("3")]);
        strip.PerformLayout();

        var bitmap = Draw(strip);
        var bounds = strip.OverflowButton.Bounds;
        Assert.NotEqual(Rectangle.Empty, bounds);

        var sawNonBackground = false;
        for (var x = bounds.Left; x < bounds.Right; x++)
        {
            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) sawNonBackground = true;
            }
        }

        Assert.True(sawNonBackground);
    }

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 100) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    [AvaloniaFact]
    public void Clicking_the_overflow_button_opens_its_drop_down_with_the_overflowed_item()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, AutoSize = false, Bounds = new Rectangle(0, 0, 90, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var two = Button("2");
        var three = Button("3");
        strip.Items.AddRange([one, two, three]);
        strip.PerformLayout();
        var form = ShowWith(strip);

        var buttonBounds = strip.OverflowButton.Bounds;
        var point = new Avalonia.Point(buttonBounds.Left + buttonBounds.Width / 2, buttonBounds.Top + buttonBounds.Height / 2);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(strip.OverflowButton.DropDown.IsOpen);
        Assert.Contains(three, strip.OverflowButton.DropDownItems.Cast<ToolStripItem>());

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_an_overflowed_item_inside_the_open_drop_down_clicks_it_and_closes_the_drop_down()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, AutoSize = false, Bounds = new Rectangle(0, 0, 90, 25), GripStyle = ToolStripGripStyle.Hidden };
        var three = Button("3");
        strip.Items.AddRange([Button("1"), Button("2"), three]);
        var form = ShowWith(strip);
        var clicks = 0;
        three.Click += (_, _) => clicks++;

        var buttonBounds = strip.OverflowButton.Bounds;
        var buttonPoint = new Avalonia.Point(buttonBounds.Left + buttonBounds.Width / 2, buttonBounds.Top + buttonBounds.Height / 2);
        form.Host!.MouseDown(buttonPoint, MouseButton.Left);
        form.Host!.MouseUp(buttonPoint, MouseButton.Left);

        // The drop-down laid the borrowed item out in its own coordinates and hit-tests it there.
        var dropDownWindow = strip.OverflowButton.DropDown.PopupWindow!;
        var itemPoint = new Avalonia.Point(three.Bounds.Left + 5, three.Bounds.Top + 5);
        dropDownWindow.MouseDown(itemPoint, MouseButton.Left);
        dropDownWindow.MouseUp(itemPoint, MouseButton.Left);

        Assert.Equal(1, clicks);
        Assert.False(strip.OverflowButton.DropDown.IsOpen);
        Assert.Same(strip, three.Owner);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public void A_menu_item_on_a_crowded_tool_strip_stays_because_its_Overflow_defaults_to_Never()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(50, 25), GripStyle = ToolStripGripStyle.Hidden };
        var button = Button("1");
        var menuItem = new ToolStripMenuItem("Menu") { AutoSize = false, Size = new Size(40, 23) };
        strip.Items.AddRange([button, menuItem]);

        strip.PerformLayout();

        Assert.Equal(ToolStripItemOverflow.Never, menuItem.Overflow);
        Assert.Equal(ToolStripItemPlacement.Main, menuItem.Placement);
        Assert.Equal(ToolStripItemPlacement.Overflow, button.Placement);
    }

    [Fact]
    public void Room_for_a_right_aligned_item_is_kept_before_the_items_ahead_of_it_are_placed()
    {
        var strip = new ToolStrip { Font = Font, AutoSize = false, Size = new Size(100, 25), GripStyle = ToolStripGripStyle.Hidden };
        var one = Button("1");
        var two = Button("2");
        var right = Button("R");
        right.Alignment = ToolStripItemAlignment.Right;
        strip.Items.AddRange([one, two, right]);

        strip.PerformLayout();

        // 100px minus the 13px overflow button minus the right-aligned 40px leaves room for one more 40px button.
        Assert.Equal(ToolStripItemPlacement.Main, one.Placement);
        Assert.Equal(ToolStripItemPlacement.Overflow, two.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, right.Placement);
        Assert.True(one.Bounds.Right <= right.Bounds.Left);
    }

    [AvaloniaFact]
    public void Hovering_an_overflowed_item_inside_the_open_drop_down_makes_it_the_active_item()
    {
        var strip = new ToolStrip { Font = Font, Dock = DockStyle.None, AutoSize = false, Bounds = new Rectangle(0, 0, 90, 25), GripStyle = ToolStripGripStyle.Hidden };
        var three = Button("3");
        strip.Items.AddRange([Button("1"), Button("2"), three]);
        var form = ShowWith(strip);

        var buttonBounds = strip.OverflowButton.Bounds;
        var buttonPoint = new Avalonia.Point(buttonBounds.Left + buttonBounds.Width / 2, buttonBounds.Top + buttonBounds.Height / 2);
        form.Host!.MouseDown(buttonPoint, MouseButton.Left);
        form.Host!.MouseUp(buttonPoint, MouseButton.Left);

        // A submenu opens on hover only for the drop-down's active item, so the borrowed item must become it.
        var dropDown = strip.OverflowButton.DropDown;
        dropDown.PopupWindow!.MouseMove(new Avalonia.Point(three.Bounds.Left + 5, three.Bounds.Top + 5));

        Assert.True(three.Selected);
        Assert.Same(three, dropDown.ActiveItem);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }
}
