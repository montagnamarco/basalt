using System.ComponentModel;
using System.Drawing;
using System.Linq;

namespace System.Windows.Forms;

/// <summary>
/// A month-at-a-time calendar for picking one date or, up to
/// <see cref="MaxSelectionCount"/>, a contiguous range: a title bar with the
/// month, the year and two navigation arrows, a day-of-week header, a 6x7
/// grid of days and an optional "Today" footer.
/// </summary>
/// <remarks>
/// MonthCalendar is native in WinForms (its MONTHCAL window paints and
/// handles input itself): this class uses the <see cref="Control.Native"/>
/// hooks (<see cref="DefaultPaint"/>, <see cref="DefaultMouseDown"/> and the
/// rest) rather than OnPaint/OnMouseDown/OnKeyDown, exactly as the members
/// Microsoft overrides for this control (and no others) show.
/// </remarks>
[DefaultProperty("SelectionRange")]
[DefaultEvent("DateChanged")]
public partial class MonthCalendar : Control
{
    /// <summary>The oldest and newest date Windows' month calendar can ever show, regardless of MinDate/MaxDate.</summary>
    private static readonly DateTime AbsoluteMinDate = new(1753, 1, 1);

    private static readonly DateTime AbsoluteMaxDate = new(9998, 12, 31);

    private readonly List<DateTime> _boldedDates = [];
    private readonly List<DateTime> _annuallyBoldedDates = [];
    private readonly List<DateTime> _monthlyBoldedDates = [];

    private DateTime _minDate = AbsoluteMinDate;
    private DateTime _maxDate = AbsoluteMaxDate;
    private DateTime _selectionStart = DateTime.Today;
    private DateTime _selectionEnd = DateTime.Today;

    /// <summary>The first day of the month currently displayed in the grid (always day 1).</summary>
    private DateTime _currentMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private DateTime _todayDate = DateTime.Today;
    private bool _todayDateSet;

    private int _maxSelectionCount = 7;
    private int _scrollChange;
    private Day _firstDayOfWeek = Day.Default;

    /// <summary>Empty until explicitly set: DefaultSize is read by the base constructor before this is (1,1).</summary>
    private Size _calendarDimensions;

    private bool _showToday = true;
    private bool _showTodayCircle = true;
    private bool _showWeekNumbers;
    private bool _rightToLeftLayout;

    private Color _titleBackColor = SystemColors.ActiveCaption;
    private Color _titleForeColor = SystemColors.ActiveCaptionText;
    private Color _trailingForeColor = SystemColors.GrayText;

    public MonthCalendar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.Selectable, true);

        _focusDate = _selectionStart;

        // The base Control constructor reads DefaultSize (hence SingleMonthSize)
        // before this type's own field initializers (ShowToday and the rest)
        // have run, so the very first computation is missing their effect.
        // Setting the size again now, with every field at its real value, fixes it.
        Size = SingleMonthSize;
    }

    protected override Padding DefaultMargin => new(9);

    protected override Size DefaultSize => SingleMonthSize;

    /// <summary>The size one month takes at the current font; CalendarDimensions beyond (1,1) is not drawn.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Size SingleMonthSize => ComputeMetrics().TotalSize;

    /// <summary>How many months are shown side by side; only (1,1) is actually drawn by this implementation.</summary>
    [Localizable(true)]
    public Size CalendarDimensions
    {
        get => _calendarDimensions.IsEmpty ? new Size(1, 1) : _calendarDimensions;
        set => SetCalendarDimensions(value.Width, value.Height);
    }

    public void SetCalendarDimensions(int x, int y)
    {
        if (x < 1) throw new ArgumentOutOfRangeException(nameof(x), x, "'x' must be greater than or equal to 1.");
        if (y < 1) throw new ArgumentOutOfRangeException(nameof(y), y, "'y' must be greater than or equal to 1.");

        var dimensions = new Size(x, y);
        if (dimensions == CalendarDimensions) return;

        _calendarDimensions = dimensions;
        Invalidate();
    }

    /// <summary>The month background; Window unless the application set one, matching the native control.</summary>
    public override Color BackColor
    {
        get => ShouldSerializeBackColor() ? base.BackColor : SystemColors.Window;
        set => base.BackColor = value;
    }

    public override Color ForeColor
    {
        get => ShouldSerializeForeColor() ? base.ForeColor : SystemColors.WindowText;
        set => base.ForeColor = value;
    }

    protected override void OnBackColorChanged(EventArgs e)
    {
        base.OnBackColorChanged(e);
        Invalidate();
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        Invalidate();
    }

    public Color TitleBackColor
    {
        get => _titleBackColor;
        set
        {
            if (value == _titleBackColor) return;

            _titleBackColor = value;
            Invalidate();
        }
    }

    public Color TitleForeColor
    {
        get => _titleForeColor;
        set
        {
            if (value == _titleForeColor) return;

            _titleForeColor = value;
            Invalidate();
        }
    }

    public Color TrailingForeColor
    {
        get => _trailingForeColor;
        set
        {
            if (value == _trailingForeColor) return;

            _trailingForeColor = value;
            Invalidate();
        }
    }

    [DefaultValue(Day.Default)]
    [Localizable(true)]
    public Day FirstDayOfWeek
    {
        get => _firstDayOfWeek;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(Day));
            if (value == _firstDayOfWeek) return;

            _firstDayOfWeek = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowToday
    {
        get => _showToday;
        set
        {
            if (value == _showToday) return;

            _showToday = value;

            // The footer row (or its absence) changes how tall one month is.
            Size = SingleMonthSize;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    public bool ShowTodayCircle
    {
        get => _showTodayCircle;
        set
        {
            if (value == _showTodayCircle) return;

            _showTodayCircle = value;
            Invalidate();
        }
    }

    // Stored and raised only: no week-number column is drawn yet (listed as a known gap).
    [DefaultValue(false)]
    [Localizable(true)]
    public bool ShowWeekNumbers
    {
        get => _showWeekNumbers;
        set
        {
            if (value == _showWeekNumbers) return;

            _showWeekNumbers = value;
            Invalidate();
        }
    }

    // Stored and raised only: the calendar is not mirrored yet (listed as a known gap).
    [DefaultValue(false)]
    [Localizable(true)]
    public virtual bool RightToLeftLayout
    {
        get => _rightToLeftLayout;
        set
        {
            if (value == _rightToLeftLayout) return;

            _rightToLeftLayout = value;
            OnRightToLeftLayoutChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    protected virtual void OnRightToLeftLayoutChanged(EventArgs e) => ((EventHandler?)Events[s_rightToLeftLayoutChangedEvent])?.Invoke(this, e);

    public event EventHandler? RightToLeftLayoutChanged
    {
        add => Events.AddHandler(s_rightToLeftLayoutChangedEvent, value);
        remove => Events.RemoveHandler(s_rightToLeftLayoutChangedEvent, value);
    }

    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            if (value == _minDate) return;

            if (value < AbsoluteMinDate || value > AbsoluteMaxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"'{value}' is not a valid value for 'MinDate'. 'MinDate' must be between {AbsoluteMinDate:d} and {AbsoluteMaxDate:d}.");
            }

            if (value > _maxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "'MinDate' must be less than or equal to 'MaxDate'.");
            }

            _minDate = value;
            ClampSelectionToBounds();

            var minMonth = new DateTime(value.Year, value.Month, 1);
            if (_currentMonth < minMonth) _currentMonth = minMonth;

            Invalidate();
        }
    }

    public DateTime MaxDate
    {
        get => _maxDate;
        set
        {
            if (value == _maxDate) return;

            if (value < AbsoluteMinDate || value > AbsoluteMaxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"'{value}' is not a valid value for 'MaxDate'. 'MaxDate' must be between {AbsoluteMinDate:d} and {AbsoluteMaxDate:d}.");
            }

            if (value < _minDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "'MaxDate' must be greater than or equal to 'MinDate'.");
            }

            _maxDate = value;
            ClampSelectionToBounds();

            var maxMonth = new DateTime(value.Year, value.Month, 1);
            if (_currentMonth > maxMonth) _currentMonth = maxMonth;

            Invalidate();
        }
    }

    /// <summary>Pulls the current selection back inside [MinDate, MaxDate] after either bound moved; no exception, as Windows adjusts itself.</summary>
    private void ClampSelectionToBounds()
    {
        var start = Clamp(_selectionStart);
        var end = Clamp(_selectionEnd);
        if (start > end) end = start;

        if (start == _selectionStart && end == _selectionEnd) return;

        _selectionStart = start;
        _selectionEnd = end;
        OnDateChanged(new DateRangeEventArgs(_selectionStart, _selectionEnd));
    }

    /// <summary>Keeps a day inside the bounds; the bounds' days only, since MinDate = DateTime.Now must not put a time of day into the selection.</summary>
    private DateTime Clamp(DateTime date)
    {
        if (date < _minDate.Date) return _minDate.Date;
        if (date > _maxDate.Date) return _maxDate.Date;

        return date;
    }

    [DefaultValue(7)]
    public int MaxSelectionCount
    {
        get => _maxSelectionCount;
        set
        {
            if (value < 1) throw new ArgumentOutOfRangeException(nameof(value), value, "'MaxSelectionCount' must be greater than or equal to 1.");

            _maxSelectionCount = value;

            var maxEnd = _selectionStart.AddDays(value - 1);
            if (_selectionEnd > maxEnd) ApplySelectionRange(_selectionStart, maxEnd);
        }
    }

    [DefaultValue(0)]
    public int ScrollChange
    {
        get => _scrollChange;
        set
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), value, "'ScrollChange' must be greater than or equal to 0.");

            _scrollChange = value;
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime SelectionStart
    {
        get => _selectionStart;
        set => SetSelectionRange(value, _selectionEnd);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime SelectionEnd
    {
        get => _selectionEnd;
        set => SetSelectionRange(_selectionStart, value);
    }

    [Bindable(true)]
    public SelectionRange SelectionRange
    {
        get => new(_selectionStart, _selectionEnd);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            SetSelectionRange(value.Start, value.End);
        }
    }

    public void SetSelectionRange(DateTime date1, DateTime date2)
    {
        var start = date1 < date2 ? date1 : date2;
        var end = date1 < date2 ? date2 : date1;

        if (start < _minDate || end > _maxDate)
        {
            throw new ArgumentOutOfRangeException(nameof(date1), "SelectionStart and SelectionEnd must be between MinDate and MaxDate.");
        }

        // A span over MaxSelectionCount keeps its earlier day, as Windows does.
        ApplySelectionRange(start, end);
        NavigateToMonthOf(_selectionStart);
    }

    public void SetDate(DateTime date)
    {
        if (date < _minDate || date > _maxDate)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "'date' must be between MinDate and MaxDate.");
        }

        ApplySelectionRange(date, date);
        _focusDate = date.Date;
        NavigateToMonthOf(date);
    }

    /// <summary>
    /// Strips the time of day, keeps the moving end within MaxSelectionCount
    /// days of the anchor (so a backward drag or Shift+arrow stops short
    /// instead of dropping the anchor), reorders, and raises DateChanged when
    /// the result differs.
    /// </summary>
    private void ApplySelectionRange(DateTime anchor, DateTime moving)
    {
        anchor = anchor.Date;
        moving = moving.Date;

        var maxSpan = Math.Max(_maxSelectionCount - 1, 0);
        if ((moving - anchor).Days > maxSpan) moving = anchor.AddDays(maxSpan);
        else if ((anchor - moving).Days > maxSpan) moving = anchor.AddDays(-maxSpan);

        var date1 = anchor < moving ? anchor : moving;
        var date2 = anchor < moving ? moving : anchor;

        if (date1 == _selectionStart && date2 == _selectionEnd) return;

        _selectionStart = date1;
        _selectionEnd = date2;

        OnDateChanged(new DateRangeEventArgs(_selectionStart, _selectionEnd));
        Invalidate();
    }

    private void NavigateToMonthOf(DateTime date)
    {
        var month = new DateTime(date.Year, date.Month, 1);
        if (month == _currentMonth) return;

        _currentMonth = month;
        Invalidate();
    }

    public DateTime TodayDate
    {
        get => _todayDate;
        set
        {
            if (value < _minDate || value > _maxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "'TodayDate' must be between MinDate and MaxDate.");
            }

            _todayDate = value.Date;
            _todayDateSet = true;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool TodayDateSet => _todayDateSet;

    [Localizable(true)]
    public DateTime[] BoldedDates
    {
        get => [.. _boldedDates];
        set
        {
            _boldedDates.Clear();
            if (value is not null) _boldedDates.AddRange(value.Select(d => d.Date));

            Invalidate();
        }
    }

    [Localizable(true)]
    public DateTime[] AnnuallyBoldedDates
    {
        get => [.. _annuallyBoldedDates];
        set
        {
            _annuallyBoldedDates.Clear();
            if (value is not null) _annuallyBoldedDates.AddRange(value.Select(d => d.Date));

            Invalidate();
        }
    }

    [Localizable(true)]
    public DateTime[] MonthlyBoldedDates
    {
        get => [.. _monthlyBoldedDates];
        set
        {
            _monthlyBoldedDates.Clear();
            if (value is not null) _monthlyBoldedDates.AddRange(value.Select(d => d.Date));

            Invalidate();
        }
    }

    /// <summary>Adds to the bolded set; like Windows, the change is not drawn until <see cref="UpdateBoldedDates"/> runs.</summary>
    public void AddBoldedDate(DateTime date) => _boldedDates.Add(date.Date);

    public void AddAnnuallyBoldedDate(DateTime date) => _annuallyBoldedDates.Add(date.Date);

    public void AddMonthlyBoldedDate(DateTime date) => _monthlyBoldedDates.Add(date.Date);

    public void RemoveBoldedDate(DateTime date) => _boldedDates.RemoveAll(d => d.Date == date.Date);

    public void RemoveAnnuallyBoldedDate(DateTime date) => _annuallyBoldedDates.RemoveAll(d => d.Month == date.Month && d.Day == date.Day);

    public void RemoveMonthlyBoldedDate(DateTime date) => _monthlyBoldedDates.RemoveAll(d => d.Day == date.Day);

    public void RemoveAllBoldedDates() => _boldedDates.Clear();

    public void RemoveAllAnnuallyBoldedDates() => _annuallyBoldedDates.Clear();

    public void RemoveAllMonthlyBoldedDates() => _monthlyBoldedDates.Clear();

    /// <summary>Applies whatever Add/RemoveBoldedDate (and the Annually/Monthly equivalents) queued up, by repainting.</summary>
    public void UpdateBoldedDates() => Invalidate();

    private bool IsBoldDate(DateTime date)
    {
        var day = date.Date;

        foreach (var bold in _boldedDates)
        {
            if (bold == day) return true;
        }

        foreach (var bold in _annuallyBoldedDates)
        {
            if (bold.Month == day.Month && bold.Day == day.Day) return true;
        }

        foreach (var bold in _monthlyBoldedDates)
        {
            if (bold.Day == day.Day) return true;
        }

        return false;
    }

    /// <summary>The dates currently in the grid: every one of them when visible, or only the displayed month's own days.</summary>
    public SelectionRange GetDisplayRange(bool visible)
    {
        if (!visible) return new SelectionRange(_currentMonth, _currentMonth.AddMonths(1).AddDays(-1));

        var first = FirstVisibleDate();

        return new SelectionRange(first, first.AddDays(41));
    }

    public override string ToString() => $"{base.ToString()}, SelectionRange: {SelectionRange}";

    private static readonly object s_dateChangedEvent = new();
    private static readonly object s_dateSelectedEvent = new();
    private static readonly object s_rightToLeftLayoutChangedEvent = new();

    public event DateRangeEventHandler? DateChanged
    {
        add => Events.AddHandler(s_dateChangedEvent, value);
        remove => Events.RemoveHandler(s_dateChangedEvent, value);
    }

    protected virtual void OnDateChanged(DateRangeEventArgs drevent) => ((DateRangeEventHandler?)Events[s_dateChangedEvent])?.Invoke(this, drevent);

    public event DateRangeEventHandler? DateSelected
    {
        add => Events.AddHandler(s_dateSelectedEvent, value);
        remove => Events.RemoveHandler(s_dateSelectedEvent, value);
    }

    protected virtual void OnDateSelected(DateRangeEventArgs drevent) => ((DateRangeEventHandler?)Events[s_dateSelectedEvent])?.Invoke(this, drevent);

    public sealed class HitTestInfo
    {
        internal HitTestInfo(HitArea hitArea, Point point, DateTime time)
        {
            HitArea = hitArea;
            Point = point;
            Time = time;
        }

        public HitArea HitArea { get; }

        public Point Point { get; }

        public DateTime Time { get; }
    }

    public enum HitArea
    {
        Nowhere = 0,
        TitleBackground = 1,
        TitleMonth = 2,
        TitleYear = 3,
        NextMonthButton = 4,
        PrevMonthButton = 5,
        CalendarBackground = 6,
        Date = 7,
        NextMonthDate = 8,
        PrevMonthDate = 9,
        DayOfWeek = 10,
        WeekNumbers = 11,
        TodayLink = 12,
    }
}
