using System.Drawing;

namespace System.Windows.Forms;

/// <summary>
/// The native control's own painting: the Window background, an optional
/// check box, the formatted fields with the focused one highlighted the way
/// a selection is, and a drop-down button or a pair of spin buttons on the
/// right. The sunken edge around all of it comes from <see cref="Control.CreateParams"/>
/// (WS_EX_CLIENTEDGE), painted by the skin that draws every control's frame.
/// </summary>
public partial class DateTimePicker
{
    private Color EffectiveBackColor => Enabled ? (ShouldSerializeBackColor() ? BackColor : SystemColors.Window) : SystemColors.Control;

    private Color EffectiveForeColor
    {
        get
        {
            if (!Enabled) return SystemColors.GrayText;
            if (_showCheckBox && !_checked) return SystemColors.GrayText;

            return ShouldSerializeForeColor() ? ForeColor : SystemColors.WindowText;
        }
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        using (var background = new SolidBrush(EffectiveBackColor))
        {
            graphics.FillRectangle(background, client);
        }

        if (_showCheckBox) PaintCheckBox(graphics);

        PaintFields(graphics);

        if (_showUpDown) PaintSpinButtons(graphics);
        else PaintDropDownButton(graphics);
    }

    private void PaintCheckBox(Graphics graphics)
    {
        var state = ButtonState.Normal;
        if (_checked) state |= ButtonState.Checked;
        if (!Enabled) state |= ButtonState.Inactive;

        ControlPaint.DrawCheckBox(graphics, CheckBoxBounds(), state);
    }

    private void PaintFields(Graphics graphics)
    {
        var editableCount = EditableTokens().Count;
        var selectedIndex = editableCount == 0 ? -1 : Math.Clamp(_selectedEditableIndex, 0, editableCount - 1);
        var focused = Focused && (!_showCheckBox || _checked);

        var editableSeen = -1;
        foreach (var field in RenderFields())
        {
            var bounds = FieldScreenBounds(field);
            var isEditable = IsEditable(field.Kind);
            if (isEditable) editableSeen++;

            var isSelected = focused && isEditable && editableSeen == selectedIndex;

            if (isSelected)
            {
                using var highlight = new SolidBrush(SystemColors.Highlight);
                graphics.FillRectangle(highlight, bounds);
                TextRenderer.DrawText(graphics, field.Text, Font, bounds, SystemColors.HighlightText, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            }
            else
            {
                TextRenderer.DrawText(graphics, field.Text, Font, bounds, EffectiveForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
            }
        }
    }

    private void PaintDropDownButton(Graphics graphics)
    {
        var state = ButtonState.Normal;
        if (!Enabled) state = ButtonState.Inactive;
        else if (_buttonPressed || _dropped) state = ButtonState.Pushed;

        ControlPaint.DrawComboButton(graphics, ButtonBounds(), state);
    }

    private void PaintSpinButtons(Graphics graphics)
    {
        var upState = Enabled ? (_upPressed ? ButtonState.Pushed : ButtonState.Normal) : ButtonState.Inactive;
        var downState = Enabled ? (_downPressed ? ButtonState.Pushed : ButtonState.Normal) : ButtonState.Inactive;

        ControlPaint.DrawScrollButton(graphics, UpButtonBounds(), ScrollButton.Up, upState);
        ControlPaint.DrawScrollButton(graphics, DownButtonBounds(), ScrollButton.Down, downState);
    }
}
