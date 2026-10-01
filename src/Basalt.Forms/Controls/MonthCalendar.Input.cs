namespace System.Windows.Forms;

/// <summary>
/// Mouse and keyboard, through the native-control hooks in
/// <see cref="Control.Native"/>: a click selects a date, a drag extends the
/// selection up to <see cref="MonthCalendar.MaxSelectionCount"/>, the arrows
/// and the Today link navigate, and the arrow keys, Page Up/Down and
/// Home/End move the keyboard-focused date the same way Windows does.
/// </summary>
public partial class MonthCalendar
{
    private enum ArrowButton
    {
        None,
        Prev,
        Next,
    }

    /// <summary>The date Left/Right/Up/Down, Page Up/Down and Home/End move from; starts at SelectionStart.</summary>
    private DateTime _focusDate;

    private bool _dragging;
    private DateTime _dragAnchorDate;
    private ArrowButton _pressedArrow = ArrowButton.None;

    protected override bool IsInputKey(Keys keyData)
    {
        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left or Keys.Right or Keys.Up or Keys.Down:
            case Keys.PageUp or Keys.PageDown:
            case Keys.Home or Keys.End:
                return true;
        }

        return base.IsInputKey(keyData);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    internal override void DefaultLostFocus() => Invalidate();

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        var hit = HitTest(e.Location);

        switch (hit.HitArea)
        {
            case HitArea.PrevMonthButton:
                _pressedArrow = ArrowButton.Prev;
                Invalidate();
                NavigateMonths(-1);
                break;

            case HitArea.NextMonthButton:
                _pressedArrow = ArrowButton.Next;
                Invalidate();
                NavigateMonths(1);
                break;

            case HitArea.TodayLink:
                SetDate(TodayDate);
                break;

            case HitArea.Date:
            case HitArea.PrevMonthDate:
            case HitArea.NextMonthDate:
                BeginDateDrag(hit.Time);
                break;
        }
    }

    private void BeginDateDrag(DateTime date)
    {
        date = date.Date;
        if (date < MinDate.Date || date > MaxDate.Date) return;

        _dragging = true;
        _dragAnchorDate = date;
        _focusDate = date;

        NavigateToMonthOf(date);
        ApplySelectionRange(date, date);
    }

    internal override void DefaultMouseMove(MouseEventArgs e)
    {
        if (!_dragging || !Capture) return;

        var hit = HitTest(e.Location);
        if (hit.HitArea is not (HitArea.Date or HitArea.PrevMonthDate or HitArea.NextMonthDate)) return;

        var date = hit.Time.Date;
        if (date < MinDate.Date || date > MaxDate.Date) return;

        _focusDate = date;
        ApplySelectionRange(_dragAnchorDate, date);
    }

    internal override void DefaultMouseUp(MouseEventArgs e)
    {
        if (_pressedArrow != ArrowButton.None)
        {
            _pressedArrow = ArrowButton.None;
            Invalidate();
        }

        if (!_dragging) return;

        _dragging = false;
        OnDateSelected(new DateRangeEventArgs(_selectionStart, _selectionEnd));
    }

    internal override void DefaultCaptureLost()
    {
        _dragging = false;

        if (_pressedArrow == ArrowButton.None) return;

        _pressedArrow = ArrowButton.None;
        Invalidate();
    }

    /// <summary>Moves the displayed month by ScrollChange months (one, when it is zero), clamped to Min/MaxDate's months.</summary>
    private void NavigateMonths(int direction)
    {
        var count = _scrollChange > 0 ? _scrollChange : 1;
        var target = _currentMonth.AddMonths(direction * count);

        var earliest = new DateTime(_minDate.Year, _minDate.Month, 1);
        var latest = new DateTime(_maxDate.Year, _maxDate.Month, 1);
        if (target < earliest) target = earliest;
        if (target > latest) target = latest;

        if (target == _currentMonth) return;

        _currentMonth = target;
        Invalidate();
    }

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
                MoveFocusByDays(-1, e.Shift);
                e.Handled = true;
                break;

            case Keys.Right:
                MoveFocusByDays(1, e.Shift);
                e.Handled = true;
                break;

            case Keys.Up:
                MoveFocusByDays(-7, e.Shift);
                e.Handled = true;
                break;

            case Keys.Down:
                MoveFocusByDays(7, e.Shift);
                e.Handled = true;
                break;

            case Keys.PageUp:
                MoveFocusByMonths(e.Control ? -12 : -1);
                e.Handled = true;
                break;

            case Keys.PageDown:
                MoveFocusByMonths(e.Control ? 12 : 1);
                e.Handled = true;
                break;

            case Keys.Home:
                MoveFocusTo(new DateTime(_currentMonth.Year, _currentMonth.Month, 1));
                e.Handled = true;
                break;

            case Keys.End:
                MoveFocusTo(new DateTime(_currentMonth.Year, _currentMonth.Month, 1).AddMonths(1).AddDays(-1));
                e.Handled = true;
                break;
        }
    }

    private void MoveFocusByDays(int days, bool extend)
    {
        // The anchor is the end of the selection the focus is not on: it stays put however far Shift+arrows go back and forth.
        var anchor = _focusDate == _selectionStart ? _selectionEnd : _selectionStart;
        var target = Clamp(_focusDate.AddDays(days));
        _focusDate = target;

        if (extend) ApplySelectionRange(anchor, target);
        else ApplySelectionRange(target, target);

        NavigateToMonthOf(target);
    }

    private void MoveFocusByMonths(int months)
    {
        var target = Clamp(_focusDate.AddMonths(months));
        _focusDate = target;

        ApplySelectionRange(target, target);
        NavigateToMonthOf(target);
    }

    private void MoveFocusTo(DateTime date)
    {
        var target = Clamp(date);
        _focusDate = target;

        ApplySelectionRange(target, target);
        NavigateToMonthOf(target);
    }
}
