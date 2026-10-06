using System.Drawing;
using System.Windows.Forms.Internal.Rendering;

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
    private bool UsePortablePickerAppearance => PortableVisualStylesEnabled && !HasExplicitAppearanceBackColor;

    private Color EffectiveBackColor
    {
        get
        {
            if (!UsePortablePickerAppearance)
            {
                return Enabled ? (ShouldSerializeBackColor() ? BackColor : SystemColors.Window) : SystemColors.Control;
            }

            var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
            return Enabled ? palette.Field : palette.Face;
        }
    }

    private Color EffectiveForeColor
    {
        get
        {
            var palette = UsePortablePickerAppearance
                ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
            if (!Enabled || _showCheckBox && !_checked)
            {
                return palette?.DisabledText ?? SystemColors.GrayText;
            }

            return HasExplicitAppearanceForeColor ? ForeColor : palette?.Text ?? SystemColors.WindowText;
        }
    }

    internal override void DefaultPaint(PaintEventArgs e)
    {
        var graphics = e.Graphics;
        var client = ClientRectangle;

        // Nothing a native control paints leaves its client: the formatted text is as wide as the
        // format makes it and the buttons start left of a client narrower than ButtonWidth, so
        // without this antialiased glyphs and button edges land on the neighbouring pixels.
        var state = graphics.Save();
        graphics.IntersectClip(client);

        try
        {
            using (var background = new SolidBrush(EffectiveBackColor))
            {
                graphics.FillRectangle(background, client);
            }

            if (_showCheckBox) PaintCheckBox(graphics);

            PaintFields(graphics);

            if (_showUpDown) PaintSpinButtons(graphics);
            else PaintDropDownButton(graphics);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void PaintCheckBox(Graphics graphics)
    {
        if (UsePortablePickerAppearance)
        {
            PortableVisualStyles.PaintGlyph(graphics, CheckBoxBounds(),
                PortableVisualStyles.Palette(PortableVisualStylePlatform), radio: false,
                selected: _checked, mixed: false, pressed: false, enabled: Enabled);
            return;
        }

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

        // The text stops where the button starts, as in the native control, instead of running under it.
        var state = graphics.Save();
        graphics.IntersectClip(TextAreaBounds());

        try
        {
            var editableSeen = -1;
            foreach (var field in RenderFields())
            {
                var bounds = FieldScreenBounds(field);
                var isEditable = IsEditable(field.Kind);
                if (isEditable) editableSeen++;

                var isSelected = focused && isEditable && editableSeen == selectedIndex;

                if (isSelected)
                {
                    var palette = UsePortablePickerAppearance
                        ? PortableVisualStyles.Palette(PortableVisualStylePlatform) : null;
                    using var highlight = new SolidBrush(palette?.Selection ?? SystemColors.Highlight);
                    graphics.FillRectangle(highlight, bounds);
                    TextRenderer.DrawText(graphics, field.Text, Font, bounds,
                        palette?.SelectedText ?? SystemColors.HighlightText,
                        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                }
                else
                {
                    TextRenderer.DrawText(graphics, field.Text, Font, bounds, EffectiveForeColor, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                }
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void PaintDropDownButton(Graphics graphics)
    {
        if (UsePortablePickerAppearance)
        {
            PaintPortablePickerButton(graphics, ButtonBounds(), _buttonPressed || _dropped, 0);
            return;
        }

        var state = ButtonState.Normal;
        if (!Enabled) state = ButtonState.Inactive;
        else if (_buttonPressed || _dropped) state = ButtonState.Pushed;

        ControlPaint.DrawComboButton(graphics, ButtonBounds(), state);
    }

    private void PaintSpinButtons(Graphics graphics)
    {
        if (UsePortablePickerAppearance)
        {
            PaintPortablePickerButton(graphics, UpButtonBounds(), _upPressed, -1);
            PaintPortablePickerButton(graphics, DownButtonBounds(), _downPressed, 1);
            return;
        }

        var upState = Enabled ? (_upPressed ? ButtonState.Pushed : ButtonState.Normal) : ButtonState.Inactive;
        var downState = Enabled ? (_downPressed ? ButtonState.Pushed : ButtonState.Normal) : ButtonState.Inactive;

        ControlPaint.DrawScrollButton(graphics, UpButtonBounds(), ScrollButton.Up, upState);
        ControlPaint.DrawScrollButton(graphics, DownButtonBounds(), ScrollButton.Down, downState);
    }

    private void PaintPortablePickerButton(Graphics graphics, Rectangle bounds, bool pressed, int arrowDirection)
    {
        bounds = Rectangle.Intersect(bounds, ClientRectangle);
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var palette = PortableVisualStyles.Palette(PortableVisualStylePlatform);
        using var background = new SolidBrush(Enabled && pressed ? palette.PressedFace : palette.Face);
        using var border = new Pen(palette.Border);
        graphics.FillRectangle(background, bounds);
        graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        if (bounds.Width < 8 || bounds.Height < 8) return;

        var centerX = bounds.Left + bounds.Width / 2;
        var centerY = bounds.Top + bounds.Height / 2 + (pressed ? 1 : 0);
        var points = arrowDirection < 0
            ? new[] { new Point(centerX - 3, centerY + 1), new Point(centerX + 3, centerY + 1), new Point(centerX, centerY - 2) }
            : new[] { new Point(centerX - 3, centerY - 1), new Point(centerX + 3, centerY - 1), new Point(centerX, centerY + 2) };
        var arrowColor = !Enabled ? palette.DisabledText
            : HasExplicitAppearanceForeColor ? ForeColor : palette.Text;
        using var arrow = new SolidBrush(arrowColor);
        graphics.FillPolygon(arrow, points);
    }
}
