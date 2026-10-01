using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.ToolStrips;

/// <summary>
/// ToolStripDropDown, ToolStripMenuItem, MenuStrip and ContextMenuStrip: the
/// popup that carries a menu's items, mouse-driven opening and closing,
/// keyboard navigation inside an open menu, checked items, and the classic
/// menu painting (highlight bar, check margin, submenu arrow).
/// </summary>
public class MenuTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    // --- ToolStripDropDown: opening, closing, items -------------------------

    [AvaloniaFact]
    public void Showing_a_drop_down_opens_it_and_Close_closes_it()
    {
        var dropDown = new ToolStripDropDown { Font = Font };
        var item = new ToolStripMenuItem("One");
        dropDown.Items.Add(item);
        var owner = new Button { Bounds = new Rectangle(10, 10, 80, 23) };
        var form = ShowWith(owner);

        var opened = 0;
        var closed = 0;
        dropDown.Opened += (_, _) => opened++;
        dropDown.Closed += (_, _) => closed++;

        dropDown.Show(owner, new Point(0, owner.Height));
        Assert.True(dropDown.IsOpen);
        Assert.Equal(1, opened);

        dropDown.Close();
        Assert.False(dropDown.IsOpen);
        Assert.Equal(1, closed);

        // Not a child of the form (a drop-down is its own top-level popup, not added to any
        // Controls collection), so it needs disposing on its own, or it keeps the Avalonia visual
        // its popup window created alive for ToolStripManager's weak list to find from another test.
        dropDown.Dispose();
        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Opening_can_be_cancelled()
    {
        var dropDown = new ToolStripDropDown { Font = Font };
        dropDown.Items.Add(new ToolStripMenuItem("One"));
        dropDown.Opening += (_, e) => e.Cancel = true;

        dropDown.Show(new Point(50, 50));

        Assert.False(dropDown.IsOpen);

        dropDown.Dispose();
    }

    // --- MenuStrip and ToolStripMenuItem: mouse opens and closes -------------

    [AvaloniaFact]
    public void Clicking_a_top_level_item_opens_its_drop_down_and_clicking_a_leaf_item_closes_it()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var exit = new ToolStripMenuItem("E&xit");
        file.DropDownItems.Add(exit);
        menu.Items.Add(file);
        var form = ShowWith(menu);

        var clicked = false;
        exit.Click += (_, _) => clicked = true;

        var point = new Avalonia.Point(file.Bounds.Left + 5, file.Bounds.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(file.DropDown.IsOpen);

        // The drop-down is its own native window: click into its own popup window, at its own (not the form's) coordinates.
        var dropDownWindow = file.DropDown.PopupWindow!;
        var itemPoint = new Avalonia.Point(exit.Bounds.Left + 5, exit.Bounds.Top + 5);
        dropDownWindow.MouseDown(itemPoint, MouseButton.Left);
        dropDownWindow.MouseUp(itemPoint, MouseButton.Left);

        Assert.True(clicked);
        Assert.False(file.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MenuActivate_and_MenuDeactivate_bracket_an_open_drop_down()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("Item"));
        menu.Items.Add(file);
        var form = ShowWith(menu);

        var activated = 0;
        var deactivated = 0;
        menu.MenuActivate += (_, _) => activated++;
        menu.MenuDeactivate += (_, _) => deactivated++;

        file.ShowDropDown();
        Assert.Equal(1, activated);
        Assert.Equal(0, deactivated);

        file.HideDropDown();
        Assert.Equal(1, deactivated);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // --- Keyboard navigation inside an open menu -----------------------------

    [AvaloniaFact]
    public void Down_moves_the_highlight_and_Enter_clicks_the_highlighted_item()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var one = new ToolStripMenuItem("One");
        var two = new ToolStripMenuItem("Two");
        file.DropDownItems.AddRange([one, two]);
        menu.Items.Add(file);
        var form = ShowWith(menu);

        var twoClicked = false;
        two.Click += (_, _) => twoClicked = true;

        file.ShowDropDown();
        file.DropDown.SelectFirstAvailableItem();
        Assert.True(one.Selected);

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowDown, RawInputModifiers.None);
        Assert.True(two.Selected);
        Assert.False(one.Selected);

        form.Host!.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Assert.True(twoClicked);
        Assert.False(file.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Escape_closes_one_level_and_leaves_the_parent_open()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var recent = new ToolStripMenuItem("Recent");
        var child = new ToolStripMenuItem("doc.txt");
        recent.DropDownItems.Add(child);
        file.DropDownItems.Add(recent);
        menu.Items.Add(file);
        var form = ShowWith(menu);

        file.ShowDropDown();
        recent.ShowDropDown();
        Assert.True(recent.DropDown.IsOpen);
        Assert.True(file.DropDown.IsOpen);

        form.Host!.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);

        Assert.False(recent.DropDown.IsOpen);
        Assert.True(file.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Right_arrow_opens_a_highlighted_items_submenu()
    {
        var menu = new MenuStrip { Font = Font, Bounds = new Rectangle(0, 0, 300, 24) };
        var file = new ToolStripMenuItem("&File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add(new ToolStripMenuItem("doc.txt"));
        file.DropDownItems.Add(recent);
        menu.Items.Add(file);
        var form = ShowWith(menu);

        file.ShowDropDown();
        file.DropDown.SetActiveItem(recent);

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.True(recent.HasDropDown);
        Assert.True(recent.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // --- Checked items and painting ------------------------------------------

    [Fact]
    public void CheckOnClick_toggles_Checked_when_the_item_is_clicked()
    {
        var item = new ToolStripMenuItem("Word wrap") { CheckOnClick = true };

        item.PerformClick();
        Assert.True(item.Checked);

        item.PerformClick();
        Assert.False(item.Checked);
    }

    [Fact]
    public void A_checked_item_paints_a_check_glyph_in_its_image_margin()
    {
        var dropDown = new ToolStripDropDownMenu { Font = Font, AutoSize = false, Size = new Size(200, 60) };
        var item = new ToolStripMenuItem("Word wrap") { CheckOnClick = true, AutoSize = false, Size = new Size(150, 22) };
        dropDown.Items.Add(item);
        item.Checked = true;
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);

        // The check glyph sits roughly in the middle of the image margin, drawn over a sunken, lighter square.
        var probeX = item.Bounds.Left + 11;
        var probeY = item.Bounds.Top + item.Bounds.Height / 2;
        Assert.NotEqual(dropDown.BackColor.ToArgb(), bitmap.GetPixel(probeX, probeY).ToArgb());
    }

    [Fact]
    public void A_selected_menu_item_paints_the_highlight_background()
    {
        var dropDown = new ToolStripDropDownMenu { Font = Font, AutoSize = false, Size = new Size(200, 40), BackColor = Color.White };
        var item = new ToolStripMenuItem("Open") { AutoSize = false, Size = new Size(150, 22) };
        dropDown.Items.Add(item);
        dropDown.PerformLayout();

        var probeX = item.Bounds.Left + 60;
        var probeY = item.Bounds.Top + item.Bounds.Height / 2;

        using var idle = Draw(dropDown);
        var idleColor = idle.GetPixel(probeX, probeY).ToArgb();

        item.SetSelectedInternal(true);
        using var selected = Draw(dropDown);
        var selectedColor = selected.GetPixel(probeX, probeY).ToArgb();

        Assert.NotEqual(idleColor, selectedColor);
    }

    [Fact]
    public void An_item_with_a_submenu_paints_an_arrow_glyph_at_its_trailing_edge()
    {
        // The arrow glyph is drawn through Renderer.DrawArrow, which (as on a real toolbar's drop-down
        // arrow) only the professional renderer paints by default; the classic one leaves it to a
        // RenderArrow subscriber, same as Microsoft's own ToolStripSystemRenderer.
        var dropDown = new ToolStripDropDownMenu { Font = Font, AutoSize = false, Size = new Size(200, 40), BackColor = Color.White, RenderMode = ToolStripRenderMode.Professional };
        var item = new ToolStripMenuItem("More") { AutoSize = false, Size = new Size(150, 22) };
        item.DropDownItems.Add(new ToolStripMenuItem("Sub"));
        dropDown.Items.Add(item);
        dropDown.PerformLayout();

        using var bitmap = Draw(dropDown);

        var probe = bitmap.GetPixel(item.Bounds.Right - 8, item.Bounds.Top + item.Bounds.Height / 2);
        Assert.NotEqual(Color.White.ToArgb(), probe.ToArgb());
    }

    // --- ToolStripDropDownButton and ToolStripSplitButton --------------------

    [AvaloniaFact]
    public void A_drop_down_button_opens_its_menu_on_click()
    {
        var strip = new ToolStrip { Font = Font, Bounds = new Rectangle(0, 0, 200, 25) };
        var button = new ToolStripDropDownButton("Tools") { AutoSize = false, Size = new Size(60, 23) };
        button.DropDownItems.Add(new ToolStripMenuItem("Options"));
        strip.Items.Add(button);
        var form = ShowWith(strip);

        var point = new Avalonia.Point(button.Bounds.Left + 5, button.Bounds.Top + 5);
        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.True(button.DropDown.IsOpen);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_split_buttons_face_and_arrow_do_different_things()
    {
        var strip = new ToolStrip { Font = Font, Bounds = new Rectangle(0, 0, 200, 25) };
        var split = new ToolStripSplitButton("Save") { AutoSize = false, Size = new Size(60, 23) };
        split.DropDownItems.Add(new ToolStripMenuItem("Save As"));
        strip.Items.Add(split);
        var form = ShowWith(strip);

        var buttonClicks = 0;
        split.ButtonClick += (_, _) => buttonClicks++;

        var facePoint = new Avalonia.Point(split.Bounds.Left + 5, split.Bounds.Top + 5);
        form.Host!.MouseDown(facePoint, MouseButton.Left);
        form.Host!.MouseUp(facePoint, MouseButton.Left);

        Assert.Equal(1, buttonClicks);
        Assert.False(split.DropDown.IsOpen);

        var arrowPoint = new Avalonia.Point(split.Bounds.Right - 3, split.Bounds.Top + 5);
        form.Host!.MouseDown(arrowPoint, MouseButton.Left);

        Assert.True(split.DropDown.IsOpen);
        Assert.Equal(1, buttonClicks);

        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    // --- ContextMenuStrip -----------------------------------------------------

    [AvaloniaFact]
    public void Right_clicking_a_control_shows_its_context_menu_with_SourceControl_set()
    {
        var target = new Panel { Bounds = new Rectangle(10, 10, 100, 60) };
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add(new ToolStripMenuItem("Copy"));
        target.ContextMenuStripInternal = menu;
        var form = ShowWith(target);

        var point = new Avalonia.Point(target.Bounds.Left + 10, target.Bounds.Top + 10);
        form.Host!.MouseDown(point, MouseButton.Right);
        form.Host!.MouseUp(point, MouseButton.Right);

        Assert.True(menu.IsOpen);
        Assert.Same(target, menu.SourceControl);

        // A ContextMenuStrip is only referenced from Control.ContextMenuStripInternal, not parented
        // in any Controls collection, so disposing the form does not cascade to it.
        menu.Dispose();
        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Opening_can_be_cancelled_and_Closing_reports_the_reason()
    {
        var target = new Panel { Bounds = new Rectangle(10, 10, 100, 60) };
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add(new ToolStripMenuItem("Copy"));
        target.ContextMenuStripInternal = menu;
        var form = ShowWith(target);

        var cancelNext = true;
        menu.Opening += (_, e) => e.Cancel = cancelNext;

        var point = new Avalonia.Point(target.Bounds.Left + 10, target.Bounds.Top + 10);
        form.Host!.MouseDown(point, MouseButton.Right);
        form.Host!.MouseUp(point, MouseButton.Right);
        Assert.False(menu.IsOpen);

        cancelNext = false;
        ToolStripDropDownCloseReason? reason = null;
        menu.Closing += (_, e) => reason = e.CloseReason;

        form.Host!.MouseDown(point, MouseButton.Right);
        form.Host!.MouseUp(point, MouseButton.Right);
        Assert.True(menu.IsOpen);

        menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        Assert.Equal(ToolStripDropDownCloseReason.CloseCalled, reason);
        Assert.False(menu.IsOpen);

        menu.Dispose();
        form.Close();
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_cancelled_close_leaves_the_open_submenu_open()
    {
        var bar = new MenuStrip { Font = Font };
        var file = new ToolStripMenuItem("File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add("one.txt");
        file.DropDownItems.Add(recent);
        bar.Items.Add(file);
        var form = ShowWith(bar);
        file.ShowDropDown();
        recent.ShowDropDown();
        file.DropDown.Closing += (_, e) => e.Cancel = true;

        file.DropDown.Close(ToolStripDropDownCloseReason.CloseCalled);

        Assert.True(file.DropDown.IsOpen);
        Assert.True(recent.DropDown.IsOpen);
        form.Close();
    }

    [AvaloniaFact]
    public void Removing_an_item_whose_menu_is_open_closes_the_menu()
    {
        var bar = new MenuStrip { Font = Font };
        var file = new ToolStripMenuItem("File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add("one.txt");
        file.DropDownItems.Add(recent);
        bar.Items.Add(file);
        var form = ShowWith(bar);
        file.ShowDropDown();
        recent.ShowDropDown();

        file.DropDownItems.Remove(recent);

        Assert.False(recent.DropDown.IsOpen);
        Assert.DoesNotContain(recent.DropDown, System.Windows.Forms.Internal.Hosting.Popups.All);
        form.Close();
    }

    [AvaloniaFact]
    public void A_menu_shown_at_the_screen_corner_stays_on_screen()
    {
        var menu = new ContextMenuStrip { Font = Font };
        menu.Items.Add(new ToolStripMenuItem("A rather long menu entry"));
        menu.Items.Add(new ToolStripMenuItem("Another"));
        var form = ShowWith(new Panel());
        var area = Screen.GetWorkingArea(new Point(0, 0));

        menu.Show(new Point(area.Right - 2, area.Bottom - 2));

        var bounds = menu.Bounds;
        Assert.True(area.Contains(bounds), $"{bounds} is not inside {area}");
        menu.Close();
        form.Close();
    }

    [AvaloniaFact]
    public void A_menu_shortcut_fires_while_another_control_has_the_focus()
    {
        var bar = new MenuStrip { Font = Font };
        var file = new ToolStripMenuItem("File");
        var save = new ToolStripMenuItem("Save") { ShortcutKeys = Keys.Control | Keys.S };
        var clicks = 0;
        save.Click += (_, _) => clicks++;
        file.DropDownItems.Add(save);
        bar.Items.Add(file);
        var box = new TextBox { Top = 40 };
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.AddRange([bar, box]);
        form.MainMenuStrip = bar;
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.S, RawInputModifiers.Control);
        form.Host!.KeyReleaseQwerty(PhysicalKey.S, RawInputModifiers.Control);

        Assert.Equal(1, clicks);
        form.Close();
    }

    [Fact]
    public void Setting_ContextMenuStrip_raises_its_event_and_a_disposed_menu_is_dropped()
    {
        var control = new Control();
        var menu = new ContextMenuStrip();
        var changes = 0;
        control.ContextMenuStripChanged += (_, _) => changes++;

        control.ContextMenuStrip = menu;
        Assert.Same(menu, control.ContextMenuStrip);

        menu.Dispose();
        Assert.Null(control.ContextMenuStrip);
        Assert.Equal(2, changes);
    }

    [AvaloniaFact]
    public void Removing_an_item_closes_its_menu_even_when_Closing_cancels()
    {
        var bar = new MenuStrip { Font = Font };
        var file = new ToolStripMenuItem("File");
        var recent = new ToolStripMenuItem("Recent");
        recent.DropDownItems.Add("one.txt");
        file.DropDownItems.Add(recent);
        bar.Items.Add(file);
        var form = ShowWith(bar);
        file.ShowDropDown();
        recent.ShowDropDown();
        recent.DropDown.Closing += (_, e) => e.Cancel = true;

        file.DropDownItems.Remove(recent);

        Assert.False(recent.DropDown.IsOpen);
        form.Close();
    }

    [Fact]
    public void A_disposed_control_no_longer_listens_to_its_menu()
    {
        var control = new Control();
        var menu = new ContextMenuStrip();
        control.ContextMenuStrip = menu;
        var changes = 0;
        control.ContextMenuStripChanged += (_, _) => changes++;

        control.Dispose();
        menu.Dispose();

        Assert.Equal(0, changes);
    }

    [AvaloniaFact]
    public void A_hidden_item_keeps_its_shortcut_but_a_disabled_one_does_not()
    {
        var bar = new MenuStrip { Font = Font };
        var edit = new ToolStripMenuItem("Edit");
        var hidden = new ToolStripMenuItem("Hidden") { ShortcutKeys = Keys.Control | Keys.H, Visible = false };
        var disabled = new ToolStripMenuItem("Disabled") { ShortcutKeys = Keys.Control | Keys.D, Enabled = false };
        var hiddenClicks = 0;
        var disabledClicks = 0;
        hidden.Click += (_, _) => hiddenClicks++;
        disabled.Click += (_, _) => disabledClicks++;
        edit.DropDownItems.AddRange([hidden, disabled]);
        bar.Items.Add(edit);
        var box = new TextBox { Top = 40 };
        var form = new Form { ClientSize = new Size(400, 200), Font = Font };
        form.Controls.AddRange([bar, box]);
        form.Show();
        Dispatcher.UIThread.RunJobs();
        box.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.H, RawInputModifiers.Control);
        form.Host!.KeyReleaseQwerty(PhysicalKey.H, RawInputModifiers.Control);
        form.Host!.KeyPressQwerty(PhysicalKey.D, RawInputModifiers.Control);
        form.Host!.KeyReleaseQwerty(PhysicalKey.D, RawInputModifiers.Control);

        Assert.Equal(1, hiddenClicks);
        Assert.Equal(0, disabledClicks);
        form.Close();
    }

    [AvaloniaFact]
    public void A_menu_taller_than_the_screen_keeps_its_top_on_screen()
    {
        var menu = new ContextMenuStrip { Font = Font };
        var area = Screen.GetWorkingArea(new Point(0, 0));
        while (menu.GetPreferredSize(Size.Empty).Height <= area.Height) menu.Items.Add(new ToolStripMenuItem("Entry"));
        var form = ShowWith(new Panel());

        menu.Show(new Point(area.Left + 10, area.Top + area.Height / 2));

        Assert.Equal(area.Top, menu.Bounds.Top);
        menu.Close();
        form.Close();
    }
}
