using System.Drawing;
using System.Windows.Forms.Internal.Hosting;

namespace System.Windows.Forms;

/// <summary>
/// The calendar drop-down: a borderless popup (<see cref="PopupWindow"/>,
/// the same mechanism <see cref="ToolTip"/> uses) holding one
/// <see cref="MonthCalendar"/>, opened on <see cref="Value"/> below the
/// control and closed by picking a date, Enter, Escape, or a press outside it.
/// </summary>
public partial class DateTimePicker
{
    private DropDownHolder? _dropDownHolder;
    private bool _dropped;

    private DropDownHolder EnsureDropDownHolder() => _dropDownHolder ??= new DropDownHolder(this);

    /// <summary>Pushes the Calendar* properties onto the drop-down's MonthCalendar; harmless, and cheap, before it is shown.</summary>
    private void ApplyCalendarAppearance()
    {
        var holder = _dropDownHolder;
        if (holder is null) return;

        holder.VisualStylesOverride = PortableVisualStylesEnabled;
        holder.VisualStylePlatformOverride = PortableVisualStylePlatform;
        var calendar = holder.Calendar;

        calendar.Font = CalendarFont;
        if (_calendarForeColor is { } foreColor) calendar.ForeColor = foreColor;
        if (_calendarMonthBackground is { } background) calendar.BackColor = background;
        if (_calendarTitleBackColor is { } titleBackground) calendar.TitleBackColor = titleBackground;
        if (_calendarTitleForeColor is { } titleForeground) calendar.TitleForeColor = titleForeground;
        if (_calendarTrailingForeColor is { } trailingForeground) calendar.TrailingForeColor = trailingForeground;
    }

    private void ToggleDropDown()
    {
        if (_dropped) CloseDropDown(commit: false);
        else OpenDropDown();
    }

    private void OpenDropDown()
    {
        if (_dropped || _showUpDown) return;

        var holder = EnsureDropDownHolder();
        ApplyCalendarAppearance();

        var calendar = holder.Calendar;
        calendar.MinDate = _minDate;
        calendar.MaxDate = _maxDate;
        calendar.SetDate(_value.Date);

        var size = calendar.SingleMonthSize;
        var topLeft = PointToScreen(new Point(0, Height));
        if (_dropDownAlign == LeftRightAlignment.Right) topLeft.X = PointToScreen(new Point(Width, Height)).X - size.Width;

        Popups.Show(holder, this, new Rectangle(topLeft, size));
        calendar.Focus();

        _dropped = true;
        Invalidate();
        OnDropDown(EventArgs.Empty);
    }

    /// <summary>Closes the calendar; when committing, the clicked, typed or Enter-confirmed date replaces Value's date, keeping its time of day.</summary>
    internal void CloseDropDown(bool commit)
    {
        if (!_dropped) return;

        _dropped = false;

        if (commit && _dropDownHolder is { } open)
        {
            SetValueClamped(open.Calendar.SelectionStart.Date + _value.TimeOfDay);
        }

        if (_dropDownHolder is { } holder) Popups.Close(holder);

        Invalidate();
        OnCloseUp(EventArgs.Empty);
    }

    /// <summary>The popup window: a plain container, borderless, whose only child is the calendar it shows.</summary>
    private sealed class DropDownHolder : ContainerControl
    {
        private readonly DateTimePicker _owner;

        public DropDownHolder(DateTimePicker owner)
        {
            _owner = owner;
            SetTopLevel(true);
            VisualStylesOverride = owner.PortableVisualStylesEnabled;
            VisualStylePlatformOverride = owner.PortableVisualStylePlatform;

            Calendar = new MonthCalendar();
            Calendar.DateSelected += (_, _) => _owner.CloseDropDown(commit: true);
            Calendar.KeyDown += OnCalendarKeyDown;

            Controls.Add(Calendar);
        }

        public MonthCalendar Calendar { get; }

        /// <summary>The owner's Alt+Down/F4 opened this: while it is open, the calendar gets the owner window's keys (Escape, Enter, the arrows).</summary>
        internal override bool TakesKeyboardWhileOpen => true;

        /// <summary>A press elsewhere, or the owner's window losing activation, closes this without applying a new date.</summary>
        internal override void DismissPopup() => _owner.CloseDropDown(commit: false);

        private void OnCalendarKeyDown(object? sender, KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Escape:
                    e.Handled = true;
                    _owner.CloseDropDown(commit: false);
                    break;

                case Keys.Enter:
                case Keys.Space:
                    e.Handled = true;
                    _owner.CloseDropDown(commit: true);
                    break;
            }
        }
    }
}
