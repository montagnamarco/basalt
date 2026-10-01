using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms.Internal;

namespace System.Windows.Forms;

/// <summary>
/// A single-line box that shows one <see cref="DateTime"/>, edited field by
/// field (year, month, day, hour...) or picked from a drop-down
/// <see cref="MonthCalendar"/>.
/// </summary>
/// <remarks>
/// DateTimePicker is native in WinForms (its SysDateTimePick32 window paints
/// and handles input itself): this class uses the <see cref="Control.Native"/>
/// hooks (<see cref="DefaultPaint"/>, <see cref="DefaultMouseDown"/>,
/// <see cref="DefaultKeyDown"/> and the rest) rather than
/// OnPaint/OnMouseDown/OnKeyDown, exactly as the members Microsoft overrides
/// for this control (and no others) show.
/// </remarks>
[DefaultProperty("Value")]
[DefaultEvent("ValueChanged")]
public partial class DateTimePicker : Control
{
    /// <summary>The oldest and newest date this control can ever show, regardless of MinDate/MaxDate: the same range MonthCalendar uses.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static readonly DateTime MinDateTime = new(1753, 1, 1);

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static readonly DateTime MaxDateTime = new(9998, 12, 31);

    protected static readonly Color DefaultMonthBackColor = SystemColors.Window;
    protected static readonly Color DefaultTitleBackColor = SystemColors.ActiveCaption;
    protected static readonly Color DefaultTitleForeColor = SystemColors.ActiveCaptionText;
    protected static readonly Color DefaultTrailingForeColor = SystemColors.GrayText;

    internal const int ButtonWidth = 17;
    internal const int CheckBoxAreaWidth = 20;
    internal const int FieldInset = 2;

    private DateTime _value = DateTime.Now;
    private DateTime _minDate = MinDateTime;
    private DateTime _maxDate = MaxDateTime;
    private DateTimePickerFormat _format = DateTimePickerFormat.Long;
    private string? _customFormat;
    private bool _showCheckBox;
    private bool _checked = true;
    private bool _showUpDown;
    private LeftRightAlignment _dropDownAlign = LeftRightAlignment.Left;

    private Color? _calendarForeColor;
    private Color? _calendarMonthBackground;
    private Color? _calendarTitleBackColor;
    private Color? _calendarTitleForeColor;
    private Color? _calendarTrailingForeColor;
    private Font? _calendarFont;

    public DateTimePicker()
    {
        // DateTimePicker is native in WinForms: it paints and handles its
        // mouse and keys through Control's default-procedure hooks, not
        // OnPaint/OnMouseDown/OnKeyDown.
        SetStyle(ControlStyles.UserPaint | ControlStyles.StandardClick | ControlStyles.StandardDoubleClick, false);
        SetStyle(ControlStyles.FixedHeight, true);
        SetStyle(ControlStyles.Selectable, true);

        Size = DefaultSize;
    }

    protected override Size DefaultSize => new(200, PreferredHeight);

    /// <summary>The height one line takes with the sunken edge, exactly as TextBox computes it: DateTimePicker's edge cannot be turned off.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PreferredHeight => FontHeight + NonClientArea.BorderWidth * 4 + 3;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WindowStyles.ExClientEdge;

            return parameters;
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color BackColor
    {
        get => base.BackColor;
        set => base.BackColor = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Image? BackgroundImage
    {
        get => base.BackgroundImage;
        set => base.BackgroundImage = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override ImageLayout BackgroundImageLayout
    {
        get => base.BackgroundImageLayout;
        set => base.BackgroundImageLayout = value;
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public override Color ForeColor
    {
        get => base.ForeColor;
        set => base.ForeColor = value;
    }

    /// <summary>Which side of the control the drop-down calendar's edge aligns with.</summary>
    [DefaultValue(LeftRightAlignment.Left)]
    public LeftRightAlignment DropDownAlign
    {
        get => _dropDownAlign;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(LeftRightAlignment));

            _dropDownAlign = value;
        }
    }

    /// <summary>The formatted value, exactly what is painted; setting a parseable date within bounds applies it as Value does.</summary>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => FormattedText;
        set
        {
            if (value is null || !DateTime.TryParse(value, out var parsed)) return;
            if (parsed < _minDate || parsed > _maxDate) return;

            SetValueCore(parsed);
        }
    }

    public static DateTime MaximumDateTime => MaxDateTime;

    public static DateTime MinimumDateTime => MinDateTime;

    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            if (value == _minDate) return;

            if (value < MinDateTime || value > MaxDateTime)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"'{value}' is not a valid value for 'MinDate'. 'MinDate' must be between {MinDateTime:d} and {MaxDateTime:d}.");
            }

            if (value > _maxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "'MinDate' must be less than or equal to 'MaxDate'.");
            }

            _minDate = value;
            ClampValueToBounds();
            Invalidate();
        }
    }

    public DateTime MaxDate
    {
        get => _maxDate;
        set
        {
            if (value == _maxDate) return;

            if (value < MinDateTime || value > MaxDateTime)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    $"'{value}' is not a valid value for 'MaxDate'. 'MaxDate' must be between {MinDateTime:d} and {MaxDateTime:d}.");
            }

            if (value < _minDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "'MaxDate' must be greater than or equal to 'MinDate'.");
            }

            _maxDate = value;
            ClampValueToBounds();
            Invalidate();
        }
    }

    /// <summary>The date and time shown; must lie between MinDate and MaxDate.</summary>
    [Bindable(true)]
    [RefreshProperties(RefreshProperties.All)]
    public DateTime Value
    {
        get => _value;
        set
        {
            if (value < _minDate || value > _maxDate)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Value must be between MinDate and MaxDate.");
            }

            SetValueCore(value);
        }
    }

    private void SetValueCore(DateTime newValue)
    {
        if (newValue == _value) return;

        _value = newValue;
        OnValueChanged(EventArgs.Empty);
        OnTextChanged(EventArgs.Empty);
        Invalidate();
    }

    /// <summary>Moves the current value back inside [MinDate, MaxDate] after a bound moved; no exception, as the calendar adjusts itself.</summary>
    private void ClampValueToBounds()
    {
        var clamped = Clamp(_value);
        SetValueCore(clamped);
    }

    private DateTime Clamp(DateTime value)
    {
        if (value < _minDate) return _minDate;
        if (value > _maxDate) return _maxDate;

        return value;
    }

    /// <summary>Applies a candidate from field editing or spinning: clamped silently, unlike the public setter.</summary>
    private void SetValueClamped(DateTime candidate) => SetValueCore(Clamp(candidate));

    [RefreshProperties(RefreshProperties.Repaint)]
    public DateTimePickerFormat Format
    {
        get => _format;
        set
        {
            if (!Enum.IsDefined(value)) throw new InvalidEnumArgumentException(nameof(value), (int)value, typeof(DateTimePickerFormat));
            if (value == _format) return;

            _format = value;
            ResetFieldSelection();
            OnFormatChanged(EventArgs.Empty);
            Invalidate();
        }
    }

    [DefaultValue(null)]
    [RefreshProperties(RefreshProperties.Repaint)]
    public string? CustomFormat
    {
        get => _customFormat;
        set
        {
            if (value == _customFormat) return;

            _customFormat = value;
            if (_format != DateTimePickerFormat.Custom) return;

            ResetFieldSelection();
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool ShowCheckBox
    {
        get => _showCheckBox;
        set
        {
            if (value == _showCheckBox) return;

            _showCheckBox = value;
            Invalidate();
        }
    }

    [DefaultValue(true)]
    [Bindable(true)]
    public bool Checked
    {
        get => _checked;
        set
        {
            if (value == _checked) return;

            _checked = value;
            Invalidate();
        }
    }

    [DefaultValue(false)]
    public bool ShowUpDown
    {
        get => _showUpDown;
        set
        {
            if (value == _showUpDown) return;

            _showUpDown = value;

            // The calendar drop-down and the spin buttons are mutually exclusive, as Windows draws them.
            if (value) CloseDropDown(commit: false);

            Invalidate();
        }
    }

    public Font CalendarFont
    {
        get => _calendarFont ?? Font;
        set
        {
            _calendarFont = value;
            ApplyCalendarAppearance();
        }
    }

    public Color CalendarForeColor
    {
        get => _calendarForeColor ?? SystemColors.WindowText;
        set
        {
            _calendarForeColor = value;
            ApplyCalendarAppearance();
        }
    }

    public Color CalendarMonthBackground
    {
        get => _calendarMonthBackground ?? DefaultMonthBackColor;
        set
        {
            _calendarMonthBackground = value;
            ApplyCalendarAppearance();
        }
    }

    public Color CalendarTitleBackColor
    {
        get => _calendarTitleBackColor ?? DefaultTitleBackColor;
        set
        {
            _calendarTitleBackColor = value;
            ApplyCalendarAppearance();
        }
    }

    public Color CalendarTitleForeColor
    {
        get => _calendarTitleForeColor ?? DefaultTitleForeColor;
        set
        {
            _calendarTitleForeColor = value;
            ApplyCalendarAppearance();
        }
    }

    public Color CalendarTrailingForeColor
    {
        get => _calendarTrailingForeColor ?? DefaultTrailingForeColor;
        set
        {
            _calendarTrailingForeColor = value;
            ApplyCalendarAppearance();
        }
    }

    public event EventHandler? ValueChanged;

    protected virtual void OnValueChanged(EventArgs eventargs) => ValueChanged?.Invoke(this, eventargs);

    public event EventHandler? FormatChanged;

    protected virtual void OnFormatChanged(EventArgs e) => FormatChanged?.Invoke(this, e);

    public event EventHandler? DropDown;

    protected virtual void OnDropDown(EventArgs eventargs) => DropDown?.Invoke(this, eventargs);

    public event EventHandler? CloseUp;

    protected virtual void OnCloseUp(EventArgs eventargs) => CloseUp?.Invoke(this, eventargs);

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = PreferredHeight;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    internal override void DefaultLostFocus()
    {
        // Leaving by keyboard closes the calendar as a click elsewhere does.
        CloseDropDown(commit: false);
        ResetTyping();
        Invalidate();
    }

    public override string ToString() => $"{base.ToString()}, Value: {Value}";
}
