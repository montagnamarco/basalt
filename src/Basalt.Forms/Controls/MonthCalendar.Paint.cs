using System.Drawing;
using System.Globalization;
using System.Windows.Forms.Internal.Rendering;

namespace System.Windows.Forms;

/// <summary>
/// Measuring and drawing the classic (Windows 2000) look: a title band with
/// the month, the year and two arrows, a day-of-week header, the 6x7 day
/// grid and the "Today" footer, plus HitTest over the same geometry.
/// </summary>
public partial class MonthCalendar
{
    private bool UsePortableCalendarAppearance => PortableVisualStylesEnabled && !HasExplicitAppearanceBackColor;

    private const int OuterMargin = 4;

    /// <summary>The sizes everything else in the grid is measured from, at the current font.</summary>
    private readonly record struct MonthMetrics(int CellWidth, int CellHeight, int TitleHeight, int HeaderHeight, int FooterHeight)
    {
        public int GridWidth => CellWidth * 7;

        public int GridHeight => CellHeight * 6;

        public Size TotalSize => new(GridWidth + OuterMargin * 2, TitleHeight + HeaderHeight + GridHeight + FooterHeight + OuterMargin * 2);
    }

    /// <summary>Every rectangle DefaultPaint fills and HitTest tests against, computed together so the two never disagree.</summary>
    private sealed class DisplayLayout
    {
        public required MonthMetrics Metrics { get; init; }
        public required Rectangle TitleBar { get; init; }
        public required Rectangle PrevButton { get; init; }
        public required Rectangle NextButton { get; init; }
        public required Rectangle TitleMonthText { get; init; }
        public required Rectangle TitleYearText { get; init; }
        public required Rectangle Header { get; init; }
        public required Rectangle[] HeaderCells { get; init; }
        public required Rectangle Grid { get; init; }
        public required Rectangle[] Cells { get; init; }
        public required DateTime[] CellDates { get; init; }
        public required Rectangle Footer { get; init; }
    }

    private MonthMetrics ComputeMetrics()
    {
        var font = Font;
        var dayNumberSize = TextRenderer.MeasureText("30", font);

        var dayNameWidth = 0;
        var culture = CultureInfo.CurrentCulture;
        for (var i = 0; i < 7; i++)
        {
            var name = culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)i);
            dayNameWidth = Math.Max(dayNameWidth, TextRenderer.MeasureText(name, font).Width);
        }

        var cellWidth = Math.Max(dayNumberSize.Width, dayNameWidth) + 10;
        var cellHeight = dayNumberSize.Height + 6;
        var titleHeight = FontHeight + 8;
        var footerHeight = _showToday ? FontHeight + 8 : 0;

        return new MonthMetrics(cellWidth, cellHeight, titleHeight, cellHeight, footerHeight);
    }

    // HitTest runs on every mouse move and measuring the eleven strings the
    // layout needs dominated it (a per-pixel scan took 19 s), so the layout is
    // reused until one of the inputs it was computed from changes.
    private (Font Font, Rectangle Client, DateTime Month, bool ShowToday, DayOfWeek FirstDay, CultureInfo Culture)? _layoutKey;
    private DisplayLayout? _layout;

    private DisplayLayout ComputeDisplayLayout()
    {
        var key = (Font, ClientRectangle, _currentMonth, _showToday, ResolvedFirstDayOfWeek(), CultureInfo.CurrentCulture);
        if (_layout is null || !_layoutKey.Equals(key))
        {
            _layout = BuildDisplayLayout();
            _layoutKey = key;
        }

        return _layout;
    }

    private DisplayLayout BuildDisplayLayout()
    {
        var metrics = ComputeMetrics();
        var client = ClientRectangle;

        var gridLeft = client.Left + OuterMargin;
        var gridTop = client.Top + OuterMargin;
        var gridWidth = metrics.GridWidth;

        var titleBar = new Rectangle(gridLeft, gridTop, gridWidth, metrics.TitleHeight);

        var arrowSize = Math.Max(Math.Min(titleBar.Height - 4, metrics.CellWidth - 4), 1);
        var arrowTop = titleBar.Top + (titleBar.Height - arrowSize) / 2;
        var prevButton = new Rectangle(titleBar.Left + 2, arrowTop, arrowSize, arrowSize);
        var nextButton = new Rectangle(titleBar.Right - 2 - arrowSize, arrowTop, arrowSize, arrowSize);

        var titleTextArea = Rectangle.FromLTRB(prevButton.Right, titleBar.Top, nextButton.Left, titleBar.Bottom);
        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(_currentMonth.Month);
        var yearText = _currentMonth.Year.ToString(CultureInfo.CurrentCulture);
        var monthWidth = TextRenderer.MeasureText(monthName + " ", Font).Width;
        var totalTextWidth = TextRenderer.MeasureText(monthName + " " + yearText, Font).Width;
        var textStart = titleTextArea.Left + Math.Max((titleTextArea.Width - totalTextWidth) / 2, 0);

        var titleMonthArea = new Rectangle(textStart, titleTextArea.Top, monthWidth, titleTextArea.Height);
        var titleYearArea = new Rectangle(titleMonthArea.Right, titleTextArea.Top, Math.Max(totalTextWidth - monthWidth, 0), titleTextArea.Height);

        var header = new Rectangle(gridLeft, titleBar.Bottom, gridWidth, metrics.HeaderHeight);
        var headerCells = new Rectangle[7];
        for (var i = 0; i < 7; i++)
        {
            headerCells[i] = new Rectangle(gridLeft + i * metrics.CellWidth, header.Top, metrics.CellWidth, header.Height);
        }

        var grid = new Rectangle(gridLeft, header.Bottom, gridWidth, metrics.GridHeight);
        var cells = new Rectangle[42];
        var dates = new DateTime[42];
        var firstGridDate = FirstVisibleDate();

        for (var i = 0; i < 42; i++)
        {
            var row = i / 7;
            var column = i % 7;

            cells[i] = new Rectangle(grid.Left + column * metrics.CellWidth, grid.Top + row * metrics.CellHeight, metrics.CellWidth, metrics.CellHeight);
            dates[i] = firstGridDate.AddDays(i);
        }

        var footer = metrics.FooterHeight > 0
            ? new Rectangle(gridLeft, grid.Bottom, gridWidth, metrics.FooterHeight)
            : Rectangle.Empty;

        return new DisplayLayout
        {
            Metrics = metrics,
            TitleBar = titleBar,
            PrevButton = prevButton,
            NextButton = nextButton,
            TitleMonthText = titleMonthArea,
            TitleYearText = titleYearArea,
            Header = header,
            HeaderCells = headerCells,
            Grid = grid,
            Cells = cells,
            CellDates = dates,
            Footer = footer,
        };
    }

    /// <summary>The grid's first cell: the FirstDayOfWeek-aligned date on or before the displayed month's day 1.</summary>
    private DateTime FirstVisibleDate()
    {
        var firstOfMonth = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
        var firstDow = ResolvedFirstDayOfWeek();
        var offset = ((int)firstOfMonth.DayOfWeek - (int)firstDow + 7) % 7;

        return firstOfMonth.AddDays(-offset);
    }

    private DayOfWeek ResolvedFirstDayOfWeek() =>
        _firstDayOfWeek == Day.Default ? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek : DayToDayOfWeek(_firstDayOfWeek);

    private static DayOfWeek DayToDayOfWeek(Day day) => day == Day.Sunday ? DayOfWeek.Sunday : (DayOfWeek)((int)day + 1);

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var layout = ComputeDisplayLayout();

        using (var back = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(back, ClientRectangle);
        }

        PaintTitleBar(graphics, layout);
        PaintDayHeader(graphics, layout);
        PaintGrid(graphics, layout);
        if (!layout.Footer.IsEmpty) PaintFooter(graphics, layout);
    }

    private void PaintTitleBar(Graphics graphics, DisplayLayout layout)
    {
        var palette = UsePortableCalendarAppearance
            ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
        var titleBackColor = palette is not null && !_titleBackColorExplicit ? palette.Face : _titleBackColor;
        var titleForeColor = palette is not null && !_titleForeColorExplicit ? palette.Text : _titleForeColor;

        using (var back = new SolidBrush(titleBackColor))
        {
            graphics.FillRectangle(back, layout.TitleBar);
        }

        if (palette is not null)
        {
            using var border = new Pen(palette.Border);
            graphics.DrawLine(border, layout.TitleBar.Left, layout.TitleBar.Bottom - 1,
                layout.TitleBar.Right - 1, layout.TitleBar.Bottom - 1);
        }

        // The navigation arrows are flat glyphs on the caption band, not raised
        // buttons: a 3D bevel in the control's face colour would not sit on it.
        DrawTitleArrow(graphics, layout.PrevButton, pointingRight: false,
            _pressedArrow == ArrowButton.Prev, titleForeColor, palette);
        DrawTitleArrow(graphics, layout.NextButton, pointingRight: true,
            _pressedArrow == ArrowButton.Next, titleForeColor, palette);

        var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(_currentMonth.Month);
        var text = $"{monthName} {_currentMonth.Year}";
        var textArea = Rectangle.Union(layout.TitleMonthText, layout.TitleYearText);

        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var brush = new SolidBrush(titleForeColor);
        graphics.DrawString(text, Font, brush, (RectangleF)textArea, format);
    }

    private void DrawTitleArrow(Graphics graphics, Rectangle area, bool pointingRight, bool pressed,
        Color arrowColor, PortableVisualStylePalette? palette)
    {
        if (pressed && palette is not null)
        {
            using var pressedFace = new SolidBrush(palette.PressedFace);
            graphics.FillRectangle(pressedFace, area);
        }

        if (pressed) area.Offset(1, 1);

        var cx = area.Left + area.Width / 2;
        var cy = area.Top + area.Height / 2;
        var half = Math.Max(Math.Min(area.Width, area.Height) / 2 - 2, 1);

        Point[] points = pointingRight
            ? [new Point(cx - half, cy - half), new Point(cx - half, cy + half), new Point(cx + half, cy)]
            : [new Point(cx + half, cy - half), new Point(cx + half, cy + half), new Point(cx - half, cy)];

        using var brush = new SolidBrush(Enabled ? arrowColor : palette?.DisabledText ?? arrowColor);
        graphics.FillPolygon(brush, points);
    }

    private void PaintDayHeader(Graphics graphics, DisplayLayout layout)
    {
        using (var back = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(back, layout.Header);
        }

        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        var palette = UsePortableCalendarAppearance
            ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
        var textColor = !Enabled ? palette?.DisabledText ?? SystemColors.GrayText
            : palette is not null && !HasExplicitAppearanceForeColor ? palette.Text : ForeColor;
        using var brush = new SolidBrush(textColor);

        var culture = CultureInfo.CurrentCulture;
        var firstDow = ResolvedFirstDayOfWeek();

        for (var i = 0; i < 7; i++)
        {
            var dow = (DayOfWeek)(((int)firstDow + i) % 7);
            var name = culture.DateTimeFormat.GetAbbreviatedDayName(dow);
            graphics.DrawString(name, Font, brush, (RectangleF)layout.HeaderCells[i], format);
        }
    }

    private void PaintGrid(Graphics graphics, DisplayLayout layout)
    {
        var palette = UsePortableCalendarAppearance
            ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
        using (var back = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(back, layout.Grid);
        }

        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var boldFont = new Font(Font, Font.Style | FontStyle.Bold);

        for (var i = 0; i < layout.Cells.Length; i++)
        {
            var date = layout.CellDates[i];
            var cell = layout.Cells[i];
            var inCurrentMonth = date.Month == _currentMonth.Month && date.Year == _currentMonth.Year;
            var selected = date >= _selectionStart && date <= _selectionEnd;

            if (selected)
            {
                if (Focused)
                {
                    using var highlight = new SolidBrush(palette?.Selection ?? SystemColors.Highlight);
                    graphics.FillRectangle(highlight, cell);
                }
                else
                {
                    using var outline = new Pen(palette?.Selection ?? SystemColors.Highlight);
                    graphics.DrawRectangle(outline, cell.Left, cell.Top, cell.Width - 1, cell.Height - 1);
                }
            }

            Color color;
            if (selected && Focused)
            {
                color = palette?.SelectedText ?? SystemColors.HighlightText;
            }
            else if (!inCurrentMonth)
            {
                color = palette is not null && !_trailingForeColorExplicit
                    ? palette.DisabledText : _trailingForeColor;
            }
            else if (!Enabled)
            {
                color = palette?.DisabledText ?? SystemColors.GrayText;
            }
            else
            {
                color = palette is not null && !HasExplicitAppearanceForeColor
                    ? palette.Text : ForeColor;
            }

            var font = IsBoldDate(date) ? boldFont : Font;

            using (var textBrush = new SolidBrush(color))
            {
                graphics.DrawString(date.Day.ToString(CultureInfo.CurrentCulture), font, textBrush, (RectangleF)cell, format);
            }

            if (_showTodayCircle && date == _todayDate.Date)
            {
                var circle = new Rectangle(cell.Left + 2, cell.Top + 1, cell.Width - 4, cell.Height - 3);

                // The classic calendar uses red for today; portable profiles
                // use the same accent as the selected date.
                if (palette is null)
                {
                    graphics.DrawEllipse(Pens.Red, circle);
                }
                else
                {
                    using var todayBorder = new Pen(palette.Accent);
                    graphics.DrawEllipse(todayBorder, circle);
                }
            }
        }
    }

    private void PaintFooter(Graphics graphics, DisplayLayout layout)
    {
        var palette = UsePortableCalendarAppearance
            ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
        using (var back = new SolidBrush(BackColor))
        {
            graphics.FillRectangle(back, layout.Footer);
        }

        var bulletSize = Math.Min(layout.Footer.Height - 6, 10);
        var bulletRect = new Rectangle(layout.Footer.Left + 4, layout.Footer.Top + (layout.Footer.Height - bulletSize) / 2, bulletSize, bulletSize);
        if (palette is null)
        {
            graphics.DrawEllipse(Pens.Red, bulletRect);
        }
        else
        {
            using var todayBorder = new Pen(palette.Accent);
            graphics.DrawEllipse(todayBorder, bulletRect);
        }

        var text = $"Today: {_todayDate.ToShortDateString()}";
        var textArea = Rectangle.FromLTRB(bulletRect.Right + 6, layout.Footer.Top, layout.Footer.Right, layout.Footer.Bottom);

        using var format = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };
        var textColor = !Enabled ? palette?.DisabledText ?? SystemColors.GrayText
            : palette is not null && !HasExplicitAppearanceForeColor ? palette.Text : ForeColor;
        using var brush = new SolidBrush(textColor);
        graphics.DrawString(text, Font, brush, (RectangleF)textArea, format);
    }

    public HitTestInfo HitTest(int x, int y) => HitTest(new Point(x, y));

    // Control has an unrelated internal HitTest(Point) (input routing, a
    // different return type) that only this name and parameter happen to
    // match; "new" hides it deliberately instead of silencing a warning.
    public new HitTestInfo HitTest(Point point)
    {
        if (!ClientRectangle.Contains(point)) return new HitTestInfo(HitArea.Nowhere, point, DateTime.MinValue);

        var layout = ComputeDisplayLayout();

        if (layout.PrevButton.Contains(point)) return new HitTestInfo(HitArea.PrevMonthButton, point, DateTime.MinValue);
        if (layout.NextButton.Contains(point)) return new HitTestInfo(HitArea.NextMonthButton, point, DateTime.MinValue);
        if (layout.TitleMonthText.Contains(point)) return new HitTestInfo(HitArea.TitleMonth, point, _currentMonth);
        if (layout.TitleYearText.Contains(point)) return new HitTestInfo(HitArea.TitleYear, point, _currentMonth);
        if (layout.TitleBar.Contains(point)) return new HitTestInfo(HitArea.TitleBackground, point, DateTime.MinValue);

        if (layout.Header.Contains(point)) return new HitTestInfo(HitArea.DayOfWeek, point, DateTime.MinValue);

        for (var i = 0; i < layout.Cells.Length; i++)
        {
            if (!layout.Cells[i].Contains(point)) continue;

            var date = layout.CellDates[i];
            var firstOfMonth = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);

            var area = date.Month == _currentMonth.Month && date.Year == _currentMonth.Year
                ? HitArea.Date
                : date < firstOfMonth ? HitArea.PrevMonthDate : HitArea.NextMonthDate;

            return new HitTestInfo(area, point, date);
        }

        if (!layout.Footer.IsEmpty && layout.Footer.Contains(point)) return new HitTestInfo(HitArea.TodayLink, point, _todayDate);

        return new HitTestInfo(HitArea.CalendarBackground, point, DateTime.MinValue);
    }
}
