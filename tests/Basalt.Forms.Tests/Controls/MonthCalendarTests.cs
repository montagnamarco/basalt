using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.Windows.Forms;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace Basalt.Forms.Tests.Controls;

/// <summary>
/// MonthCalendar: selection rules and the events they raise, MinDate/MaxDate
/// clamping and exceptions, keyboard navigation, HitTest, bolded dates drawn
/// bold, and the culture's first day of week.
/// </summary>
public class MonthCalendarTests
{
    private static readonly Font Font = new("Microsoft Sans Serif", 8.25f);

    private static Form ShowWith(Control control)
    {
        var form = new Form { ClientSize = new Size(320, 240) };
        form.Controls.Add(control);
        form.Show();
        Dispatcher.UIThread.RunJobs();

        return form;
    }

    private static MonthCalendar NewCalendar() => new() { Font = Font, Bounds = new Rectangle(0, 0, 300, 220) };

    /// <summary>Scans the control for the first point HitTest reports as matching the predicate, black-box.</summary>
    private static Point Locate(MonthCalendar calendar, Func<MonthCalendar.HitTestInfo, bool> predicate)
    {
        for (var y = 0; y < calendar.Height; y += 2)
        {
            for (var x = 0; x < calendar.Width; x += 2)
            {
                if (predicate(calendar.HitTest(x, y))) return new Point(x, y);
            }
        }

        throw new InvalidOperationException("No point on the control satisfies the predicate.");
    }

    private static Point LocateDate(MonthCalendar calendar, DateTime date) =>
        Locate(calendar, hit => hit.HitArea is MonthCalendar.HitArea.Date or MonthCalendar.HitArea.PrevMonthDate or MonthCalendar.HitArea.NextMonthDate
            && hit.Time.Date == date.Date);

    // --- Selection rules and the events they raise ---

    [AvaloniaFact]
    public void Clicking_a_date_selects_it_and_raises_DateChanged_then_DateSelected_on_release()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 1));
        var form = ShowWith(calendar);

        var changed = new List<DateTime>();
        var selected = new List<DateTime>();
        calendar.DateChanged += (_, e) => changed.Add(e.Start);
        calendar.DateSelected += (_, e) => selected.Add(e.Start);

        var target = new DateTime(2024, 3, 15);
        var point = LocateDate(calendar, target);

        form.Host!.MouseDown(new Avalonia.Point(point.X, point.Y), MouseButton.Left);

        Assert.Equal(target, calendar.SelectionStart);
        Assert.Equal(target, calendar.SelectionEnd);
        Assert.Contains(target, changed);
        Assert.Empty(selected);

        form.Host!.MouseUp(new Avalonia.Point(point.X, point.Y), MouseButton.Left);

        Assert.Equal([target], selected);

        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_extends_the_selection_but_never_past_MaxSelectionCount()
    {
        var calendar = NewCalendar();
        calendar.MaxSelectionCount = 3;
        calendar.SetDate(new DateTime(2024, 3, 1));
        var form = ShowWith(calendar);

        var start = new DateTime(2024, 3, 10);
        var farBeyondTheLimit = new DateTime(2024, 3, 20);
        var startPoint = LocateDate(calendar, start);
        var endPoint = LocateDate(calendar, farBeyondTheLimit);

        form.Host!.MouseDown(new Avalonia.Point(startPoint.X, startPoint.Y), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(endPoint.X, endPoint.Y));

        Assert.Equal(start, calendar.SelectionStart);
        // Three days total (MaxSelectionCount = 3): start, start+1, start+2 - never reaching the 20th.
        Assert.Equal(start.AddDays(2), calendar.SelectionEnd);

        form.Host!.MouseUp(new Avalonia.Point(endPoint.X, endPoint.Y), MouseButton.Left);
        form.Close();
    }

    [AvaloniaFact]
    public void Dragging_backward_past_MaxSelectionCount_keeps_the_day_the_drag_started_on()
    {
        var calendar = NewCalendar();
        calendar.MaxSelectionCount = 3;
        calendar.SetDate(new DateTime(2024, 3, 1));
        var form = ShowWith(calendar);

        var start = new DateTime(2024, 3, 20);
        var startPoint = LocateDate(calendar, start);
        var endPoint = LocateDate(calendar, new DateTime(2024, 3, 5));

        form.Host!.MouseDown(new Avalonia.Point(startPoint.X, startPoint.Y), MouseButton.Left);
        form.Host!.MouseMove(new Avalonia.Point(endPoint.X, endPoint.Y), RawInputModifiers.LeftMouseButton);

        Assert.Equal(new DateTime(2024, 3, 18), calendar.SelectionStart);
        Assert.Equal(start, calendar.SelectionEnd);

        form.Host!.MouseUp(new Avalonia.Point(endPoint.X, endPoint.Y), MouseButton.Left);
        form.Close();
    }

    [Fact]
    public void A_MinDate_with_a_time_of_day_pulls_the_selection_back_to_a_whole_day()
    {
        var calendar = new MonthCalendar();
        calendar.SetDate(new DateTime(2024, 1, 1));

        calendar.MinDate = new DateTime(2024, 1, 2, 8, 0, 0);

        Assert.Equal(new DateTime(2024, 1, 2), calendar.SelectionStart);
        Assert.Equal(new DateTime(2024, 1, 2), calendar.SelectionEnd);
    }

    [Fact]
    public void SetSelectionRange_outside_MinDate_and_MaxDate_throws()
    {
        var calendar = new MonthCalendar { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };

        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SetSelectionRange(new DateTime(2025, 1, 1), new DateTime(2025, 1, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.SelectionStart = new DateTime(2023, 12, 31));
    }

    // --- MinDate/MaxDate clamping and exceptions ---

    [Fact]
    public void MinDate_above_MaxDate_and_MaxDate_below_MinDate_throw()
    {
        var calendar = new MonthCalendar { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };

        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.MinDate = new DateTime(2025, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => calendar.MaxDate = new DateTime(2023, 1, 1));
    }

    [Fact]
    public void Lowering_MaxDate_below_the_current_selection_pulls_it_back_instead_of_throwing()
    {
        var calendar = new MonthCalendar { MinDate = new DateTime(2024, 1, 1), MaxDate = new DateTime(2024, 12, 31) };
        calendar.SetDate(new DateTime(2024, 6, 15));

        calendar.MaxDate = new DateTime(2024, 6, 10);

        Assert.Equal(new DateTime(2024, 6, 10), calendar.SelectionStart);
        Assert.Equal(new DateTime(2024, 6, 10), calendar.SelectionEnd);
    }

    // --- Keyboard navigation ---

    [AvaloniaFact]
    public void Arrow_keys_move_the_selection_and_shift_extends_it()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 15));
        var form = ShowWith(calendar);
        calendar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        Assert.Equal(new DateTime(2024, 3, 16), calendar.SelectionStart);
        Assert.Equal(new DateTime(2024, 3, 16), calendar.SelectionEnd);

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.Equal(new DateTime(2024, 3, 16), calendar.SelectionStart);
        Assert.Equal(new DateTime(2024, 3, 17), calendar.SelectionEnd);

        form.Close();
    }

    [AvaloniaFact]
    public void Shift_left_twice_grows_the_range_backward_from_the_anchor()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 15));
        var form = ShowWith(calendar);
        calendar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        form.Host!.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        form.Host!.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);
        form.Host!.KeyReleaseQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.Shift);

        Assert.Equal(new DateTime(2024, 3, 13), calendar.SelectionStart);
        Assert.Equal(new DateTime(2024, 3, 15), calendar.SelectionEnd);

        form.Close();
    }

    [AvaloniaFact]
    public void Home_and_End_select_the_edges_of_the_displayed_month()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 15));
        var form = ShowWith(calendar);
        calendar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.Home, RawInputModifiers.None);
        Assert.Equal(new DateTime(2024, 3, 1), calendar.SelectionStart);

        form.Host!.KeyPressQwerty(PhysicalKey.End, RawInputModifiers.None);
        Assert.Equal(new DateTime(2024, 3, 31), calendar.SelectionStart);

        form.Close();
    }

    [AvaloniaFact]
    public void Page_down_moves_the_selection_one_month_forward()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 1, 31));
        var form = ShowWith(calendar);
        calendar.Focus();

        form.Host!.KeyPressQwerty(PhysicalKey.PageDown, RawInputModifiers.None);

        // January 31 has no equivalent in February: DateTime.AddMonths clamps to the last valid day.
        Assert.Equal(new DateTime(2024, 2, 29), calendar.SelectionStart);

        form.Close();
    }

    // --- HitTest ---

    [Fact]
    public void HitTest_finds_the_navigation_arrows_a_date_cell_and_outside_the_control()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 15));

        var prevButton = Locate(calendar, hit => hit.HitArea == MonthCalendar.HitArea.PrevMonthButton);
        Assert.Equal(MonthCalendar.HitArea.PrevMonthButton, calendar.HitTest(prevButton).HitArea);

        var nextButton = Locate(calendar, hit => hit.HitArea == MonthCalendar.HitArea.NextMonthButton);
        Assert.Equal(MonthCalendar.HitArea.NextMonthButton, calendar.HitTest(nextButton).HitArea);

        var dateCell = LocateDate(calendar, new DateTime(2024, 3, 15));
        var dateHit = calendar.HitTest(dateCell);
        Assert.Equal(MonthCalendar.HitArea.Date, dateHit.HitArea);
        Assert.Equal(new DateTime(2024, 3, 15), dateHit.Time.Date);

        Assert.Equal(MonthCalendar.HitArea.Nowhere, calendar.HitTest(-10, -10).HitArea);
    }

    [AvaloniaFact]
    public void Clicking_the_next_month_arrow_changes_the_displayed_month_without_changing_the_selection()
    {
        var calendar = NewCalendar();
        calendar.SetDate(new DateTime(2024, 3, 15));
        var form = ShowWith(calendar);
        var selectionBefore = calendar.SelectionStart;

        var nextButton = Locate(calendar, hit => hit.HitArea == MonthCalendar.HitArea.NextMonthButton);
        form.Host!.MouseDown(new Avalonia.Point(nextButton.X, nextButton.Y), MouseButton.Left);
        form.Host!.MouseUp(new Avalonia.Point(nextButton.X, nextButton.Y), MouseButton.Left);

        Assert.Equal(new DateTime(2024, 4, 1), calendar.GetDisplayRange(false).Start);
        Assert.Equal(selectionBefore, calendar.SelectionStart);

        form.Close();
    }

    // --- Bolded dates are drawn bold ---

    private static Rectangle CellBoundsOf(MonthCalendar calendar, DateTime date)
    {
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        for (var y = 0; y < calendar.Height; y++)
        {
            for (var x = 0; x < calendar.Width; x++)
            {
                var hit = calendar.HitTest(x, y);
                if (hit.HitArea != MonthCalendar.HitArea.Date || hit.Time.Date != date.Date) continue;

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX) throw new InvalidOperationException($"{date:d} is not in the currently displayed grid.");

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    private static int InkPixelCount(MonthCalendar calendar, Rectangle area)
    {
        using var bitmap = new Bitmap(calendar.Width, calendar.Height, PixelFormat.Format32bppArgb);
        calendar.DrawToBitmap(bitmap, new Rectangle(0, 0, calendar.Width, calendar.Height));

        // Color's == compares the knownColor/name state as well as the ARGB
        // bits, so a named SystemColors constant never equals a plain pixel
        // read back even when the bits match; ToArgb() strips that down to
        // the bits actually painted.
        var background = calendar.BackColor.ToArgb();
        var count = 0;

        for (var y = area.Top; y < area.Bottom; y++)
        {
            for (var x = area.Left; x < area.Right; x++)
            {
                if (bitmap.GetPixel(x, y).ToArgb() != background) count++;
            }
        }

        return count;
    }

    // Tahoma has a visibly heavier bold weight on the fonts this test runs
    // with; Microsoft Sans Serif resolves to the same outlines bold or not.
    private static readonly Font BoldCapableFont = new("Tahoma", 10f);

    [Fact]
    public void A_bolded_date_paints_more_ink_than_the_same_date_plain()
    {
        var calendar = NewCalendar();
        calendar.Font = BoldCapableFont;
        calendar.ShowToday = false;
        calendar.SetDate(new DateTime(2024, 3, 1));
        var target = new DateTime(2024, 3, 20);
        var cell = CellBoundsOf(calendar, target);

        var plainInk = InkPixelCount(calendar, cell);

        calendar.BoldedDates = [target];
        var boldInk = InkPixelCount(calendar, cell);

        Assert.True(boldInk > plainInk, $"plain={plainInk} bold={boldInk}");
    }

    [Fact]
    public void AddBoldedDate_has_no_effect_until_UpdateBoldedDates_is_called()
    {
        var calendar = NewCalendar();
        calendar.Font = BoldCapableFont;
        calendar.ShowToday = false;
        calendar.SetDate(new DateTime(2024, 3, 1));
        var target = new DateTime(2024, 3, 20);
        var cell = CellBoundsOf(calendar, target);

        var plainInk = InkPixelCount(calendar, cell);

        calendar.AddBoldedDate(target);
        Assert.Equal(plainInk, InkPixelCount(calendar, cell));

        calendar.UpdateBoldedDates();
        Assert.True(InkPixelCount(calendar, cell) > plainInk);
    }

    // --- Culture's first day of week ---

    [Fact]
    public void FirstDayOfWeek_Default_follows_the_current_culture_while_an_explicit_value_overrides_it()
    {
        var original = CultureInfo.CurrentCulture;

        try
        {
            // German: the week starts on Monday.
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var calendar = new MonthCalendar();
            calendar.SetDate(new DateTime(2024, 3, 15));

            Assert.Equal(DayOfWeek.Monday, calendar.GetDisplayRange(true).Start.DayOfWeek);

            calendar.FirstDayOfWeek = Day.Sunday;
            Assert.Equal(DayOfWeek.Sunday, calendar.GetDisplayRange(true).Start.DayOfWeek);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
