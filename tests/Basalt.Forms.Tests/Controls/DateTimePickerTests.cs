using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.Internal.Hosting;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// DateTimePicker: Value/MinDate/MaxDate validation, Format/CustomFormat,
/// ShowCheckBox/Checked, classic painting, field-by-field keyboard editing,
/// and the drop-down calendar.
/// </summary>
public class DateTimePickerTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form Show(params Control[] controls)
    {
        var form = new Form { ClientSize = new Size(320, 200) };
        form.Controls.AddRange(controls);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    /// <summary>Runs the action under en-US, so Short/Long/Time patterns and DateTime.TryParse are deterministic, then restores the original culture.</summary>
    private static void WithEnglishCulture(Action action)
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            action();
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    private static void ClickButton(Form form, DateTimePicker picker)
    {
        var x = picker.Left + picker.Width - DateTimePicker.ButtonWidth / 2;
        var y = picker.Top + picker.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    private static void ClickField(Form form, DateTimePicker picker, int xOffsetInField)
    {
        var x = picker.Left + DateTimePicker.FieldInset + xOffsetInField;
        var y = picker.Top + picker.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    private static void ClickSpinButton(Form form, DateTimePicker picker, bool up)
    {
        var x = picker.Left + picker.Width - DateTimePicker.ButtonWidth / 2;
        var y = picker.Top + (up ? picker.Height / 4 : picker.Height * 3 / 4);
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);
    }

    /// <summary>Scans the calendar for the first point HitTest reports as a day cell, black-box, as MonthCalendarTests does.</summary>
    private static Point LocateDateCell(MonthCalendar calendar)
    {
        for (var y = 0; y < calendar.Height; y += 2)
        {
            for (var x = 0; x < calendar.Width; x += 2)
            {
                if (calendar.HitTest(x, y).HitArea == MonthCalendar.HitArea.Date) return new Point(x, y);
            }
        }

        throw new InvalidOperationException("No date cell found on the displayed month.");
    }

    private static void Press(Form form, PhysicalKey key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        form.Host!.KeyPressQwerty(key, modifiers);
        form.Host!.KeyReleaseQwerty(key, modifiers);
    }

    private static void Type(Form form, string text)
    {
        foreach (var character in text) form.Host!.KeyTextInput(character.ToString());
    }

    private static int CountPixelsOfColor(Control control, Rectangle area, Color color)
    {
        using var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        var target = color.ToArgb();
        var count = 0;

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() == target) count++;
            }
        }

        return count;
    }

    private static double AverageBrightness(Control control, Rectangle area)
    {
        using var bitmap = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppArgb);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));

        long sum = 0;
        var count = 0;

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                sum += pixel.R + pixel.G + pixel.B;
                count++;
            }
        }

        return count == 0 ? 0 : (double)sum / count;
    }

    // --- Value, MinDate/MaxDate: validation and clamping ---

    [Fact]
    public void Value_outside_MinDate_and_MaxDate_throws()
    {
        var picker = new DateTimePicker { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };

        Assert.Throws<ArgumentOutOfRangeException>(() => picker.Value = new DateTime(2023, 12, 31));
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.Value = new DateTime(2025, 1, 1));
    }

    [Fact]
    public void MinDate_above_MaxDate_and_MaxDate_below_MinDate_throw()
    {
        var picker = new DateTimePicker { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };

        Assert.Throws<ArgumentOutOfRangeException>(() => picker.MinDate = new DateTime(2025, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => picker.MaxDate = new DateTime(2023, 1, 1));
    }

    [Fact]
    public void Lowering_MaxDate_below_Value_clamps_it_and_raises_ValueChanged()
    {
        var picker = new DateTimePicker { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31), Value = new DateTime(2024, 6, 15) };
        var raised = 0;
        picker.ValueChanged += (_, _) => raised++;

        picker.MaxDate = new DateTime(2024, 6, 10);

        Assert.Equal(new DateTime(2024, 6, 10), picker.Value);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void MinimumDateTime_and_MaximumDateTime_match_the_absolute_bounds_of_MinDateTime_and_MaxDateTime()
    {
        Assert.Equal(DateTimePicker.MinDateTime, DateTimePicker.MinimumDateTime);
        Assert.Equal(DateTimePicker.MaxDateTime, DateTimePicker.MaximumDateTime);
        Assert.Equal(new DateTime(1753, 1, 1), DateTimePicker.MinDateTime);
        Assert.Equal(new DateTime(9998, 12, 31), DateTimePicker.MaxDateTime);
    }

    // --- Format / CustomFormat ---

    [Fact]
    public void Changing_Format_raises_FormatChanged_and_changes_the_formatted_Text() => WithEnglishCulture(() =>
    {
        var picker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(2024, 3, 5) };
        var raised = 0;
        picker.FormatChanged += (_, _) => raised++;
        var shortText = picker.Text;

        picker.Format = DateTimePickerFormat.Long;

        Assert.Equal(1, raised);
        Assert.NotEqual(shortText, picker.Text);
        Assert.Contains("2024", picker.Text);
    });

    [Fact]
    public void CustomFormat_is_used_only_once_Format_is_Custom() => WithEnglishCulture(() =>
    {
        var picker = new DateTimePicker { Value = new DateTime(2024, 3, 5), CustomFormat = "yyyy-MM-dd" };

        // Format is still Long: the custom pattern has no effect yet.
        Assert.DoesNotContain("2024-03-05", picker.Text);

        picker.Format = DateTimePickerFormat.Custom;

        Assert.Equal("2024-03-05", picker.Text);
    });

    [Fact]
    public void Setting_Text_to_a_parseable_date_within_bounds_applies_it_as_Value_does() => WithEnglishCulture(() =>
    {
        var picker = new DateTimePicker { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };

        picker.Text = "2024-05-20";

        Assert.Equal(new DateTime(2024, 5, 20), picker.Value.Date);
    });

    // --- ShowCheckBox / Checked ---

    [Fact]
    public void Unchecked_greys_the_text()
    {
        var picker = new DateTimePicker { Font = new Font("Tahoma", 14f), Bounds = new Rectangle(0, 0, 260, 30), ShowCheckBox = true, Checked = true };
        var area = new Rectangle(DateTimePicker.CheckBoxAreaWidth, 0, picker.Width - DateTimePicker.CheckBoxAreaWidth - DateTimePicker.ButtonWidth, picker.Height);

        var checkedBrightness = AverageBrightness(picker, area);

        picker.Checked = false;
        var uncheckedBrightness = AverageBrightness(picker, area);

        Assert.True(uncheckedBrightness > checkedBrightness, $"checked={checkedBrightness} unchecked={uncheckedBrightness}");
    }

    [AvaloniaFact]
    public void Clicking_the_check_box_toggles_Checked()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), ShowCheckBox = true, Checked = true };
        var form = Show(picker);

        var x = picker.Left + DateTimePicker.FieldInset + 6;
        var y = picker.Top + picker.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);

        Assert.False(picker.Checked);

        form.Close();
    }

    [AvaloniaFact]
    public void A_users_tick_raises_ValueChanged_but_setting_Checked_from_code_does_not()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), ShowCheckBox = true, Checked = true };
        var form = Show(picker);
        var changes = 0;
        picker.ValueChanged += (_, _) => changes++;

        picker.Checked = false;
        Assert.Equal(0, changes);

        var x = picker.Left + DateTimePicker.FieldInset + 6;
        var y = picker.Top + picker.Height / 2;
        form.Host!.MouseDown(new Avalonia.Point(x, y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(x, y), MouseButton.Left);

        Assert.True(picker.Checked);
        Assert.Equal(1, changes);

        form.Close();
    }

    // --- Classic painting ---

    [Fact]
    public void PreferredHeight_grows_with_a_bigger_font()
    {
        var small = new DateTimePicker { Font = new Font("Tahoma", 8f) };
        var big = new DateTimePicker { Font = new Font("Tahoma", 20f) };

        Assert.True(big.PreferredHeight > small.PreferredHeight);
    }

    [Fact]
    public void The_sunken_edge_shrinks_the_client_area_on_every_side()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(0, 0, 200, 24) };

        Assert.Equal(new Size(196, 20), picker.ClientSize);
    }

    [AvaloniaFact]
    public void The_focused_field_is_painted_with_the_Highlight_colour()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 3, 5) };
        var other = new Button { Bounds = new Rectangle(10, 50, 80, 24) };
        var form = Show(picker, other);
        other.Focus();

        var unfocusedHighlightPixels = CountPixelsOfColor(picker, picker.ClientRectangle, SystemColors.Highlight);
        Assert.Equal(0, unfocusedHighlightPixels);

        picker.Focus();
        var focusedHighlightPixels = CountPixelsOfColor(picker, picker.ClientRectangle, SystemColors.Highlight);
        Assert.True(focusedHighlightPixels > 0);

        other.Focus();
        var refocusedElsewherePixels = CountPixelsOfColor(picker, picker.ClientRectangle, SystemColors.Highlight);
        Assert.Equal(0, refocusedElsewherePixels);

        form.Close();
    }

    // --- Field-by-field keyboard editing ---

    [AvaloniaFact]
    public void Right_moves_to_the_next_field_and_Up_steps_it_wrapping_within_its_own_range()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 1, 31) };
        var form = Show(picker);
        picker.Focus();

        // Month is field 0; Right moves to Day (field 1).
        Press(form, PhysicalKey.ArrowRight);
        Press(form, PhysicalKey.ArrowUp);

        // Day 31 of January, stepped up, wraps to day 1 of the same month instead of spilling into February.
        Assert.Equal(new DateTime(2024, 1, 1), picker.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Down_on_the_month_field_wraps_from_January_to_December_without_changing_the_year()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 1, 15) };
        var form = Show(picker);
        picker.Focus();

        Press(form, PhysicalKey.ArrowDown);

        Assert.Equal(new DateTime(2024, 12, 15), picker.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_digits_sets_each_field_and_advances_once_it_is_full()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 1, 1) };
        var form = Show(picker);
        picker.Focus();

        Type(form, "12"); // Month field: 1, then 2 -> 12, full at two digits, advances to Day.
        Type(form, "31"); // Day field: 3, then 1 -> 31, full at two digits, advances to Year.
        Type(form, "1999"); // Year field: four digits.

        Assert.Equal(new DateTime(1999, 12, 31), picker.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void A_day_typed_past_the_months_length_restarts_the_buffer_instead_of_producing_an_invalid_day()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 3, 1) };
        var form = Show(picker);
        picker.Focus();
        Press(form, PhysicalKey.ArrowRight); // Day field.

        // March has 31 days: "3" alone could still become "31", but "35" cannot stand, so the buffer restarts at "5".
        Type(form, "35");

        Assert.Equal(5, picker.Value.Day);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_the_check_box_while_unchecked_blocks_field_keys_until_it_is_checked()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", ShowCheckBox = true, Checked = false, Value = new DateTime(2024, 1, 1) };
        var form = Show(picker);
        picker.Focus();

        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal(new DateTime(2024, 1, 1), picker.Value);

        picker.Checked = true;
        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal(new DateTime(2024, 2, 1), picker.Value);

        form.Close();
    }

    // --- Drop-down calendar ---

    [AvaloniaFact]
    public void Clicking_the_button_opens_the_calendar_on_Value_and_raises_DropDown()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);
        var raised = 0;
        picker.DropDown += (_, _) => raised++;

        ClickButton(form, picker);

        Assert.Equal(1, raised);
        var popup = Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        var calendar = (MonthCalendar)popup.Controls[0];
        Assert.Equal(new DateTime(2024, 3, 5), calendar.SelectionStart);

        form.Close();
    }

    [AvaloniaFact]
    public void Clicking_a_date_in_the_calendar_sets_Value_and_closes_raising_CloseUp()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);
        var closed = 0;
        picker.CloseUp += (_, _) => closed++;

        ClickButton(form, picker);
        var popup = Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        var calendar = (MonthCalendar)popup.Controls[0];

        var dateCell = LocateDateCell(calendar);
        popup.PopupWindow!.MouseDown(new Avalonia.Point(dateCell.X, dateCell.Y), MouseButton.Left);
        popup.PopupWindow!.MouseUp(new Avalonia.Point(dateCell.X, dateCell.Y), MouseButton.Left);

        Assert.Equal(1, closed);
        Assert.DoesNotContain(popup, Popups.All);
        Assert.Equal(calendar.SelectionStart.Date, picker.Value.Date);

        form.Close();
    }

    [AvaloniaFact]
    public void A_year_is_applied_once_all_its_digits_are_typed_and_zero_never_crashes()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);
        picker.Focus();
        var changes = new List<DateTime>();
        picker.ValueChanged += (_, _) => changes.Add(picker.Value);

        Type(form, "201");
        Assert.Empty(changes);
        Assert.StartsWith("201-", picker.Text);

        Type(form, "9");
        Assert.Equal([new DateTime(2019, 3, 5)], changes);

        // A lone 0 left in the year field is not year 0: it is applied clamped to MinDate, never thrown.
        Press(form, PhysicalKey.ArrowLeft); // Back from the month to the year.
        Type(form, "0");
        Press(form, PhysicalKey.ArrowRight);

        Assert.Equal(picker.MinDate, picker.Value);
        form.Close();
    }

    [AvaloniaFact]
    public void A_half_typed_year_is_dropped_by_a_format_change_and_by_Up_without_an_extra_change()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "yyyy-MM-dd", Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);
        picker.Focus();
        var changes = new List<DateTime>();
        picker.ValueChanged += (_, _) => changes.Add(picker.Value);

        Type(form, "201");
        picker.CustomFormat = "yyyy/MM/dd";
        Assert.Empty(changes);

        Type(form, "201");
        Press(form, PhysicalKey.ArrowUp);
        Assert.Equal([new DateTime(2025, 3, 5)], changes);

        form.Close();
    }

    [AvaloniaFact]
    public void Typing_a_day_never_takes_Value_below_MinDate()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy", MinDate = new DateTime(2024, 6, 15), Value = new DateTime(2024, 6, 20) };
        var form = Show(picker);
        picker.Focus();
        var seen = new List<DateTime>();
        picker.ValueChanged += (_, _) => seen.Add(picker.Value);

        Type(form, "1");

        Assert.All(seen, date => Assert.True(date >= picker.MinDate, $"{date:d} is below MinDate"));
        Assert.True(picker.Value >= picker.MinDate);
        form.Close();
    }

    [AvaloniaFact]
    public void Tabbing_away_closes_the_dropdown()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Value = new DateTime(2024, 3, 5) };
        var next = new TextBox { Bounds = new Rectangle(10, 50, 100, 20) };
        var form = Show(picker, next);
        picker.Focus();
        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);

        next.Focus();

        Assert.DoesNotContain(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        form.Close();
    }

    [AvaloniaFact]
    public void Alt_Down_opens_the_dropdown_and_Escape_closes_it_without_changing_Value()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);
        picker.Focus();

        Press(form, PhysicalKey.ArrowDown, RawInputModifiers.Alt);
        Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);

        Press(form, PhysicalKey.Escape);

        Assert.DoesNotContain(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        Assert.Equal(new DateTime(2024, 3, 5), picker.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void A_press_outside_the_picker_closes_the_dropdown_without_changing_Value()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), Value = new DateTime(2024, 3, 5) };
        var form = Show(picker);

        ClickButton(form, picker);
        Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);

        form.Host!.MouseDown(new Avalonia.Point(300, 190), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(300, 190), MouseButton.Left);

        Assert.DoesNotContain(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        Assert.Equal(new DateTime(2024, 3, 5), picker.Value);

        form.Close();
    }

    [AvaloniaFact]
    public void ShowUpDown_shows_spin_buttons_and_the_button_click_no_longer_opens_a_calendar()
    {
        var picker = new DateTimePicker { Bounds = new Rectangle(10, 10, 200, 24), ShowUpDown = true, Format = DateTimePickerFormat.Custom, CustomFormat = "MM/dd/yyyy", Value = new DateTime(2024, 1, 1) };
        var form = Show(picker);

        ClickSpinButton(form, picker, up: true);

        Assert.DoesNotContain(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        // The up spin button stepped the focused (month) field instead of opening a calendar.
        Assert.Equal(2, picker.Value.Month);

        form.Close();
    }

    [AvaloniaFact]
    public void Calendar_colours_and_font_are_forwarded_to_the_drop_down()
    {
        var calendarFont = new Font("Tahoma", 12f);
        var picker = new DateTimePicker
        {
            Bounds = new Rectangle(10, 10, 200, 24),
            CalendarTitleBackColor = Color.FromArgb(255, 10, 20, 30),
            CalendarTitleForeColor = Color.FromArgb(255, 40, 50, 60),
            CalendarTrailingForeColor = Color.FromArgb(255, 70, 80, 90),
            CalendarMonthBackground = Color.FromArgb(255, 100, 110, 120),
            CalendarFont = calendarFont,
        };
        var form = Show(picker);

        ClickButton(form, picker);
        var popup = Assert.Single(Popups.All, control => control.Controls.Count == 1 && control.Controls[0] is MonthCalendar);
        var calendar = (MonthCalendar)popup.Controls[0];

        Assert.Equal(picker.CalendarTitleBackColor, calendar.TitleBackColor);
        Assert.Equal(picker.CalendarTitleForeColor, calendar.TitleForeColor);
        Assert.Equal(picker.CalendarTrailingForeColor, calendar.TrailingForeColor);
        Assert.Equal(picker.CalendarMonthBackground, calendar.BackColor);
        Assert.Equal(calendarFont, calendar.Font);

        form.Close();
    }

    /// <summary>
    /// Reintroducing the bug ("new" instead of "override" on Value/Name) would make Value null here:
    /// a screen reader, like this test, reads AccessibleObject.Value through the base type, which a
    /// "new" member hides from (it only answers through DateTimePickerAccessibleObject itself).
    /// </summary>
    [Fact]
    public void AccessibilityObject_Value_reads_the_formatted_date_through_the_base_type()
    {
        WithEnglishCulture(() =>
        {
            var picker = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = new DateTime(2024, 1, 2) };
            AccessibleObject accessible = picker.AccessibilityObject;
            var direct = (DateTimePicker.DateTimePickerAccessibleObject)accessible;

            Assert.Equal(picker.Text, accessible.Value);
            Assert.Equal(direct.Value, accessible.Value);
            Assert.Equal(direct.Name, accessible.Name);
        });
    }
}
