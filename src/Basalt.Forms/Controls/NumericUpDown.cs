using System.ComponentModel;
using System.Globalization;

namespace System.Windows.Forms;

/// <summary>A box the user edits as text, but whose content is a decimal <see cref="Value"/> between <see cref="Minimum"/> and <see cref="Maximum"/>.</summary>
[DefaultProperty("Value")]
[DefaultEvent("ValueChanged")]
public class NumericUpDown : UpDownBase, ISupportInitialize
{
    private readonly NumericUpDownAccelerationCollection _accelerations = new();

    private decimal _value;
    private decimal _minimum;
    private decimal _maximum = 100m;
    private decimal _increment = 1m;
    private int _decimalPlaces;
    private bool _hexadecimal;
    private bool _thousandsSeparator;

    private bool _initializing;

    private DateTime? _spinStart;
    private DateTime? _lastSpin;

    /// <summary>A spin that follows the previous one within this gap counts as the same held key or button, for acceleration.</summary>
    private static readonly TimeSpan SpinContinuationGap = TimeSpan.FromMilliseconds(300);

    public NumericUpDown()
    {
        UpdateEditText();
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public NumericUpDownAccelerationCollection Accelerations => _accelerations;

    [DefaultValue(0)]
    public int DecimalPlaces
    {
        get => _decimalPlaces;
        set
        {
            if (value < 0 || value > 99) throw new ArgumentOutOfRangeException(nameof(value), value, "DecimalPlaces must be between 0 and 99.");
            if (value == _decimalPlaces) return;

            _decimalPlaces = value;
            UpdateEditText();
        }
    }

    [DefaultValue(false)]
    public bool Hexadecimal
    {
        get => _hexadecimal;
        set
        {
            if (value == _hexadecimal) return;

            _hexadecimal = value;
            UpdateEditText();
        }
    }

    public decimal Increment
    {
        get => _increment;
        set => _increment = value;
    }

    [RefreshProperties(RefreshProperties.All)]
    public decimal Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value) return;

            _maximum = value;
            if (_initializing) return;

            if (_minimum > _maximum) Minimum = _maximum;
            if (_value > _maximum) Value = _maximum;
        }
    }

    [RefreshProperties(RefreshProperties.All)]
    public decimal Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value) return;

            _minimum = value;
            if (_initializing) return;

            if (_maximum < _minimum) Maximum = _minimum;
            if (_value < _minimum) Value = _minimum;
        }
    }

    [DefaultValue(false)]
    [Localizable(true)]
    public bool ThousandsSeparator
    {
        get => _thousandsSeparator;
        set
        {
            if (value == _thousandsSeparator) return;

            _thousandsSeparator = value;
            UpdateEditText();
        }
    }

    [Bindable(true)]
    public decimal Value
    {
        get => _value;
        set
        {
            if (!_initializing && (value < _minimum || value > _maximum))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, $"'{value}' is not a valid value for 'Value'. 'Value' should be between 'Minimum' and 'Maximum'.");
            }

            SetValueCore(value);
            UpdateEditText();
        }
    }

    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Bindable(false)]
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    public event EventHandler? ValueChanged;

    protected virtual void OnValueChanged(EventArgs e) => ValueChanged?.Invoke(this, e);

    /// <summary>Defers Minimum/Maximum/Value cross-checks until <see cref="EndInit"/>: a designer sets them in any order.</summary>
    public void BeginInit() => _initializing = true;

    public void EndInit()
    {
        _initializing = false;

        if (_minimum > _maximum) _maximum = _minimum;
        SetValueCore(Math.Clamp(_value, _minimum, _maximum));
        UpdateEditText();
    }

    public override void UpButton()
    {
        if (UserEdit) ParseEditText();

        var increment = CurrentIncrement();
        var target = _value + increment > _maximum ? _maximum : _value + increment;

        SetValueCore(target);
        UpdateEditText();
    }

    public override void DownButton()
    {
        if (UserEdit) ParseEditText();

        var increment = CurrentIncrement();
        var target = _value - increment < _minimum ? _minimum : _value - increment;

        SetValueCore(target);
        UpdateEditText();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Up or Keys.Down) _spinStart ??= DateTime.UtcNow;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            _spinStart = null;
            _lastSpin = null;
        }
    }

    protected override void OnLostFocus(EventArgs e) => base.OnLostFocus(e);

    protected override void OnTextBoxKeyPress(object? source, KeyPressEventArgs e)
    {
        base.OnTextBoxKeyPress(source, e);
        if (e.Handled) return;

        if (e.KeyChar == '\r')
        {
            ParseEditText();
            UpdateEditText();
            e.Handled = true;
            return;
        }

        if (!IsValidCharacter(e.KeyChar)) e.Handled = true;
    }

    private bool IsValidCharacter(char character)
    {
        if (char.IsControl(character)) return true;

        if (_hexadecimal) return Uri.IsHexDigit(character);

        if (char.IsDigit(character)) return true;

        var format = CultureInfo.CurrentCulture.NumberFormat;
        if (character.ToString() == format.NegativeSign && _minimum < 0) return true;

        return _decimalPlaces > 0 && character.ToString() == format.NumberDecimalSeparator;
    }

    /// <summary>Reads the edit's text into <see cref="Value"/>, clamped to range; invalid text leaves the value unchanged.</summary>
    protected void ParseEditText()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(Text))
            {
                var parsed = _hexadecimal
                    ? Convert.ToInt64(Text.Trim(), 16)
                    : decimal.Parse(Text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.CurrentCulture);

                SetValueCore(Math.Clamp(parsed, _minimum, _maximum));
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException)
        {
            // The text does not parse: Value keeps its old number, and the caller redisplays it.
        }
        finally
        {
            UserEdit = false;
        }
    }

    protected override void UpdateEditText()
    {
        ChangingText = true;
        Text = FormatValue(_value);

        // TextChanged (and so the flag's own reset) only fires when the formatted text actually
        // differs from what the edit already shows; a value that rounds back to the same text
        // must not leave ChangingText set, or the next real keystroke would be ignored.
        ChangingText = false;
    }

    protected override void ValidateEditText()
    {
        ParseEditText();
        UpdateEditText();
    }

    private void SetValueCore(decimal value)
    {
        if (_value == value) return;

        _value = value;
        OnValueChanged(EventArgs.Empty);
    }

    private string FormatValue(decimal value)
    {
        if (_hexadecimal)
        {
            // Shown as a 64-bit number, as Windows does; beyond that range the value is clamped, not thrown.
            var integral = (long)Math.Clamp(Math.Truncate(value), long.MinValue, long.MaxValue);

            return Convert.ToString(integral, 16).ToUpperInvariant();
        }

        var format = (_thousandsSeparator ? "N" : "F") + _decimalPlaces.ToString(CultureInfo.InvariantCulture);

        return value.ToString(format, CultureInfo.CurrentCulture);
    }

    /// <summary>The increment a held spin has reached: a gap since the last spin starts the clock over.</summary>
    private decimal CurrentIncrement()
    {
        var now = DateTime.UtcNow;

        if (_lastSpin is null || now - _lastSpin.Value > SpinContinuationGap) _spinStart = now;
        _lastSpin = now;

        if (_accelerations.Count == 0) return _increment;

        var elapsedSeconds = (now - _spinStart!.Value).TotalSeconds;
        var effective = _increment;

        foreach (var acceleration in _accelerations)
        {
            if (elapsedSeconds >= acceleration.Seconds) effective = acceleration.Increment;
        }

        return effective;
    }

    public override string ToString() => $"{base.ToString()}, Minimum: {Minimum}, Maximum: {Maximum}, Value: {Value}";
}
