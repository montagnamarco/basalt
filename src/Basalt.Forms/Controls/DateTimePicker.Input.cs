namespace System.Windows.Forms;

/// <summary>
/// Mouse and keyboard, through the native-control hooks in
/// <see cref="Control.Native"/>: a click focuses a field or presses the
/// check box, the button or the spinner; Left/Right move between fields,
/// Up/Down and typed digits edit the focused one, and Alt+Down, F4 or the
/// button open the drop-down calendar.
/// </summary>
public partial class DateTimePicker
{
    private bool _upPressed;
    private bool _downPressed;
    private bool _buttonPressed;

    /// <summary>Whether the keys move between fields and spin them, which this control wants and a dialog does not get.</summary>
    protected override bool IsInputKey(Keys keyData)
    {
        if ((keyData & Keys.Alt) == Keys.Alt) return false;

        switch (keyData & Keys.KeyCode)
        {
            case Keys.Left or Keys.Right or Keys.Up or Keys.Down:
                return true;
        }

        return base.IsInputKey(keyData);
    }

    internal override bool DialogCodeWantsKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.DialogCodeWantsKey(keyData);

    internal override bool DialogCodeWantsChar(char charCode) => char.IsDigit(charCode) || base.DialogCodeWantsChar(charCode);

    internal override void DefaultMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        if (CanSelect) Focus();

        if (_showCheckBox && CheckBoxBounds().Contains(e.Location))
        {
            Checked = !Checked;

            // The native control reports a user's tick or untick as a change of value (DTN_DATETIMECHANGE); setting Checked from code does not.
            OnValueChanged(EventArgs.Empty);
            return;
        }

        if (_showUpDown)
        {
            if (UpButtonBounds().Contains(e.Location))
            {
                _upPressed = true;
                Invalidate();
                AdjustSelectedField(1);
                return;
            }

            if (DownButtonBounds().Contains(e.Location))
            {
                _downPressed = true;
                Invalidate();
                AdjustSelectedField(-1);
                return;
            }
        }
        else if (ButtonBounds().Contains(e.Location))
        {
            _buttonPressed = true;
            Invalidate();
            ToggleDropDown();
            return;
        }

        if (HitTestField(e.Location) is { } index)
        {
            _selectedEditableIndex = index;
            ResetTyping();
            Invalidate();
        }
    }

    internal override void DefaultMouseUp(MouseEventArgs e)
    {
        if (_upPressed || _downPressed || _buttonPressed)
        {
            _upPressed = false;
            _downPressed = false;
            _buttonPressed = false;
            Invalidate();
        }
    }

    internal override void DefaultCaptureLost()
    {
        if (!_upPressed && !_downPressed && !_buttonPressed) return;

        _upPressed = false;
        _downPressed = false;
        _buttonPressed = false;
        Invalidate();
    }

    internal override void DefaultKeyDown(KeyEventArgs e)
    {
        if (e.Alt && e.KeyCode == Keys.Down)
        {
            OpenDropDown();
            e.Handled = true;
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.F4:
                ToggleDropDown();
                e.Handled = true;
                break;

            case Keys.Left:
                MoveFieldSelection(-1);
                e.Handled = true;
                break;

            case Keys.Right:
                MoveFieldSelection(1);
                e.Handled = true;
                break;

            case Keys.Up:
                AdjustSelectedField(1);
                e.Handled = true;
                break;

            case Keys.Down:
                AdjustSelectedField(-1);
                e.Handled = true;
                break;
        }
    }

    internal override void DefaultKeyPress(KeyPressEventArgs e)
    {
        if (char.IsDigit(e.KeyChar))
        {
            TypeDigit(e.KeyChar - '0');
            e.Handled = true;
            return;
        }

        if (char.ToLowerInvariant(e.KeyChar) is 'a' or 'p')
        {
            TypeAmPmLetter(e.KeyChar);
            e.Handled = true;
        }
    }
}
