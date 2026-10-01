using System.Drawing;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>
/// Breaks the active format pattern into the pieces a user edits field by
/// field (year, month, day, hour, minute, second, the AM/PM designator) and
/// the pieces that are only shown (literal punctuation, a day-of-week name):
/// what <see cref="DateTimePicker.Format"/> and <see cref="DateTimePicker.CustomFormat"/>
/// turn into on screen, and what Left/Right, Up/Down and typed digits edit.
/// </summary>
public partial class DateTimePicker
{
    private enum FieldKind
    {
        Literal,
        Year,
        Month,
        Day,
        DayOfWeek,
        Hour,
        Minute,
        Second,
        AmPm,
    }

    private readonly record struct FieldToken(FieldKind Kind, string Run);

    private readonly record struct RenderedField(FieldKind Kind, string Text, int Start);

    private bool IsEditable(FieldKind kind) => kind is not (FieldKind.Literal or FieldKind.DayOfWeek);

    /// <summary>Which editable field Left/Right/Up/Down act on; an index into <see cref="EditableTokens"/>, not into every token.</summary>
    private int _selectedEditableIndex;

    /// <summary>The digits typed so far for the field being entered, and which field they belong to; reset when the selection moves.</summary>
    private string _typedDigits = string.Empty;
    private int _typingFieldIndex = -1;
    private bool _yearPending;

    /// <summary>The pattern in effect: the culture's pattern for Long/Short/Time, or CustomFormat (falling back to Short's pattern when it is empty).</summary>
    private string EffectivePattern => _format switch
    {
        DateTimePickerFormat.Long => DateTimeFormatInfo.CurrentInfo.LongDatePattern,
        DateTimePickerFormat.Short => DateTimeFormatInfo.CurrentInfo.ShortDatePattern,
        DateTimePickerFormat.Time => DateTimeFormatInfo.CurrentInfo.LongTimePattern,
        DateTimePickerFormat.Custom => string.IsNullOrEmpty(_customFormat) ? DateTimeFormatInfo.CurrentInfo.ShortDatePattern : _customFormat,
        _ => DateTimeFormatInfo.CurrentInfo.ShortDatePattern,
    };

    private static bool IsFormatLetter(char c) => c is 'y' or 'M' or 'd' or 'H' or 'h' or 'm' or 's' or 't';

    private static FieldKind KindOf(char letter, int length) => letter switch
    {
        'y' => FieldKind.Year,
        'M' => FieldKind.Month,
        'd' => length <= 2 ? FieldKind.Day : FieldKind.DayOfWeek,
        'H' or 'h' => FieldKind.Hour,
        'm' => FieldKind.Minute,
        's' => FieldKind.Second,
        't' => FieldKind.AmPm,
        _ => FieldKind.Literal,
    };

    /// <summary>Splits the pattern into runs of one repeated format letter, or literal text between them.</summary>
    private static List<FieldToken> Tokenize(string pattern)
    {
        var tokens = new List<FieldToken>();
        var i = 0;

        while (i < pattern.Length)
        {
            var c = pattern[i];

            if (IsFormatLetter(c))
            {
                var j = i;
                while (j < pattern.Length && pattern[j] == c) j++;

                tokens.Add(new FieldToken(KindOf(c, j - i), pattern[i..j]));
                i = j;
            }
            else
            {
                var j = i;
                while (j < pattern.Length && !IsFormatLetter(pattern[j])) j++;

                tokens.Add(new FieldToken(FieldKind.Literal, pattern[i..j]));
                i = j;
            }
        }

        return tokens;
    }

    private List<FieldToken> Tokens => Tokenize(EffectivePattern);

    /// <summary>The tokens a user can move to and edit, in on-screen order.</summary>
    private List<FieldToken> EditableTokens() => Tokens.Where(t => IsEditable(t.Kind)).ToList();

    /// <summary>Every token rendered against <see cref="Value"/>, with the character offset each one starts at.</summary>
    private List<RenderedField> RenderFields()
    {
        var fields = new List<RenderedField>();
        var offset = 0;

        var editableIndex = -1;

        foreach (var token in Tokens)
        {
            if (IsEditable(token.Kind)) editableIndex++;

            var showsTypedYear = _yearPending && token.Kind == FieldKind.Year && editableIndex == _typingFieldIndex;
            var text = token.Kind == FieldKind.Literal
                ? token.Run
                : showsTypedYear ? _typedDigits : _value.ToString(token.Run, CultureInfo.CurrentCulture);
            fields.Add(new RenderedField(token.Kind, text, offset));
            offset += text.Length;
        }

        return fields;
    }

    /// <summary>The text shown in the box: every token's rendering, concatenated.</summary>
    private string FormattedText => string.Concat(RenderFields().Select(f => f.Text));

    /// <summary>The rendered field the selected editable index refers to, or null while there is nothing editable.</summary>
    private RenderedField? SelectedRenderedField()
    {
        var editableKinds = EditableTokens();
        if (editableKinds.Count == 0) return null;

        var index = Math.Clamp(_selectedEditableIndex, 0, editableKinds.Count - 1);
        var targetKind = editableKinds[index].Kind;

        // The Nth editable rendered field with that kind: tokens never change kind order, so counting is unambiguous.
        var seen = -1;
        foreach (var field in RenderFields())
        {
            if (!IsEditable(field.Kind)) continue;

            seen++;
            if (seen == index) return field;
        }

        return null;
    }

    private void ResetFieldSelection()
    {
        _selectedEditableIndex = 0;
        DiscardTyping();
    }

    /// <summary>Ends typing on the current field because the user moved on: a year still being typed is applied.</summary>
    private void ResetTyping()
    {
        var pendingYear = _yearPending ? _typedDigits : null;

        DiscardTyping();

        if (pendingYear is not null) ApplyTypedYear(pendingYear);
    }

    /// <summary>Forgets the digits typed so far without applying them: the fields themselves changed (a new format) or are being stepped.</summary>
    private void DiscardTyping()
    {
        _typedDigits = string.Empty;
        _typingFieldIndex = -1;
        _yearPending = false;
    }

    private void ApplyTypedYear(string digits)
    {
        var year = Math.Max(int.Parse(digits), 1);
        SetValueClamped(ApplyFieldValue(_value, FieldKind.Year, year));
    }

    private void MoveFieldSelection(int direction)
    {
        var count = EditableTokens().Count;
        if (count == 0) return;

        _selectedEditableIndex = ((_selectedEditableIndex + direction) % count + count) % count;
        ResetTyping();
        Invalidate();
    }

    /// <summary>Up/Down on the selected field: steps it within its own range, wrapping there instead of cascading into the next field.</summary>
    private void AdjustSelectedField(int delta)
    {
        if (_showCheckBox && !_checked) return;

        var editable = EditableTokens();
        if (editable.Count == 0) return;

        // Up/Down steps the value shown before the typing began: one press, one change.
        DiscardTyping();

        var index = Math.Clamp(_selectedEditableIndex, 0, editable.Count - 1);
        var next = AdjustField(_value, editable[index].Kind, delta);

        SetValueClamped(next);
    }

    private static DateTime AdjustField(DateTime value, FieldKind kind, int delta)
    {
        switch (kind)
        {
            case FieldKind.Year:
            {
                var year = Math.Clamp(value.Year + delta, 1, 9999);
                var day = Math.Min(value.Day, DateTime.DaysInMonth(year, value.Month));
                return new DateTime(year, value.Month, day, value.Hour, value.Minute, value.Second);
            }

            case FieldKind.Month:
            {
                var month = (((value.Month - 1 + delta) % 12) + 12) % 12 + 1;
                var day = Math.Min(value.Day, DateTime.DaysInMonth(value.Year, month));
                return new DateTime(value.Year, month, day, value.Hour, value.Minute, value.Second);
            }

            case FieldKind.Day:
            {
                var daysInMonth = DateTime.DaysInMonth(value.Year, value.Month);
                var day = (((value.Day - 1 + delta) % daysInMonth) + daysInMonth) % daysInMonth + 1;
                return new DateTime(value.Year, value.Month, day, value.Hour, value.Minute, value.Second);
            }

            case FieldKind.Hour:
            {
                var hour = (((value.Hour + delta) % 24) + 24) % 24;
                return new DateTime(value.Year, value.Month, value.Day, hour, value.Minute, value.Second);
            }

            case FieldKind.Minute:
            {
                var minute = (((value.Minute + delta) % 60) + 60) % 60;
                return new DateTime(value.Year, value.Month, value.Day, value.Hour, minute, value.Second);
            }

            case FieldKind.Second:
            {
                var second = (((value.Second + delta) % 60) + 60) % 60;
                return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, second);
            }

            case FieldKind.AmPm:
                return value.AddHours(value.Hour < 12 ? 12 : -12);

            default:
                return value;
        }
    }

    /// <summary>The largest value a typed digit buffer may reach for the field's kind, before it cannot possibly extend further.</summary>
    private static int MaxDigitValue(FieldKind kind, DateTime value) => kind switch
    {
        FieldKind.Year => 9999,
        FieldKind.Month => 12,
        FieldKind.Day => DateTime.DaysInMonth(value.Year, value.Month),
        FieldKind.Hour => 23,
        FieldKind.Minute => 59,
        FieldKind.Second => 59,
        _ => 0,
    };

    private static int MaxDigitCount(FieldKind kind) => kind == FieldKind.Year ? 4 : 2;

    /// <summary>A digit typed on the selected numeric field: builds up the field's new value, auto-advancing once no further digit could extend it.</summary>
    private void TypeDigit(int digit)
    {
        if (_showCheckBox && !_checked) return;

        var editable = EditableTokens();
        if (editable.Count == 0) return;

        var index = Math.Clamp(_selectedEditableIndex, 0, editable.Count - 1);
        var kind = editable[index].Kind;
        if (kind is not (FieldKind.Year or FieldKind.Month or FieldKind.Day or FieldKind.Hour or FieldKind.Minute or FieldKind.Second)) return;

        if (_typingFieldIndex != index) ResetTyping();
        _typingFieldIndex = index;

        var maxValue = MaxDigitValue(kind, _value);
        var maxDigits = MaxDigitCount(kind);

        var candidate = _typedDigits + digit;
        if (candidate.Length > maxDigits || int.Parse(candidate) > maxValue) candidate = digit.ToString();

        _typedDigits = candidate;

        // A year is held until all four digits are in (or the field is left): applied digit by
        // digit it would pass through years 1, 19, 199 on the way to 1999, each outside any
        // sensible MinDate and year 0 not a date at all. The field shows the digits meanwhile.
        if (kind == FieldKind.Year && candidate.Length < maxDigits)
        {
            _yearPending = true;
            Invalidate();
            return;
        }

        if (kind == FieldKind.Year)
        {
            _yearPending = false;
            ApplyTypedYear(candidate);
        }
        else
        {
            var parsed = Math.Clamp(int.Parse(candidate), kind is FieldKind.Day or FieldKind.Month ? 1 : 0, maxValue);
            SetValueClamped(ApplyFieldValue(_value, kind, parsed));
        }

        // No further digit could extend the buffer without overflowing: the field is "full", so the next key starts the next one.
        if (candidate.Length == maxDigits || int.Parse(candidate) * 10 > maxValue) MoveFieldSelection(1);
    }

    /// <summary>Sets the selected field to an absolute value (typed digits), unlike <see cref="AdjustField"/>'s relative step.</summary>
    private static DateTime ApplyFieldValue(DateTime value, FieldKind kind, int newValue)
    {
        switch (kind)
        {
            case FieldKind.Year:
            {
                var day = Math.Min(value.Day, DateTime.DaysInMonth(newValue, value.Month));
                return new DateTime(newValue, value.Month, day, value.Hour, value.Minute, value.Second);
            }

            case FieldKind.Month:
            {
                var day = Math.Min(value.Day, DateTime.DaysInMonth(value.Year, newValue));
                return new DateTime(value.Year, newValue, day, value.Hour, value.Minute, value.Second);
            }

            case FieldKind.Day:
                return new DateTime(value.Year, value.Month, newValue, value.Hour, value.Minute, value.Second);

            case FieldKind.Hour:
                return new DateTime(value.Year, value.Month, value.Day, newValue, value.Minute, value.Second);

            case FieldKind.Minute:
                return new DateTime(value.Year, value.Month, value.Day, value.Hour, newValue, value.Second);

            case FieldKind.Second:
                return new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, newValue);

            default:
                return value;
        }
    }

    /// <summary>'a'/'p' set the AM/PM designator directly, as typing a letter does on the real control.</summary>
    private void TypeAmPmLetter(char letter)
    {
        if (_showCheckBox && !_checked) return;

        var editable = EditableTokens();
        var index = Math.Clamp(_selectedEditableIndex, 0, editable.Count - 1);
        if (index >= editable.Count || editable[index].Kind != FieldKind.AmPm) return;

        var wantsPm = char.ToLowerInvariant(letter) == 'p';
        var isPm = _value.Hour >= 12;
        if (wantsPm == isPm) return;

        SetValueClamped(_value.AddHours(wantsPm ? 12 : -12));
    }

    // --- Layout: where the checkbox, the fields and the drop-down/spin button are drawn and hit-tested ---

    private Rectangle CheckBoxBounds()
    {
        if (!_showCheckBox) return Rectangle.Empty;

        var client = ClientRectangle;
        return new Rectangle(FieldInset, (client.Height - 13) / 2, 13, client.Height - 2 * FieldInset);
    }

    private Rectangle ButtonBounds()
    {
        var client = ClientRectangle;
        return Rectangle.FromLTRB(client.Right - ButtonWidth, client.Top, client.Right, client.Bottom);
    }

    private Rectangle UpButtonBounds()
    {
        var button = ButtonBounds();
        return new Rectangle(button.X, button.Y, button.Width, button.Height / 2);
    }

    private Rectangle DownButtonBounds()
    {
        var button = ButtonBounds();
        var upHeight = button.Height / 2;
        return new Rectangle(button.X, button.Y + upHeight, button.Width, button.Height - upHeight);
    }

    private Rectangle TextAreaBounds()
    {
        var client = ClientRectangle;
        var left = client.Left + FieldInset + (_showCheckBox ? CheckBoxAreaWidth : 0);
        var right = client.Right - ButtonWidth;

        return Rectangle.FromLTRB(Math.Min(left, right), client.Top, right, client.Bottom);
    }

    /// <summary>The rectangle a rendered field occupies on screen, measured from the left of the text area.</summary>
    private Rectangle FieldScreenBounds(RenderedField field)
    {
        var area = TextAreaBounds();
        var text = FormattedText;

        var before = TextRenderer.MeasureText(text[..field.Start], Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
        var width = TextRenderer.MeasureText(field.Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;

        return new Rectangle(area.Left + before, area.Top, width, area.Height);
    }

    /// <summary>Which editable field (by its index among them) a point in client coordinates falls on, if any.</summary>
    private int? HitTestField(Point location)
    {
        var editableSeen = -1;

        foreach (var field in RenderFields())
        {
            if (!IsEditable(field.Kind)) continue;

            editableSeen++;
            if (FieldScreenBounds(field).Contains(location)) return editableSeen;
        }

        return null;
    }
}
