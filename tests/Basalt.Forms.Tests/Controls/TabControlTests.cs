using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>TabControl and TabPage: selection, the strip's geometry, keyboard cycling and the classic raised tab.</summary>
public class TabControlTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(300, 200) };
        control.Font = Font;
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static Bitmap Draw(Control control)
    {
        var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        return bitmap;
    }

    [Fact]
    public void Adding_the_first_page_selects_it_and_only_it_is_visible()
    {
        var tabs = new TabControl();
        var one = new TabPage("One");
        var two = new TabPage("Two");

        tabs.TabPages.Add(one);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(one.Visible);

        tabs.TabPages.Add(two);
        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(one.Visible);
        Assert.False(two.Visible);

        tabs.SelectedIndex = 1;
        Assert.False(one.Visible);
        Assert.True(two.Visible);
    }

    [Fact]
    public void Only_TabPages_can_be_added_to_the_Controls_collection()
    {
        var tabs = new TabControl();

        Assert.Throws<ArgumentException>(() => tabs.Controls.Add(new Button()));
    }

    [Fact]
    public void Selection_events_fire_in_the_order_WinForms_documents()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));

        var order = new List<string>();
        tabs.Deselecting += (_, _) => order.Add("Deselecting");
        tabs.Selecting += (_, _) => order.Add("Selecting");
        tabs.Deselected += (_, _) => order.Add("Deselected");
        tabs.Selected += (_, _) => order.Add("Selected");
        tabs.SelectedIndexChanged += (_, _) => order.Add("SelectedIndexChanged");

        tabs.SelectedIndex = 1;

        Assert.Equal(["Deselecting", "Selecting", "Deselected", "Selected", "SelectedIndexChanged"], order);
    }

    [Fact]
    public void Cancelling_Selecting_leaves_the_old_page_selected_and_visible()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        tabs.Selecting += (_, e) => e.Cancel = true;

        tabs.SelectedIndex = 1;

        Assert.Equal(0, tabs.SelectedIndex);
        Assert.True(tabs.TabPages[0].Visible);
        Assert.False(tabs.TabPages[1].Visible);
    }

    [Fact]
    public void Cancelling_Deselecting_also_leaves_the_selection_unchanged()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        tabs.Deselecting += (_, e) => e.Cancel = true;

        tabs.SelectedIndex = 1;

        Assert.Equal(0, tabs.SelectedIndex);
    }

    [Fact]
    public void TabPage_Enter_and_Leave_fire_as_the_selection_moves_between_pages()
    {
        var tabs = new TabControl();
        var one = new TabPage("One");
        var two = new TabPage("Two");
        tabs.TabPages.AddRange(one, two);

        var entered = 0;
        var left = 0;
        two.Enter += (_, _) => entered++;
        one.Leave += (_, _) => left++;

        tabs.SelectedIndex = 1;

        Assert.Equal(1, entered);
        Assert.Equal(1, left);
    }

    [Fact]
    public void DisplayRectangle_excludes_the_strip_and_the_classic_raised_page_border()
    {
        var tabs = new TabControl
        {
            Font = Font,
            Bounds = new Rectangle(0, 0, 200, 150),
            Padding = new Point(6, 3),
        };
        tabs.TabPages.Add(new TabPage("One"));

        // One row: the font's line plus twice the vertical padding, plus the 2px the selected tab bulges up
        // by, plus the page's own 2px raised border on every side (the classic look this control draws).
        var rowHeight = Font.Height + tabs.Padding.Y * 2;
        var expectedTop = rowHeight + 2 + 2;
        var expected = new Rectangle(2, expectedTop, tabs.ClientSize.Width - 4, tabs.ClientSize.Height - expectedTop - 2);

        Assert.Equal(expected, tabs.DisplayRectangle);
    }

    [Fact]
    public void GetTabRect_widths_follow_the_tab_s_text_in_Normal_size_mode()
    {
        var tabs = new TabControl { Font = Font, Bounds = new Rectangle(0, 0, 400, 150) };
        tabs.TabPages.AddRange(new TabPage("Short"), new TabPage("A longer caption"));

        var shortWidth = TextRenderer.MeasureText("Short", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + tabs.Padding.X * 2;
        var longWidth = TextRenderer.MeasureText("A longer caption", Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + tabs.Padding.X * 2;

        Assert.Equal(shortWidth, tabs.GetTabRect(0).Width);
        Assert.Equal(longWidth, tabs.GetTabRect(1).Width);
        Assert.True(longWidth > shortWidth, "A longer caption must draw a wider tab than a short one.");
    }

    [Fact]
    public void SizeMode_Fixed_gives_every_tab_the_same_ItemSize_width_regardless_of_text()
    {
        var tabs = new TabControl { Font = Font, Bounds = new Rectangle(0, 0, 400, 150) };
        tabs.TabPages.AddRange(new TabPage("A"), new TabPage("A much, much longer caption than the first"));

        tabs.SizeMode = TabSizeMode.Fixed;
        tabs.ItemSize = new Size(90, 22);

        Assert.Equal(90, tabs.GetTabRect(0).Width);
        Assert.Equal(90, tabs.GetTabRect(1).Width);
        Assert.Equal(22, tabs.GetTabRect(0).Height);
    }

    [Fact]
    public void Multiline_wraps_tabs_that_do_not_fit_one_row_into_a_second_row()
    {
        var tabs = new TabControl { Font = Font, Bounds = new Rectangle(0, 0, 120, 150), Multiline = true };
        tabs.TabPages.AddRange(new TabPage("One caption"), new TabPage("Two caption"), new TabPage("Three caption"));

        Assert.True(tabs.RowCount > 1, $"Expected the strip to wrap into more than one row, got {tabs.RowCount}.");
        Assert.True(tabs.GetTabRect(2).Y > tabs.GetTabRect(0).Y, "The third tab should have wrapped onto a lower row than the first.");
    }

    [Fact]
    public void A_single_row_strip_reports_RowCount_one()
    {
        var tabs = new TabControl { Font = Font, Bounds = new Rectangle(0, 0, 400, 150) };
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));

        Assert.Equal(1, tabs.RowCount);
        Assert.Equal(2, tabs.TabCount);
    }

    [AvaloniaFact]
    public void The_selected_tab_is_drawn_raised_above_its_neighbours()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        ShowWith(tabs);

        var rects = new[] { tabs.GetTabRect(0), tabs.GetTabRect(1) };
        var bitmap = Draw(tabs);

        // The selected tab (index 0) bulges two pixels above the unraised row every tab shares;
        // at that row a column inside the selected tab must already show the tab's border, while
        // the same row over the unselected tab is still the plain strip background.
        var raisedRow = rects[0].Top - 1;
        var selectedColumn = rects[0].Left + rects[0].Width / 2;
        var unselectedColumn = rects[1].Left + rects[1].Width / 2;

        var overSelected = bitmap.GetPixel(selectedColumn, raisedRow);
        var overUnselected = bitmap.GetPixel(unselectedColumn, raisedRow);

        Assert.NotEqual(overSelected, overUnselected);
    }

    [AvaloniaFact]
    public void A_mouse_click_on_a_tab_selects_it_and_focuses_the_strip()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        var form = ShowWith(tabs);

        var secondTab = tabs.GetTabRect(1);
        var point = new Avalonia.Point(tabs.Left + secondTab.Left + secondTab.Width / 2, tabs.Top + secondTab.Top + secondTab.Height / 2);

        form.Host!.MouseDown(point, MouseButton.Left);
        form.Host!.MouseUp(point, MouseButton.Left);

        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(tabs.Focused);

        form.Close();
    }

    [AvaloniaFact]
    public void Ctrl_Tab_cycles_pages_forward_and_wraps_past_the_last_one()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"), new TabPage("Three"));
        var form = ShowWith(tabs);
        tabs.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal(1, tabs.SelectedIndex);

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal(2, tabs.SelectedIndex);

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control);
        Assert.Equal(0, tabs.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Ctrl_Shift_Tab_cycles_pages_backward()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"), new TabPage("Three"));
        var form = ShowWith(tabs);
        tabs.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.Control | RawInputModifiers.Shift);

        Assert.Equal(2, tabs.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Ctrl_PageDown_and_PageUp_also_cycle_pages()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        var form = ShowWith(tabs);
        tabs.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.Control);
        Assert.Equal(1, tabs.SelectedIndex);

        form.Host!.KeyPressQwerty(PhysicalKey.PageUp, RawInputModifiers.Control);
        Assert.Equal(0, tabs.SelectedIndex);

        form.Close();
    }

    [AvaloniaFact]
    public void Left_and_Right_cycle_pages_when_the_strip_itself_has_the_focus()
    {
        var tabs = new TabControl();
        tabs.TabPages.AddRange(new TabPage("One"), new TabPage("Two"));
        var form = ShowWith(tabs);
        tabs.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(1, tabs.SelectedIndex);

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(0, tabs.SelectedIndex);

        form.Close();
    }

    [Fact]
    public void Inserting_a_page_before_the_selected_one_keeps_the_same_page_selected()
    {
        var tabs = new TabControl { Size = new Size(200, 150) };
        var first = new TabPage("A");
        var second = new TabPage("B");
        tabs.TabPages.Add(first);
        tabs.TabPages.Add(second);

        tabs.TabPages.Insert(0, new TabPage("C"));

        Assert.Same(first, tabs.SelectedTab);
        Assert.Equal(1, tabs.SelectedIndex);
        Assert.True(first.Visible);
    }

    [Fact]
    public void Pages_fill_the_page_area_without_their_Dock_being_changed()
    {
        var tabs = new TabControl { Size = new Size(200, 150) };
        var page = new TabPage("A");

        tabs.TabPages.Add(page);
        tabs.PerformLayout();

        Assert.Equal(DockStyle.None, page.Dock);
        Assert.Equal(tabs.DisplayRectangle, page.Bounds);

        tabs.Size = new Size(300, 200);
        Assert.Equal(tabs.DisplayRectangle, page.Bounds);
    }
}
